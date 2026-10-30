package builtin

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// makeSkillSource creates a fake skill source directory: SKILL.md with
// the exe anchor, a references/ subdirectory, a stray exe, and a stale
// manifest — everything install must handle.
func makeSkillSource(t *testing.T) string {
	t.Helper()
	dir := t.TempDir()

	skillMD := "---\nname: \"Revit-CLI\"\n---\n\n# Revit CLI Client\n\n" + skillExeAnchor + "\n"
	if err := os.WriteFile(filepath.Join(dir, skillFileName), []byte(skillMD), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(filepath.Join(dir, "references"), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(dir, "references", "api.md"), []byte("ref"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(dir, "revit-cli.exe"), []byte("fake"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(dir, skillManifestName), []byte("stale"), 0o644); err != nil {
		t.Fatal(err)
	}
	return dir
}

func TestFindSkillSourceDirEnvOverride(t *testing.T) {
	dir := makeSkillSource(t)
	t.Setenv(skillEnvDir, dir)
	if got := findSkillSourceDir(); got != dir {
		t.Errorf("findSkillSourceDir() = %q, want %q", got, dir)
	}

	t.Setenv(skillEnvDir, filepath.Join(dir, "nope"))
	if got := findSkillSourceDir(); got != "" {
		t.Errorf("findSkillSourceDir() with invalid env = %q, want \"\"", got)
	}
}

func TestRewriteSkillContent(t *testing.T) {
	exe := `C:\tools\revit-cli.exe`

	// Anchor present: replaced in place, nothing appended.
	in := "intro\n\n" + skillExeAnchor + "\n\noutro\n"
	out, ok := rewriteSkillContent(in, exe)
	if !ok {
		t.Fatal("expected ok=true when anchor present")
	}
	if strings.Contains(out, skillExeAnchor) {
		t.Error("anchor not removed")
	}
	if !strings.Contains(out, "located at `"+exe+"`") {
		t.Errorf("rewritten content missing absolute path: %q", out)
	}
	if !strings.Contains(out, "outro") {
		t.Error("replacement clobbered surrounding content")
	}

	// Anchor missing: fallback appends a Local Binary Path section.
	out, ok = rewriteSkillContent("# no anchor here\n", exe)
	if ok {
		t.Error("expected ok=false when anchor missing")
	}
	if !strings.Contains(out, "## Local Binary Path") || !strings.Contains(out, exe) {
		t.Errorf("fallback section missing: %q", out)
	}
}

func TestSkillManifestRoundTrip(t *testing.T) {
	dir := t.TempDir()
	if m := readSkillManifest(dir); m != nil {
		t.Fatalf("readSkillManifest on empty dir = %v, want nil", m)
	}

	want := &SkillManifest{
		SkillName:   skillDirName,
		SourceExe:   `C:\x\revit-cli.exe`,
		Version:     "1.2.3",
		InstalledAt: "2026-10-30T00:00:00Z",
	}
	if err := writeSkillManifest(dir, want); err != nil {
		t.Fatal(err)
	}
	got := readSkillManifest(dir)
	if got == nil {
		t.Fatal("manifest not readable after write")
	}
	if *got != *want {
		t.Errorf("round trip = %+v, want %+v", *got, *want)
	}

	// A manifest for another skill must not be adopted.
	other := *want
	other.SkillName = "some-other-skill"
	if err := writeSkillManifest(dir, &other); err != nil {
		t.Fatal(err)
	}
	if m := readSkillManifest(dir); m != nil {
		t.Errorf("foreign manifest adopted: %+v", m)
	}
}

func TestParseSelection(t *testing.T) {
	// Empty input cancels.
	idx, ok := parseSelection("", 3)
	if !ok || len(idx) != 0 {
		t.Errorf("empty input = %v,%v; want cancel", idx, ok)
	}
	// Whitespace cancels too.
	idx, ok = parseSelection("  \n", 3)
	if !ok || len(idx) != 0 {
		t.Errorf("blank input = %v,%v; want cancel", idx, ok)
	}

	// all selects everything.
	idx, ok = parseSelection("ALL", 3)
	if !ok || len(idx) != 3 {
		t.Errorf("ALL = %v,%v; want 3 indices", idx, ok)
	}

	// Comma list with duplicates and spaces.
	idx, ok = parseSelection("3, 1,1", 3)
	if !ok || len(idx) != 2 || idx[0] != 2 || idx[1] != 0 {
		t.Errorf("'3, 1,1' = %v,%v; want [2 0]", idx, ok)
	}

	// Out of range and garbage are rejected. ("1,," is accepted leniently:
	// FieldsFunc drops empty segments, leaving a valid "1".)
	for _, bad := range []string{"0", "4", "1,x", "x", "-1"} {
		if _, ok := parseSelection(bad, 3); ok {
			t.Errorf("parseSelection(%q) accepted, want rejected", bad)
		}
	}
}

func TestFindHarnesses(t *testing.T) {
	hs, err := findHarnesses("Claude, CODEBUDDY,claude")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(hs) != 2 || hs[0].ID != "claude" || hs[1].ID != "codebuddy" {
		t.Errorf("findHarnesses = %+v, want [claude codebuddy]", hs)
	}

	if _, err := findHarnesses("claude,nope"); err == nil {
		t.Error("unknown id accepted, want error")
	} else if !strings.Contains(err.Error(), harnessIDs()) {
		t.Errorf("error does not list valid ids: %v", err)
	}

	if _, err := findHarnesses(" , "); err == nil {
		t.Error("empty list accepted, want error")
	}
}

func TestSkillCandidatesScopes(t *testing.T) {
	// Project scope: chdir into a temp dir that has a .claude harness dir
	// with an installed skill, plus an empty .cursor dir.
	proj := t.TempDir()
	wd, err := os.Getwd()
	if err != nil {
		t.Fatal(err)
	}
	if err := os.Chdir(proj); err != nil {
		t.Fatal(err)
	}
	defer os.Chdir(wd)

	installed := filepath.Join(proj, ".claude", "skills", skillDirName)
	if err := os.MkdirAll(installed, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := writeSkillManifest(installed, &SkillManifest{SkillName: skillDirName, Version: "9.9.9"}); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(filepath.Join(proj, ".cursor"), 0o755); err != nil {
		t.Fatal(err)
	}

	cands, err := skillCandidates(false)
	if err != nil {
		t.Fatal(err)
	}
	byID := map[string]skillCandidate{}
	for _, c := range cands {
		byID[c.Agent] = c
	}
	if c := byID["claude"]; !c.Detected || c.Manifest == nil || c.Manifest.Version != "9.9.9" {
		t.Errorf("claude candidate = %+v, want detected+installed", c)
	}
	if c := byID["cursor"]; !c.Detected || c.Manifest != nil {
		t.Errorf("cursor candidate = %+v, want detected without manifest", c)
	}
	if c := byID["codebuddy"]; c.Detected {
		t.Errorf("codebuddy candidate = %+v, want not detected", c)
	}
	if got := byID["claude"].Dir; !strings.HasPrefix(strings.ToLower(got), strings.ToLower(proj)) {
		t.Errorf("claude dir rooted outside cwd: %s (cwd %s)", got, proj)
	}
}

func TestCopySkillFilesAndInstallDir(t *testing.T) {
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir()) // isolate install index writes
	src := makeSkillSource(t)
	dst := filepath.Join(t.TempDir(), "out", skillDirName)

	c := skillCandidate{Agent: "claude", Scope: skillScopeProject, Dir: dst, Detected: true}
	if err := installSkillDir(src, "# rewritten skill body\n", `C:\bin\revit-cli.exe`, c); err != nil {
		t.Fatal(err)
	}

	// Rewritten SKILL.md (not the source copy).
	data, err := os.ReadFile(filepath.Join(dst, skillFileName))
	if err != nil {
		t.Fatal(err)
	}
	if string(data) != "# rewritten skill body\n" {
		t.Errorf("SKILL.md = %q, want rewritten content", data)
	}

	// references/ copied recursively.
	if b, err := os.ReadFile(filepath.Join(dst, "references", "api.md")); err != nil || string(b) != "ref" {
		t.Errorf("references/api.md not copied: %v", err)
	}

	// Exe and stale manifest excluded.
	if fileExists(filepath.Join(dst, "revit-cli.exe")) {
		t.Error("revit-cli.exe must not be copied into installs")
	}
	if stale, _ := os.ReadFile(filepath.Join(dst, skillManifestName)); string(stale) == "stale" {
		t.Error("stale source manifest leaked into install")
	}

	// Fresh manifest written and readable.
	m := readSkillManifest(dst)
	if m == nil || m.SkillName != skillDirName || m.SourceExe != `C:\bin\revit-cli.exe` {
		t.Errorf("install manifest = %+v", m)
	}
}

func TestCopySkillFilesOverlapGuard(t *testing.T) {
	// Regression: installing into a directory inside the source must not
	// recurse into the growing destination (used to hit path-too-long).
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	src := makeSkillSource(t)
	insideDst := filepath.Join(src, "nested", skillDirName)
	c := skillCandidate{Agent: "custom", Scope: skillScopeCustom, Dir: insideDst}
	if err := installSkillDir(src, "# skill\n", `C:\x\revit-cli.exe`, c); err != nil {
		t.Fatal(err)
	}
	if !fileExists(filepath.Join(insideDst, skillFileName)) {
		t.Error("SKILL.md not installed into nested target")
	}
	// Release-zip scenario: destination nested inside the source must
	// still copy references/ (only entries containing dst are skipped).
	if b, err := os.ReadFile(filepath.Join(insideDst, "references", "api.md")); err != nil || string(b) != "ref" {
		t.Errorf("references not copied into nested target: %v", err)
	}
	if m := readSkillManifest(insideDst); m == nil {
		t.Error("manifest missing for nested target")
	}
	// Source SKILL.md must survive (no self-truncation).
	if b, err := os.ReadFile(filepath.Join(src, skillFileName)); err != nil || !strings.Contains(string(b), skillExeAnchor) {
		t.Errorf("source SKILL.md corrupted: %v", err)
	}

	// Equal dirs must be a no-op for the bulk copy.
	if err := copySkillFiles(src, src); err != nil {
		t.Fatalf("copySkillFiles(src, src) errored: %v", err)
	}
	if b, err := os.ReadFile(filepath.Join(src, skillFileName)); err != nil || !strings.Contains(string(b), skillExeAnchor) {
		t.Errorf("self-copy corrupted SKILL.md: %v", err)
	}
}

func TestFindInstalledSkills(t *testing.T) {
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir()) // isolate install index reads
	proj := t.TempDir()
	wd, err := os.Getwd()
	if err != nil {
		t.Fatal(err)
	}
	if err := os.Chdir(proj); err != nil {
		t.Fatal(err)
	}
	defer os.Chdir(wd)

	// One installed project-level location and one unmanaged lookalike.
	installed := filepath.Join(proj, ".claude", "skills", skillDirName)
	if err := os.MkdirAll(installed, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := writeSkillManifest(installed, &SkillManifest{SkillName: skillDirName, Version: "1.0.0"}); err != nil {
		t.Fatal(err)
	}
	lookalike := filepath.Join(proj, ".cursor", "skills", skillDirName)
	if err := os.MkdirAll(lookalike, 0o755); err != nil {
		t.Fatal(err)
	}

	found := findInstalledSkills()
	dirs := map[string]bool{}
	for _, c := range found {
		dirs[strings.ToLower(c.Dir)] = true
	}
	if !dirs[strings.ToLower(installed)] {
		t.Errorf("installed location missing from %v", found)
	}
	if dirs[strings.ToLower(lookalike)] {
		t.Error("unmanaged directory reported as installed")
	}
}

func TestSameDir(t *testing.T) {
	if !sameDir(`C:\a\skills\revit-cli`, `c:\a\skills\REVIT-CLI\`) {
		t.Error("case/trailing-slash variants should match")
	}
	if sameDir(`C:\a\revit-cli`, `C:\a\revit-cli-2`) {
		t.Error("distinct directories must not match")
	}
}

func TestInstallSkillDirRefusesSourceSelf(t *testing.T) {
	// Regression: --target resolving to the source directory itself used
	// to overwrite SKILL.md in place and plant a manifest there, turning
	// the executable's own directory into a removable "install".
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	src := makeSkillSource(t)
	c := skillCandidate{Agent: "custom", Scope: skillScopeCustom, Dir: src}
	err := installSkillDir(src, "# rewritten\n", `C:\x\revit-cli.exe`, c)
	if err == nil {
		t.Fatal("expected refusal when target equals the source directory")
	}
	if !strings.Contains(err.Error(), "source directory") {
		t.Errorf("error not self-describing: %v", err)
	}
	// Source must be untouched: anchor intact, no manifest planted.
	if b, rerr := os.ReadFile(filepath.Join(src, skillFileName)); rerr != nil || !strings.Contains(string(b), skillExeAnchor) {
		t.Errorf("source SKILL.md overwritten: %v", rerr)
	}
	if fileExists(filepath.Join(src, skillManifestName)) {
		// makeSkillSource seeds a stale manifest; install must not have
		// rewritten it — verify it is still the stale content.
		if b, rerr := os.ReadFile(filepath.Join(src, skillManifestName)); rerr != nil || string(b) != "stale" {
			t.Error("manifest was written into the source directory")
		}
	}
}

func TestDeregisterSkillDir(t *testing.T) {
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	dir := t.TempDir()

	// Idempotent on a directory without a manifest.
	if err := deregisterSkillDir(dir); err != nil {
		t.Fatalf("deregisterSkillDir without manifest = %v, want nil", err)
	}

	if err := writeSkillManifest(dir, &SkillManifest{SkillName: skillDirName, Version: "1.0.0"}); err != nil {
		t.Fatal(err)
	}
	upsertSkillIndex(skillInstallRecord{Agent: "custom", Scope: skillScopeCustom, Dir: dir, Manifest: SkillManifest{SkillName: skillDirName}})

	// Other records in the index must survive.
	other := t.TempDir()
	upsertSkillIndex(skillInstallRecord{Agent: "claude", Scope: skillScopeProject, Dir: other, Manifest: SkillManifest{SkillName: skillDirName}})

	if err := deregisterSkillDir(dir); err != nil {
		t.Fatalf("deregisterSkillDir = %v, want nil", err)
	}
	if m := readSkillManifest(dir); m != nil {
		t.Error("manifest not removed by deregistration")
	}
	for _, r := range loadSkillIndex() {
		if strings.EqualFold(r.Dir, dir) {
			t.Error("index record not removed by deregistration")
		}
	}
	found := false
	for _, r := range loadSkillIndex() {
		if strings.EqualFold(r.Dir, other) {
			found = true
		}
	}
	if !found {
		t.Error("unrelated index record was dropped")
	}
	// The directory itself must remain (only bookkeeping is undone).
	if !dirExists(dir) {
		t.Error("directory was removed by deregistration")
	}
}
