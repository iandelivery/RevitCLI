package builtin

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"

	"revit-cli/internal/client/discovery"
)

// Skill file names and layout constants.
const (
	// skillDirName is the per-skill directory every harness expects under
	// its skills root (e.g. .claude/skills/revit-cli/SKILL.md).
	skillDirName = "revit-cli"
	// skillFileName is the skill definition file (Open Skill protocol).
	skillFileName = "SKILL.md"
	// skillManifestName is the install manifest written next to SKILL.md in
	// every installed location. It marks the directory as managed by
	// revit-cli (so uninstall never touches user-maintained skill dirs)
	// and records provenance for show/update.
	skillManifestName = ".revit-cli-skill.json"
	// skillEnvDir mirrors BRIDGE_DIR: explicit override for the skill
	// source directory containing SKILL.md.
	skillEnvDir = "REVIT_CLI_SKILL_DIR"
	// skillIndexName is the central install registry stored in the CLI
	// data directory. It tracks custom --target installs that cannot be
	// rediscovered by scanning known harness directories.
	skillIndexName = "skill-installs.json"
)

// skillExeAnchor is the exact sentence in SKILL.md that locates the
// executable relative to the markdown file. It holds true inside release
// zips (exe next to SKILL.md) but not once the skill is installed into a
// harness directory, so install rewrites it to an absolute path.
const skillExeAnchor = "The `revit-cli.exe` binary is located in the same directory as this markdown file."

// ClientVersion is injected from main (set at build time via ldflags).
// Used in manifests so `skill show` can report versions.
var ClientVersion string

// HarnessSpec describes one AI coding agent (harness) and the directory
// convention it uses for skills. The same relative directory applies to
// both scopes: project-level (<cwd>/<Dir>/skills) and user-level
// (<home>/<Dir>/skills). This registry is the single point to adjust or
// extend harness support.
type HarnessSpec struct {
	ID  string // CLI-facing id, e.g. "claude" (used with --agent)
	Dir string // harness config dir relative to cwd or home, e.g. ".claude"
}

// harnessRegistry lists known harnesses in stable display order.
var harnessRegistry = []HarnessSpec{
	{"claude", ".claude"},
	{"cursor", ".cursor"},
	{"codex", ".codex"},
	{"trae", ".trae"},
	{"trae-cn", ".trae-cn"},
	{"codebuddy", ".codebuddy"},
	{"workbuddy", ".workbuddy"},
	{"kimi-work", ".kimi-work"},
	{"kimi-code", ".kimi-code"},
}

// harnessIDs returns the registry ids joined for error messages.
func harnessIDs() string {
	ids := make([]string, len(harnessRegistry))
	for i, h := range harnessRegistry {
		ids[i] = h.ID
	}
	return strings.Join(ids, ", ")
}

// findHarness resolves a comma-separated list of harness ids (case
// insensitive). Unknown ids produce an error listing all valid ids so
// agents can self-correct without extra docs.
func findHarnesses(ids string) ([]HarnessSpec, error) {
	var out []HarnessSpec
	seen := map[string]bool{}
	for _, part := range strings.Split(ids, ",") {
		id := strings.ToLower(strings.TrimSpace(part))
		if id == "" || seen[id] {
			continue
		}
		var found *HarnessSpec
		for i := range harnessRegistry {
			if harnessRegistry[i].ID == id {
				found = &harnessRegistry[i]
				break
			}
		}
		if found == nil {
			return nil, fmt.Errorf("unknown agent id %q (valid: %s)", id, harnessIDs())
		}
		seen[id] = true
		out = append(out, *found)
	}
	if len(out) == 0 {
		return nil, fmt.Errorf("no valid agent id in %q (valid: %s)", ids, harnessIDs())
	}
	return out, nil
}

