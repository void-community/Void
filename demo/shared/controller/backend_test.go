package main

import (
	"testing"
)

func TestComputeMaximumMinecraftVersion(t *testing.T) {
	releases := []string{"26.4", "26.3", "26.2", "26.1.2"}

	cases := []struct {
		name        string
		paper       string
		viaVersion  []string
		expectedCap string
	}{
		{name: "via ahead of paper", paper: "26.2", viaVersion: []string{"26.1.2", "26.2", "26.3"}, expectedCap: "26.3"},
		{name: "paper ahead of via", paper: "26.4", viaVersion: []string{"26.2", "26.3"}, expectedCap: "26.4"},
		{name: "snapshots ignored", paper: "26.3", viaVersion: []string{"26.3", "26.4-snapshot-4"}, expectedCap: "26.3"},
		{name: "no via versions", paper: "26.2", viaVersion: nil, expectedCap: "26.2"},
		{name: "paper unknown to manifest", paper: "26.5", viaVersion: nil, expectedCap: "26.5"},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if actual := computeMaximumMinecraftVersion(releases, testCase.paper, testCase.viaVersion); actual != testCase.expectedCap {
				t.Fatalf("expected %q, got %q", testCase.expectedCap, actual)
			}
		})
	}
}

func TestBackendMatchesAndForcedRecreate(t *testing.T) {
	desired := DesiredBackend{
		PaperMinecraftVersion: "26.3",
		PaperBuild:            205,
		Plugins: []BackendPlugin{
			{Slug: "viaversion", VersionId: "a", PrimaryFileSha1: "1111111111111111111111111111111111111111"},
			{Slug: "viabackwards", VersionId: "b", PrimaryFileSha1: "2222222222222222222222222222222222222222"},
		},
	}
	installedPlugins := []string{"2222222222222222222222222222222222222222", "1111111111111111111111111111111111111111"}

	matching := InstalledBackend{PaperMinecraftVersion: "26.3", PaperBuild: 205, PluginFileSha1s: installedPlugins}
	if !backendMatches(desired, matching) {
		t.Fatal("expected identical backend to match")
	}

	buildOnly := InstalledBackend{PaperMinecraftVersion: "26.3", PaperBuild: 200, PluginFileSha1s: installedPlugins}
	if backendMatches(desired, buildOnly) || requiresForcedRecreate(desired, buildOnly) {
		t.Fatal("expected a build-only difference to mismatch without forcing a recreate")
	}

	newMinecraft := InstalledBackend{PaperMinecraftVersion: "26.2", PaperBuild: 112, PluginFileSha1s: installedPlugins}
	if !requiresForcedRecreate(desired, newMinecraft) {
		t.Fatal("expected a new Minecraft version to force a recreate")
	}

	newPlugin := InstalledBackend{PaperMinecraftVersion: "26.3", PaperBuild: 205, PluginFileSha1s: installedPlugins[:1]}
	if backendMatches(desired, newPlugin) || !requiresForcedRecreate(desired, newPlugin) {
		t.Fatal("expected a missing plugin file to force a recreate")
	}
}

func TestFingerprintAndModrinthProjects(t *testing.T) {
	desired := DesiredBackend{
		PaperMinecraftVersion: "26.3",
		PaperBuild:            205,
		Plugins: []BackendPlugin{
			{Slug: "viaversion", VersionId: "5WgnasGG", PrimaryFileSha1: "bbbb"},
			{Slug: "viabackwards", VersionId: "x1", PrimaryFileSha1: "aaaa"},
		},
	}

	if actual := desired.ModrinthProjects(); actual != "viaversion:5WgnasGG,viabackwards:x1" {
		t.Fatalf("unexpected projects %q", actual)
	}

	if actual := desired.Fingerprint(); actual != "paper=26.3#205;plugins=aaaa,bbbb" {
		t.Fatalf("unexpected fingerprint %q", actual)
	}
}

func TestParsePaperManifest(t *testing.T) {
	version, build, err := parsePaperManifest([]byte(`{"@type":"me.itzg.helpers.paper.PaperManifest","files":["paper-26.2-112.jar"],"minecraftVersion":"26.2","project":"paper","build":112}`))
	if err != nil || version != "26.2" || build != 112 {
		t.Fatalf("unexpected result %q %d %v", version, build, err)
	}

	if _, _, err := parsePaperManifest([]byte(`{}`)); err == nil {
		t.Fatal("expected an error for a manifest without minecraftVersion")
	}
}

func TestParseSha1sumOutput(t *testing.T) {
	output := "589AF92E75BC1D5B241CFEBBBEC7840CF42AA614  /data/plugins/ViaVersion-5.12.1.jar\n" +
		"0123456789abcdef0123456789abcdef01234567  /data/plugins/ViaRewind-4.2.0.jar\n" +
		"sha1sum: /data/plugins/*.jar: No such file or directory\n"

	hashes := parseSha1sumOutput(output)
	if len(hashes) != 2 || hashes[0] != "589af92e75bc1d5b241cfebbbec7840cf42aa614" {
		t.Fatalf("unexpected hashes %v", hashes)
	}
}

func TestParseJavaMajorVersion(t *testing.T) {
	cases := map[string]int{
		`openjdk version "25.0.3" 2026-04-21 LTS`: 25,
		`openjdk version "21" 2023-09-19`:         21,
		`java version "1.8.0_402"`:                8,
		`garbage`:                                 0,
	}

	for output, expected := range cases {
		if actual := parseJavaMajorVersion(output); actual != expected {
			t.Fatalf("%q: expected %d, got %d", output, expected, actual)
		}
	}
}

func TestSanitizeForDockerName(t *testing.T) {
	if actual := sanitizeForDockerName("6gE1LDNHjgYRrVKUdO5u6aqHspCTAHgdTaBGtipDjuo"); actual != "6ge1ldnhjgyrrvkudo5u6aqhspctahgdtabgtipdjuo" {
		t.Fatalf("unexpected name %q", actual)
	}

	if actual := sanitizeForDockerName("LLfY4ysi7bvxJ827_lMnnvhKuq-HAZ27BeT5MjCdZIY"); actual != "llfy4ysi7bvxj827-lmnnvhkuq-haz27bet5mjcdziy" {
		t.Fatalf("unexpected name %q", actual)
	}

	if actual := sanitizeForDockerName("__"); actual != "session" {
		t.Fatalf("unexpected name %q", actual)
	}
}
