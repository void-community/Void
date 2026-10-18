package main

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"os"
	"regexp"
	"slices"
	"strconv"
	"strings"
	"time"
)

const (
	backendContainerName     = "itzg"
	backendHealthTimeout     = 15 * time.Minute
	backendUnknownRetryDelay = 30 * time.Second
	backendRetryBackoff      = time.Hour
	viaVersionSlug           = "viaversion"
)

// backendPluginSlugs mirrors the DEMO_MODRINTH_PROJECTS default in shared.yml.
var backendPluginSlugs = []string{"viaversion", "viabackwards", "viarewind"}

var javaVersionPattern = regexp.MustCompile(`version "(\d+)(?:\.(\d+))?`)

type BackendPlugin struct {
	Slug            string
	ProjectId       string
	VersionId       string
	VersionNumber   string
	PrimaryFileSha1 string
	GameVersions    []string
}

type DesiredBackend struct {
	PaperMinecraftVersion string
	PaperBuild            int
	Plugins               []BackendPlugin
}

type InstalledBackend struct {
	PaperMinecraftVersion string
	PaperBuild            int
	PluginFileSha1s       []string
	Health                string
	JavaMajorVersion      int
}

type BackendState struct {
	MaximumMinecraftVersion  string
	Installed                *InstalledBackend
	Pending                  *DesiredBackend
	PendingSinceUtc          time.Time
	LastAttemptedFingerprint string
	LastAttemptedUtc         time.Time
	UnhealthySinceUtc        time.Time
	Recreating               bool
	ViaVersionProjectId      string
}

// ModrinthProjects pins every plugin to the selected version id (itzg "slug:versionId" syntax), so an
// in-container restart reproduces the same server instead of drifting to an unvetted version.
func (desired DesiredBackend) ModrinthProjects() string {
	projects := make([]string, 0, len(desired.Plugins))
	for _, plugin := range desired.Plugins {
		projects = append(projects, plugin.Slug+":"+plugin.VersionId)
	}

	return strings.Join(projects, ",")
}

func (desired DesiredBackend) Fingerprint() string {
	hashes := make([]string, 0, len(desired.Plugins))
	for _, plugin := range desired.Plugins {
		hashes = append(hashes, plugin.PrimaryFileSha1)
	}
	slices.Sort(hashes)

	return fmt.Sprintf("paper=%s#%d;plugins=%s", desired.PaperMinecraftVersion, desired.PaperBuild, strings.Join(hashes, ","))
}

func (desired DesiredBackend) Describe() string {
	plugins := make([]string, 0, len(desired.Plugins))
	for _, plugin := range desired.Plugins {
		plugins = append(plugins, plugin.Slug+" "+plugin.VersionNumber)
	}

	return fmt.Sprintf("paper %s build %d, plugins [%s]", desired.PaperMinecraftVersion, desired.PaperBuild, strings.Join(plugins, ", "))
}

func (installed InstalledBackend) Describe() string {
	return fmt.Sprintf("paper %s build %d, %d plugin jars, health %s", installed.PaperMinecraftVersion, installed.PaperBuild, len(installed.PluginFileSha1s), installed.Health)
}

func (installed InstalledBackend) IsHealthy() bool {
	return installed.Health == "healthy"
}

// backendMatches reports whether the installed server already runs the desired Paper build and plugin files.
// Plugins are compared by file hash because one jar can belong to several Modrinth version ids.
func backendMatches(desired DesiredBackend, installed InstalledBackend) bool {
	if desired.PaperMinecraftVersion != installed.PaperMinecraftVersion || desired.PaperBuild != installed.PaperBuild {
		return false
	}

	return !hasMissingPlugin(desired, installed)
}

// requiresForcedRecreate reports whether the difference matters for client compatibility (a new Minecraft
// version or a new plugin release). A Paper build-only change is applied only while the demo is idle.
func requiresForcedRecreate(desired DesiredBackend, installed InstalledBackend) bool {
	return desired.PaperMinecraftVersion != installed.PaperMinecraftVersion || hasMissingPlugin(desired, installed)
}

