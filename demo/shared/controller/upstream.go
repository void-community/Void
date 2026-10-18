package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"slices"
	"strings"
	"time"
)

const (
	upstreamUserAgent        = "void-demo-controller (+https://github.com/void-community/Void)"
	mojangVersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"
	paperVersionsUrl         = "https://fill.papermc.io/v3/projects/paper/versions"
	paperBuildUrlFormat      = "https://fill.papermc.io/v3/projects/paper/versions/%s/builds/%d"
	modrinthApiUrl           = "https://api.modrinth.com/v2"
	voidBaseImageTagUrl      = "https://hub.docker.com/v2/namespaces/caunt/repositories/void/tags/dev"
)

type mojangVersionManifest struct {
	Versions []struct {
		Id   string `json:"id"`
		Type string `json:"type"`
	} `json:"versions"`
}

type paperVersionsResponse struct {
	Versions []paperVersionEntry `json:"versions"`
}

type paperVersionEntry struct {
	Version struct {
		Id   string `json:"id"`
		Java struct {
			Version struct {
				Minimum int `json:"minimum"`
			} `json:"version"`
		} `json:"java"`
	} `json:"version"`
	Builds []int `json:"builds"`
}

type paperBuildResponse struct {
	Id      int    `json:"id"`
	Channel string `json:"channel"`
}

type paperCandidate struct {
	MinecraftVersion string
	Build            int
}

type modrinthVersion struct {
	Id            string         `json:"id"`
	ProjectId     string         `json:"project_id"`
	VersionNumber string         `json:"version_number"`
	VersionType   string         `json:"version_type"`
	GameVersions  []string       `json:"game_versions"`
	Files         []modrinthFile `json:"files"`
}

type modrinthFile struct {
	Primary  bool   `json:"primary"`
	Filename string `json:"filename"`
	Hashes   struct {
		Sha1 string `json:"sha1"`
	} `json:"hashes"`
}

func newUpstreamClient() *http.Client {
	return &http.Client{Timeout: 30 * time.Second}
}

func (server *Server) getUpstreamJson(ctx context.Context, address string, target any) error {
	return server.sendUpstreamJson(ctx, http.MethodGet, address, nil, target)
}

func (server *Server) sendUpstreamJson(ctx context.Context, method string, address string, requestBody any, target any) error {
	var bodyReader io.Reader
	if requestBody != nil {
		body, err := json.Marshal(requestBody)
		if err != nil {
			return err
		}
		bodyReader = bytes.NewReader(body)
	}

	request, err := http.NewRequestWithContext(ctx, method, address, bodyReader)
	if err != nil {
		return err
	}
	request.Header.Set("User-Agent", upstreamUserAgent)
	request.Header.Set("Accept", "application/json")
	if requestBody != nil {
		request.Header.Set("Content-Type", "application/json")
	}

	response, err := server.UpstreamClient.Do(request)
	if err != nil {
		return err
	}
	defer response.Body.Close()

	if response.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(io.LimitReader(response.Body, 512))
		return fmt.Errorf("%s %s returned %d: %s", method, address, response.StatusCode, strings.TrimSpace(string(body)))
	}

	return json.NewDecoder(response.Body).Decode(target)
}

// fetchMojangReleaseIds returns Mojang release ids, newest first (manifest order).
func (server *Server) fetchMojangReleaseIds(ctx context.Context) ([]string, error) {
	var manifest mojangVersionManifest
	if err := server.getUpstreamJson(ctx, mojangVersionManifestUrl, &manifest); err != nil {
		return nil, err
	}

	return parseMojangReleaseIds(manifest), nil
}

func parseMojangReleaseIds(manifest mojangVersionManifest) []string {
	releaseIds := make([]string, 0, len(manifest.Versions))
	for _, version := range manifest.Versions {
		if version.Type == "release" {
			releaseIds = append(releaseIds, version.Id)
		}
	}

	return releaseIds
}

// paperCandidates keeps released Paper versions (no "-rc"/"-pre" ids) that have builds and whose Java
// minimum the backend container can run, newest first, each with its highest build.
func paperCandidates(versions []paperVersionEntry, javaMajorVersion int) []paperCandidate {
	candidates := make([]paperCandidate, 0, len(versions))
	for _, entry := range versions {
		if entry.Version.Id == "" || strings.Contains(entry.Version.Id, "-") || len(entry.Builds) == 0 {
			continue
		}

		if javaMajorVersion > 0 && entry.Version.Java.Version.Minimum > javaMajorVersion {
			continue
		}

		candidates = append(candidates, paperCandidate{MinecraftVersion: entry.Version.Id, Build: slices.Max(entry.Builds)})
	}

	return candidates
}

func isAcceptedPaperChannel(channel string) bool {
	return strings.EqualFold(channel, "STABLE") || strings.EqualFold(channel, "RECOMMENDED")
}