// findSkillSourceDir locates the source skill directory (containing
// SKILL.md), mirroring findBridgeFiles' conventions:
//
//  1. REVIT_CLI_SKILL_DIR env var pointing at the directory
//  2. <exe dir>/SKILL.md — release zip layout (exe next to SKILL.md);
//     also the client/build.ps1 layout (exe moved into the skill dir)
//  3. <exe dir>/skills/revit-cli/SKILL.md — repo layout
func findSkillSourceDir() string {
	if dir := os.Getenv(skillEnvDir); dir != "" && fileExists(filepath.Join(dir, skillFileName)) {
		return dir
	}
	exePath, err := os.Executable()
	if err != nil {
		return ""
	}
	exeDir := filepath.Dir(exePath)
	if fileExists(filepath.Join(exeDir, skillFileName)) {
		return exeDir
	}
	nested := filepath.Join(exeDir, "skills", skillDirName)
	if fileExists(filepath.Join(nested, skillFileName)) {
		return nested
	}
	return ""
}

// rewriteSkillContent replaces the relative exe anchor sentence with the
// absolute path of the installing executable. Returns ok=false when the
// anchor is missing (e.g. older SKILL.md), in which case a Local Binary
// Path section is appended so installation never fails.
func rewriteSkillContent(content, exePath string) (string, bool) {
	replacement := fmt.Sprintf("The `revit-cli.exe` binary is located at `%s`.", exePath)
	if strings.Contains(content, skillExeAnchor) {
		return strings.Replace(content, skillExeAnchor, replacement, 1), true
	}
	return content + "\n## Local Binary Path\n\n" + replacement + "\n", false
}

// SkillManifest is written into every installed skill directory and is
// the single data source for skill show/update/uninstall.
type SkillManifest struct {
	SkillName   string `json:"skill_name"`
	SourceExe   string `json:"source_exe"`
	Version     string `json:"version"`
	InstalledAt string `json:"installed_at"`
}

// readSkillManifest loads the manifest from an installed skill dir, or
// nil when the directory is not managed by revit-cli.
func readSkillManifest(dir string) *SkillManifest {
	data, err := os.ReadFile(filepath.Join(dir, skillManifestName))
	if err != nil {
		return nil
	}
	var m SkillManifest
	if json.Unmarshal(data, &m) != nil || m.SkillName != skillDirName {
		return nil
	}
	return &m
}

// writeSkillManifest stores the manifest into an installed skill dir.
func writeSkillManifest(dir string, m *SkillManifest) error {
	data, err := json.MarshalIndent(m, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(dir, skillManifestName), data, 0o644)
}

// skillScope distinguishes install locations.
type skillScope string

const (
	skillScopeProject skillScope = "project"
	skillScopeUser    skillScope = "user"
	skillScopeCustom  skillScope = "custom"
)

// skillCandidate is one potential install/inspection target.
type skillCandidate struct {
	Agent    string         `json:"agent"`             // harness id or "custom"
	Scope    skillScope     `json:"scope"`             // project | user | custom
	Dir      string         `json:"dir"`               // absolute skill dir (.../skills/revit-cli)
	Detected bool           `json:"detected"`          // harness config dir exists
	Manifest *SkillManifest `json:"manifest,omitempty"` // present when installed by revit-cli
}

// skillBaseDir returns the directory harness dirs are resolved against:
// cwd for project scope, the user home for --global.
func skillBaseDir(global bool) (string, error) {
	if global {
		return os.UserHomeDir()
	}
	return os.Getwd()
}

// skillCandidates enumerates every registered harness for one scope,
// flagging which are detected (harness dir exists) and already installed.
func skillCandidates(global bool) ([]skillCandidate, error) {
	base, err := skillBaseDir(global)
	if err != nil {
		return nil, err
	}
	scope := skillScopeProject
	if global {
		scope = skillScopeUser
	}
	var out []skillCandidate
	for _, h := range harnessRegistry {
		root := filepath.Join(base, h.Dir)
		dir := filepath.Join(root, "skills", skillDirName)
		c := skillCandidate{Agent: h.ID, Scope: scope, Dir: dir, Detected: dirExists(root)}
		if m := readSkillManifest(dir); m != nil {
			c.Manifest = m
		}
		out = append(out, c)
	}
	return out, nil
}