func hasMissingPlugin(desired DesiredBackend, installed InstalledBackend) bool {
	for _, plugin := range desired.Plugins {
		if !slices.Contains(installed.PluginFileSha1s, plugin.PrimaryFileSha1) {
			return true
		}
	}

	return false
}

// computeMaximumMinecraftVersion returns the newest Mojang release the backend accepts: the Paper version
// itself or any release ViaVersion lists as supported.
func computeMaximumMinecraftVersion(releaseIdsNewestFirst []string, paperMinecraftVersion string, viaVersionGameVersions []string) string {
	for _, releaseId := range releaseIdsNewestFirst {
		if releaseId == paperMinecraftVersion || slices.Contains(viaVersionGameVersions, releaseId) {
			return releaseId
		}
	}

	return paperMinecraftVersion
}

func parsePaperManifest(data []byte) (string, int, error) {
	var manifest struct {
		MinecraftVersion string `json:"minecraftVersion"`
		Build            int    `json:"build"`
	}
	if err := json.Unmarshal(data, &manifest); err != nil {
		return "", 0, err
	}

	if manifest.MinecraftVersion == "" {
		return "", 0, fmt.Errorf("paper manifest has no minecraftVersion")
	}

	return manifest.MinecraftVersion, manifest.Build, nil
}

func parseSha1sumOutput(output string) []string {
	hashes := []string{}
	for line := range strings.Lines(output) {
		fields := strings.Fields(line)
		if len(fields) < 2 || len(fields[0]) != 40 {
			continue
		}

		hashes = append(hashes, strings.ToLower(fields[0]))
	}

	return hashes
}

func parseJavaMajorVersion(output string) int {
	match := javaVersionPattern.FindStringSubmatch(output)
	if match == nil {
		return 0
	}

	major, err := strconv.Atoi(match[1])
	if err != nil {
		return 0
	}

	if major == 1 && match[2] != "" {
		minor, err := strconv.Atoi(match[2])
		if err == nil {
			return minor
		}
	}

	return major
}

func (server *Server) readInstalledBackend(ctx context.Context) (*InstalledBackend, error) {
	installed := &InstalledBackend{}

	health, err := dockerCommand(ctx, dockerQuickCommandTimeout, "inspect", "--format", "{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}", backendContainerName)
	if err != nil {
		return nil, fmt.Errorf("cannot inspect %s: %w", backendContainerName, err)
	}
	installed.Health = strings.TrimSpace(string(health))

	manifest, err := dockerCommand(ctx, dockerQuickCommandTimeout, "exec", backendContainerName, "cat", "/data/.papermc-manifest.json")
	if err != nil {
		return nil, fmt.Errorf("cannot read Paper manifest (server still installing?): %w", err)
	}

	installed.PaperMinecraftVersion, installed.PaperBuild, err = parsePaperManifest(manifest)
	if err != nil {
		return nil, err
	}

	pluginHashes, err := dockerCommand(ctx, dockerQuickCommandTimeout, "exec", backendContainerName, "sh", "-c", "sha1sum /data/plugins/*.jar 2>/dev/null || true")
	if err != nil {
		return nil, fmt.Errorf("cannot hash plugin jars: %w", err)
	}
	installed.PluginFileSha1s = parseSha1sumOutput(string(pluginHashes))

	javaVersion, err := runDockerCommand(ctx, dockerQuickCommandTimeout, "exec", backendContainerName, "java", "-version")
	if err == nil {
		installed.JavaMajorVersion = parseJavaMajorVersion(string(javaVersion))
	}

	return installed, nil
}

