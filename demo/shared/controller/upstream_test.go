package main

import (
	"encoding/json"
	"testing"
)

func TestParseMojangReleaseIds(t *testing.T) {
	var manifest mojangVersionManifest
	if err := json.Unmarshal([]byte(`{"versions":[{"id":"26.4-snapshot-4","type":"snapshot"},{"id":"26.3","type":"release"},{"id":"26.3-rc-3","type":"snapshot"},{"id":"26.2","type":"release"}]}`), &manifest); err != nil {
		t.Fatal(err)
	}

	releaseIds := parseMojangReleaseIds(manifest)
	if len(releaseIds) != 2 || releaseIds[0] != "26.3" || releaseIds[1] != "26.2" {
		t.Fatalf("unexpected releases %v", releaseIds)
	}
}

func TestPaperCandidates(t *testing.T) {
	var response paperVersionsResponse
	if err := json.Unmarshal([]byte(`{"versions":[
		{"version":{"id":"26.4","java":{"version":{"minimum":26}}},"builds":[1,2]},
		{"version":{"id":"26.3","java":{"version":{"minimum":25}}},"builds":[3,205,49]},
		{"version":{"id":"26.3-rc-3","java":{"version":{"minimum":25}}},"builds":[7]},
		{"version":{"id":"26.2","java":{"version":{"minimum":25}}},"builds":[]},
		{"version":{"id":"26.1.2","java":{"version":{"minimum":25}}},"builds":[60]}
	]}`), &response); err != nil {
		t.Fatal(err)
	}

	candidates := paperCandidates(response.Versions, 25)
	if len(candidates) != 2 || candidates[0] != (paperCandidate{MinecraftVersion: "26.3", Build: 205}) || candidates[1].MinecraftVersion != "26.1.2" {
		t.Fatalf("unexpected candidates %+v", candidates)
	}

	if all := paperCandidates(response.Versions, 0); len(all) != 3 || all[0].MinecraftVersion != "26.4" {
		t.Fatalf("unknown Java should not filter versions, got %+v", all)
	}

	if !isAcceptedPaperChannel("STABLE") || !isAcceptedPaperChannel("RECOMMENDED") || isAcceptedPaperChannel("ALPHA") || isAcceptedPaperChannel("BETA") {
		t.Fatal("unexpected channel acceptance")
	}
}

func TestSelectModrinthReleaseAndPrimaryFile(t *testing.T) {
	var versions []modrinthVersion
	if err := json.Unmarshal([]byte(`[
		{"id":"snap","project_id":"P1OZGk5p","version_number":"5.12.2-SNAPSHOT+1104","version_type":"beta","files":[{"primary":true,"filename":"a.jar","hashes":{"sha1":"aaaa"}}]},
		{"id":"5WgnasGG","project_id":"P1OZGk5p","version_number":"5.12.1","version_type":"release","game_versions":["26.2","26.3"],"files":[
			{"primary":false,"filename":"sources.jar","hashes":{"sha1":"cccc"}},
			{"primary":true,"filename":"ViaVersion-5.12.1.jar","hashes":{"sha1":"bbbb"}}
		]}
	]`), &versions); err != nil {
		t.Fatal(err)
	}

	release, ok := selectModrinthRelease(versions)
	if !ok || release.Id != "5WgnasGG" {
		t.Fatalf("unexpected release %+v", release)
	}

	file, ok := primaryModrinthFile(release)
	if !ok || file.Hashes.Sha1 != "bbbb" {
		t.Fatalf("unexpected primary file %+v", file)
	}

	if _, ok := selectModrinthRelease(versions[:1]); ok {
		t.Fatal("expected no release among betas")
	}

	fallback, ok := primaryModrinthFile(modrinthVersion{Files: []modrinthFile{{Filename: "only.jar"}}})
	if !ok || fallback.Filename != "only.jar" {
		t.Fatalf("expected the first file when none is primary, got %+v", fallback)
	}
}