// --- central install index (custom --target bookkeeping) ---

// skillInstallRecord persists one install location in skillIndexName.
type skillInstallRecord struct {
	Agent    string        `json:"agent"`
	Scope    skillScope    `json:"scope"`
	Dir      string        `json:"dir"`
	Manifest SkillManifest `json:"manifest"`
}

func skillIndexPath() string {
	return filepath.Join(discovery.DataDir(), skillIndexName)
}

// loadSkillIndex reads the install registry; missing file is not an error.
func loadSkillIndex() []skillInstallRecord {
	data, err := os.ReadFile(skillIndexPath())
	if err != nil {
		return nil
	}
	var recs []skillInstallRecord
	if json.Unmarshal(data, &recs) != nil {
		return nil
	}
	return recs
}

// saveSkillIndex atomically rewrites the install registry.
func saveSkillIndex(recs []skillInstallRecord) error {
	data, err := json.MarshalIndent(recs, "", "  ")
	if err != nil {
		return err
	}
	if err := os.MkdirAll(discovery.DataDir(), 0o755); err != nil {
		return err
	}
	tmp := skillIndexPath() + ".tmp"
	if err := os.WriteFile(tmp, data, 0o644); err != nil {
		return err
	}
	return os.Rename(tmp, skillIndexPath())
}

// upsertSkillIndex adds or refreshes one install record.
func upsertSkillIndex(rec skillInstallRecord) {
	recs := loadSkillIndex()
	for i := range recs {
		if strings.EqualFold(recs[i].Dir, rec.Dir) {
			recs[i] = rec
			saveSkillIndex(recs)
			return
		}
	}
	recs = append(recs, rec)
	saveSkillIndex(recs)
}

// removeSkillIndexDir drops an install record after uninstall.
func removeSkillIndexDir(dir string) {
	recs := loadSkillIndex()
	var out []skillInstallRecord
	for _, r := range recs {
		if !strings.EqualFold(r.Dir, dir) {
			out = append(out, r)
		}
	}
	saveSkillIndex(out)
}

// deregisterSkillDir undoes the bookkeeping of one install without
// touching the directory itself: it removes the install manifest (the
// marker that makes the location show up as "installed") and the index
// record. Used by uninstall for the running executable's own directory,
// which must never be deleted. A missing manifest is not an error.
func deregisterSkillDir(dir string) error {
	if err := os.Remove(filepath.Join(dir, skillManifestName)); err != nil && !os.IsNotExist(err) {
		return err
	}
	removeSkillIndexDir(dir)
	return nil
}

// findInstalledSkills returns every location currently managed by
// revit-cli: known harness dirs in both scopes plus index records,
// deduplicated and sorted by path. Used by update/uninstall/show.
func findInstalledSkills() []skillCandidate {
	seen := map[string]bool{}
	var out []skillCandidate
	add := func(c skillCandidate) {
		key := strings.ToLower(c.Dir)
		if seen[key] {
			return
		}
		seen[key] = true
		out = append(out, c)
	}
	for _, global := range []bool{false, true} {
		if cs, err := skillCandidates(global); err == nil {
			for _, c := range cs {
				if c.Manifest != nil {
					add(c)
				}
			}
		}
	}
	for _, r := range loadSkillIndex() {
		if m := readSkillManifest(r.Dir); m != nil {
			add(skillCandidate{Agent: r.Agent, Scope: r.Scope, Dir: r.Dir, Detected: true, Manifest: m})
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Dir < out[j].Dir })
	return out
}