func (server *Server) refreshBackend(ctx context.Context) {
	installed, err := server.readInstalledBackend(ctx)
	if err != nil {
		log.Printf("Backend: installed state unavailable: %v", err)
		server.setInstalledBackend(nil)
		server.recoverStuckBackend(ctx, 0, err.Error())
		return
	}

	server.setInstalledBackend(installed)

	desired, desiredErr := server.resolveDesiredBackend(ctx, installed.JavaMajorVersion)
	if desiredErr != nil {
		server.updateMaximumMinecraftVersion(ctx, installed, nil)
		log.Printf("Backend: keeping current backend (%s): %v", installed.Describe(), desiredErr)
		return
	}

	server.updateMaximumMinecraftVersion(ctx, installed, &desired)

	if !installed.IsHealthy() {
		server.recoverStuckBackend(ctx, installed.JavaMajorVersion, installed.Describe())
		return
	}

	server.BackendMutex.Lock()
	server.Backend.UnhealthySinceUtc = time.Time{}
	server.BackendMutex.Unlock()

	if backendMatches(desired, *installed) {
		server.BackendMutex.Lock()
		hadPending := server.Backend.Pending != nil
		server.Backend.Pending = nil
		server.BackendMutex.Unlock()

		if hadPending {
			log.Printf("Backend: pending update no longer needed")
		}

		log.Printf("Backend: up to date (%s)", desired.Describe())
		return
	}

	fingerprint := desired.Fingerprint()
	forced := requiresForcedRecreate(desired, *installed)

	server.BackendMutex.Lock()
	if fingerprint == server.Backend.LastAttemptedFingerprint && time.Since(server.Backend.LastAttemptedUtc) < backendRetryBackoff {
		retryAt := server.Backend.LastAttemptedUtc.Add(backendRetryBackoff)
		server.BackendMutex.Unlock()
		log.Printf("Backend: update to %s was already attempted and did not apply; retrying after %s", desired.Describe(), retryAt.Format(time.RFC3339))
		return
	}
	if server.Backend.Pending == nil {
		server.Backend.PendingSinceUtc = time.Now().UTC()
	}
	server.Backend.Pending = &desired
	pendingSince := server.Backend.PendingSinceUtc
	server.BackendMutex.Unlock()

	activeSessions := server.activeSessionCount()
	forcedAt := pendingSince.Add(server.BackendRecreateGrace)

	switch {
	case activeSessions == 0:
		log.Printf("Backend: updating idle server from %s to %s", installed.Describe(), desired.Describe())
	case forced && !time.Now().UTC().Before(forcedAt):
		log.Printf("Backend: grace period over, ending %d active sessions to update from %s to %s", activeSessions, installed.Describe(), desired.Describe())
		server.endActiveSessions("Minecraft server was updated, start a new session")
	case forced:
		log.Printf("Backend: update pending from %s to %s (active sessions %d, forced at %s)", installed.Describe(), desired.Describe(), activeSessions, forcedAt.Format(time.RFC3339))
		return
	default:
		log.Printf("Backend: build-only update pending from %s to %s (waiting for %d active sessions to end)", installed.Describe(), desired.Describe(), activeSessions)
		return
	}

	if err := server.recreateBackend(ctx, desired); err != nil {
		log.Printf("Backend: update to %s failed: %v", desired.Describe(), err)
	}
}

func (server *Server) recreateBackend(ctx context.Context, desired DesiredBackend) error {
	server.BackendMutex.Lock()
	server.Backend.Recreating = true
	server.Backend.LastAttemptedFingerprint = desired.Fingerprint()
	server.Backend.LastAttemptedUtc = time.Now().UTC()
	server.BackendMutex.Unlock()

	defer func() {
		server.BackendMutex.Lock()
		server.Backend.Recreating = false
		server.Backend.Pending = nil
		server.BackendMutex.Unlock()
	}()

	commandContext, cancel := context.WithTimeout(ctx, dockerComposeCommandTimeout)
	defer cancel()

	command := newDockerCommand(commandContext, "compose", "--project-name", "demo", "--file", "shared.yml", "up", "--detach", "--no-deps", "--force-recreate", "--renew-anon-volumes", "--no-build", backendContainerName)
	command.Env = append(os.Environ(),
		"DEMO_PAPER_VERSION="+desired.PaperMinecraftVersion,
		"DEMO_PAPER_BUILD="+strconv.Itoa(desired.PaperBuild),
		"DEMO_MODRINTH_PROJECTS="+desired.ModrinthProjects(),
	)

	if output, err := command.CombinedOutput(); err != nil {
		return fmt.Errorf("docker compose recreate failed: %v: %s", err, strings.TrimSpace(string(output)))
	}

	log.Printf("Backend: recreated %s with %s, waiting for it to become healthy", backendContainerName, desired.Describe())

	installed, err := server.waitForBackendHealthy(ctx)
	if err != nil {
		return err
	}

	if output, err := runDockerCommand(ctx, dockerQuickCommandTimeout, "volume", "prune", "--force"); err != nil {
		log.Printf("Backend: failed to prune old server volume: %v: %s", err, strings.TrimSpace(string(output)))
	}

	server.setInstalledBackend(installed)
	server.updateMaximumMinecraftVersion(ctx, installed, &desired)

	if !backendMatches(desired, *installed) {
		return fmt.Errorf("recreated server runs %s instead of %s", installed.Describe(), desired.Describe())
	}

	log.Printf("Backend: update complete (%s)", desired.Describe())
	return nil
}