// resolveDesiredPaper mirrors itzg's VERSION=LATEST: the newest version whose top build is stable.
func (server *Server) resolveDesiredPaper(ctx context.Context, javaMajorVersion int) (paperCandidate, error) {
	var versions paperVersionsResponse
	if err := server.getUpstreamJson(ctx, paperVersionsUrl, &versions); err != nil {
		return paperCandidate{}, err
	}

	for _, candidate := range paperCandidates(versions.Versions, javaMajorVersion) {
		var build paperBuildResponse
		if err := server.getUpstreamJson(ctx, fmt.Sprintf(paperBuildUrlFormat, url.PathEscape(candidate.MinecraftVersion), candidate.Build), &build); err != nil {
			return paperCandidate{}, err
		}

		if isAcceptedPaperChannel(build.Channel) {
			return candidate, nil
		}
	}

	return paperCandidate{}, fmt.Errorf("no stable Paper version found for Java %d", javaMajorVersion)
}

// selectModrinthRelease returns the first release-type version, as itzg does for MODRINTH_PROJECTS.
func selectModrinthRelease(versions []modrinthVersion) (modrinthVersion, bool) {
	for _, version := range versions {
		if version.VersionType == "release" {
			return version, true
		}
	}

	return modrinthVersion{}, false
}

func primaryModrinthFile(version modrinthVersion) (modrinthFile, bool) {
	for _, file := range version.Files {
		if file.Primary {
			return file, true
		}
	}

	if len(version.Files) > 0 {
		return version.Files[0], true
	}

	return modrinthFile{}, false
}

func (server *Server) resolveDesiredPlugin(ctx context.Context, slug string, paperMinecraftVersion string) (BackendPlugin, error) {
	gameVersions, err := json.Marshal([]string{paperMinecraftVersion})
	if err != nil {
		return BackendPlugin{}, err
	}

	for _, loaders := range [][]string{{"paper"}, {"paper", "spigot"}} {
		loadersJson, err := json.Marshal(loaders)
		if err != nil {
			return BackendPlugin{}, err
		}

		address := fmt.Sprintf("%s/project/%s/version?loaders=%s&game_versions=%s", modrinthApiUrl, url.PathEscape(slug), url.QueryEscape(string(loadersJson)), url.QueryEscape(string(gameVersions)))

		var versions []modrinthVersion
		if err := server.getUpstreamJson(ctx, address, &versions); err != nil {
			return BackendPlugin{}, err
		}

		release, ok := selectModrinthRelease(versions)
		if !ok {
			continue
		}

		file, ok := primaryModrinthFile(release)
		if !ok {
			continue
		}

		return BackendPlugin{
			Slug:            slug,
			ProjectId:       release.ProjectId,
			VersionId:       release.Id,
			VersionNumber:   release.VersionNumber,
			PrimaryFileSha1: strings.ToLower(file.Hashes.Sha1),
			GameVersions:    release.GameVersions,
		}, nil
	}

	return BackendPlugin{}, fmt.Errorf("%s has no release for Paper %s yet", slug, paperMinecraftVersion)
}

func (server *Server) resolveDesiredBackend(ctx context.Context, javaMajorVersion int) (DesiredBackend, error) {
	paper, err := server.resolveDesiredPaper(ctx, javaMajorVersion)
	if err != nil {
		return DesiredBackend{}, err
	}

	desired := DesiredBackend{PaperMinecraftVersion: paper.MinecraftVersion, PaperBuild: paper.Build}
	for _, slug := range backendPluginSlugs {
		plugin, err := server.resolveDesiredPlugin(ctx, slug, paper.MinecraftVersion)
		if err != nil {
			return DesiredBackend{}, err
		}

		desired.Plugins = append(desired.Plugins, plugin)
	}

	return desired, nil
}

// lookupModrinthVersionsBySha1 maps file sha1 hashes to the Modrinth versions they belong to.
func (server *Server) lookupModrinthVersionsBySha1(ctx context.Context, sha1Hashes []string) (map[string]modrinthVersion, error) {
	versions := map[string]modrinthVersion{}
	if len(sha1Hashes) == 0 {
		return versions, nil
	}

	requestBody := map[string]any{"hashes": sha1Hashes, "algorithm": "sha1"}
	err := server.sendUpstreamJson(ctx, http.MethodPost, modrinthApiUrl+"/version_files", requestBody, &versions)
	return versions, err
}

func (server *Server) fetchModrinthProjectId(ctx context.Context, slug string) (string, error) {
	var project struct {
		Id string `json:"id"`
	}
	if err := server.getUpstreamJson(ctx, modrinthApiUrl+"/project/"+url.PathEscape(slug), &project); err != nil {
		return "", err
	}

	return project.Id, nil
}

// fetchVoidBaseImageDigest returns the current Docker Hub digest of caunt/void:dev.
func (server *Server) fetchVoidBaseImageDigest(ctx context.Context) (string, error) {
	var tag struct {
		Digest string `json:"digest"`
	}
	if err := server.getUpstreamJson(ctx, voidBaseImageTagUrl, &tag); err != nil {
		return "", err
	}

	if tag.Digest == "" {
		return "", fmt.Errorf("docker hub returned no digest for %s", voidBaseImage)
	}

	return tag.Digest, nil
}