// sameDir reports whether two paths resolve to the same directory
// (case-insensitive best effort for Windows).
func sameDir(a, b string) bool {
	aa, ea := filepath.Abs(a)
	bb, eb := filepath.Abs(b)
	return ea == nil && eb == nil && strings.EqualFold(aa, bb)
}

// pathWithin reports whether p is equal to or inside dir (best effort;
// on Rel errors it returns false).
func pathWithin(dir, p string) bool {
	rel, err := filepath.Rel(dir, p)
	if err != nil {
		return false
	}
	return rel != ".." && !strings.HasPrefix(rel, ".."+string(filepath.Separator))
}

// copySkillFiles copies the source skill directory into dst, excluding
// the exe (client/build.ps1 moves it into the skill dir on CI builds)
// and any stale manifest from a previous install. Subdirectories (e.g.
// future references/) are copied recursively.
//
// Overlap guard: skip the bulk copy when the source tree is equal to or
// inside the destination (self-copy would truncate files while reading).
// A destination nested INSIDE the source is the normal release-zip case
// (exe + SKILL.md + references in one root, --target .\agents) and must
// keep copying — the per-entry check below skips only the entries that
// contain the destination, preventing recursive self-copy.
func copySkillFiles(srcDir, dstDir string) error {
	srcAbs, srcErr := filepath.Abs(srcDir)
	dstAbs, dstErr := filepath.Abs(dstDir)
	if srcErr != nil || dstErr != nil {
		return nil
	}
	if strings.EqualFold(srcAbs, dstAbs) || pathWithin(dstAbs, srcAbs) {
		return nil
	}

	entries, err := os.ReadDir(srcDir)
	if err != nil {
		return err
	}
	for _, entry := range entries {
		name := entry.Name()
		// SKILL.md is written separately (with the exe path rewritten),
		// the exe is skipped (client/build.ps1 moves it into the skill
		// dir on CI builds), and stale manifests from previous installs
		// are regenerated below.
		if name == skillFileName || strings.EqualFold(name, "revit-cli.exe") || name == skillManifestName {
			continue
		}
		src := filepath.Join(srcDir, name)
		// Never copy a source entry that contains the destination.
		entryAbs, err := filepath.Abs(src)
		if err == nil && pathWithin(entryAbs, dstAbs) {
			continue
		}
		dst := filepath.Join(dstDir, name)
		if entry.IsDir() {
			if err := os.MkdirAll(dst, 0o755); err != nil {
				return err
			}
			if err := copySkillFiles(src, dst); err != nil {
				return err
			}
			continue
		}
		if err := copyFile(src, dst); err != nil {
			return err
		}
	}
	return nil
}

// parseSelection parses interactive multi-select input against a list of
// n candidates. Accepted forms: "" (cancel), "all" (everything), and
// comma/space separated 1-based indices ("1,3"). Returns ok=false for
// malformed input or out-of-range indices so the caller can re-prompt.
func parseSelection(input string, n int) ([]int, bool) {
	input = strings.TrimSpace(strings.ToLower(input))
	if input == "" {
		return nil, true // cancel
	}
	if input == "all" {
		idx := make([]int, n)
		for i := range idx {
			idx[i] = i
		}
		return idx, true
	}
	var out []int
	seen := map[int]bool{}
	for _, part := range strings.FieldsFunc(input, func(r rune) bool { return r == ',' || r == ' ' }) {
		v, err := strconv.Atoi(part)
		if err != nil || v < 1 || v > n {
			return nil, false
		}
		if !seen[v-1] {
			seen[v-1] = true
			out = append(out, v-1)
		}
	}
	if len(out) == 0 {
		return nil, false
	}
	return out, true
}

// stdinIsTTY reports whether stdin is an interactive terminal, using the
// char-device check so piped/exec'd invocations (AI agents) take the
// non-interactive path.
func stdinIsTTY() bool {
	fi, err := os.Stdin.Stat()
	return err == nil && (fi.Mode()&os.ModeCharDevice) != 0
}