// recoverStuckBackend recreates the backend when it has been missing or unhealthy for longer than a recreate
// may take (for example after a failed update or a crash-looping Paper build). Sessions cannot play meanwhile.
func (server *Server) recoverStuckBackend(ctx context.Context, javaMajorVersion int, reason string) {
	server.BackendMutex.Lock()
	if server.Backend.UnhealthySinceUtc.IsZero() {
		server.Backend.UnhealthySinceUtc = time.Now().UTC()
	}
	unhealthySince := server.Backend.UnhealthySinceUtc
	server.BackendMutex.Unlock()

	if time.Since(unhealthySince) < backendHealthTimeout {
		log.Printf("Backend: waiting for %s to become healthy (%s)", backendContainerName, reason)
		return
	}

	desired, err := server.resolveDesiredBackend(ctx, javaMajorVersion)
	if err != nil {
		log.Printf("Backend: %s unhealthy since %s but no replacement could be resolved: %v", backendContainerName, unhealthySince.Format(time.RFC3339), err)
		return
	}

	log.Printf("Backend: %s unhealthy since %s (%s), recreating with %s", backendContainerName, unhealthySince.Format(time.RFC3339), reason, desired.Describe())

	if err := server.recreateBackend(ctx, desired); err != nil {
		log.Printf("Backend: recovery of %s failed: %v", backendContainerName, err)
	}

	server.BackendMutex.Lock()
	server.Backend.UnhealthySinceUtc = time.Time{}
	server.BackendMutex.Unlock()
}

func (server *Server) waitForBackendHealthy(ctx context.Context) (*InstalledBackend, error) {
	deadline := time.Now().Add(backendHealthTimeout)
	for {
		installed, err := server.readInstalledBackend(ctx)
		if err == nil && installed.IsHealthy() {
			return installed, nil
		}

		if time.Now().After(deadline) {
			if err != nil {
				return nil, fmt.Errorf("%s did not become healthy within %s: %w", backendContainerName, backendHealthTimeout, err)
			}

			return nil, fmt.Errorf("%s did not become healthy within %s (%s)", backendContainerName, backendHealthTimeout, installed.Describe())
		}

		if err := sleepContext(ctx, 5*time.Second); err != nil {
			return nil, err
		}
	}
}

func (server *Server) setInstalledBackend(installed *InstalledBackend) {
	server.BackendMutex.Lock()
	server.Backend.Installed = installed
	server.BackendMutex.Unlock()
}

// updateMaximumMinecraftVersion recomputes the newest Minecraft release the installed backend accepts.
// On upstream failures it keeps the last known cap, then falls back to the installed Paper version.
func (server *Server) updateMaximumMinecraftVersion(ctx context.Context, installed *InstalledBackend, desired *DesiredBackend) {
	maximumMinecraftVersion := ""

	releaseIds, releasesErr := server.fetchMojangReleaseIds(ctx)
	viaVersionGameVersions, viaErr := server.installedViaVersionGameVersions(ctx, installed, desired)

	server.BackendMutex.Lock()
	previousMaximumMinecraftVersion := server.Backend.MaximumMinecraftVersion
	server.BackendMutex.Unlock()

	switch {
	case releasesErr == nil && viaErr == nil:
		maximumMinecraftVersion = computeMaximumMinecraftVersion(releaseIds, installed.PaperMinecraftVersion, viaVersionGameVersions)
	case previousMaximumMinecraftVersion != "":
		maximumMinecraftVersion = previousMaximumMinecraftVersion
		log.Printf("Backend: keeping client Minecraft cap %s (releases: %v, viaversion: %v)", previousMaximumMinecraftVersion, releasesErr, viaErr)
	default:
		maximumMinecraftVersion = installed.PaperMinecraftVersion
		log.Printf("Backend: using Paper version %s as client Minecraft cap (releases: %v, viaversion: %v)", maximumMinecraftVersion, releasesErr, viaErr)
	}

	server.BackendMutex.Lock()
	server.Backend.MaximumMinecraftVersion = maximumMinecraftVersion
	server.BackendMutex.Unlock()

	if maximumMinecraftVersion != previousMaximumMinecraftVersion {
		log.Printf("Backend: %s; client Minecraft cap %s", installed.Describe(), maximumMinecraftVersion)
	}
}

func (server *Server) installedViaVersionGameVersions(ctx context.Context, installed *InstalledBackend, desired *DesiredBackend) ([]string, error) {
	if desired != nil {
		for _, plugin := range desired.Plugins {
			if plugin.Slug == viaVersionSlug && slices.Contains(installed.PluginFileSha1s, plugin.PrimaryFileSha1) {
				return plugin.GameVersions, nil
			}
		}
	}

	server.BackendMutex.Lock()
	viaVersionProjectId := server.Backend.ViaVersionProjectId
	server.BackendMutex.Unlock()

	if viaVersionProjectId == "" {
		projectId, err := server.fetchModrinthProjectId(ctx, viaVersionSlug)
		if err != nil {
			return nil, err
		}

		viaVersionProjectId = projectId
		server.BackendMutex.Lock()
		server.Backend.ViaVersionProjectId = projectId
		server.BackendMutex.Unlock()
	}

	versions, err := server.lookupModrinthVersionsBySha1(ctx, installed.PluginFileSha1s)
	if err != nil {
		return nil, err
	}

	for _, version := range versions {
		if version.ProjectId == viaVersionProjectId {
			return version.GameVersions, nil
		}
	}

	return nil, fmt.Errorf("installed plugins do not include a known ViaVersion build")
}

// waitForBackendReady blocks until the backend is healthy, not being recreated and has a known client cap.
func (server *Server) waitForBackendReady(ctx context.Context) (string, error) {
	for {
		server.BackendMutex.Lock()
		ready := !server.Backend.Recreating && server.Backend.Installed != nil && server.Backend.Installed.IsHealthy() && server.Backend.MaximumMinecraftVersion != ""
		maximumMinecraftVersion := server.Backend.MaximumMinecraftVersion
		server.BackendMutex.Unlock()

		if ready {
			return maximumMinecraftVersion, nil
		}

		if err := sleepContext(ctx, time.Second); err != nil {
			return "", err
		}
	}
}

func (server *Server) isBackendUpdating() bool {
	server.BackendMutex.Lock()
	defer server.BackendMutex.Unlock()

	return server.Backend.Recreating || server.Backend.Installed == nil || !server.Backend.Installed.IsHealthy() || server.Backend.MaximumMinecraftVersion == ""
}

func (server *Server) requestRefreshIfBackendPending() {
	server.BackendMutex.Lock()
	pending := server.Backend.Pending != nil
	server.BackendMutex.Unlock()

	if !pending {
		return
	}

	select {
	case server.RefreshRequested <- struct{}{}:
	default:
	}
}

// nextBackendRefreshDelay polls quickly until the backend state is known and healthy.
func (server *Server) nextRefreshDelay() time.Duration {
	if server.isBackendUpdating() && server.RefreshInterval > backendUnknownRetryDelay {
		return backendUnknownRetryDelay
	}

	return server.RefreshInterval
}
