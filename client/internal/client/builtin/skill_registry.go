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

const (
	skillDirName      = "revit-cli"
	skillFileName     = "SKILL.md"
	skillManifestName = ".revit-cli-skill.json"
	skillEnvDir       = "REVIT_CLI_SKILL_DIR"
	skillIndexName    = "skill-installs.json"
)

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

func harnessIDs() string {
	ids := make([]string, len(harnessRegistry))
	for i, h := range harnessRegistry {
		ids[i] = h.ID
	}
	return strings.Join(ids, ", ")
}

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

func writeSkillManifest(dir string, m *SkillManifest) error {
	data, err := json.MarshalIndent(m, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(dir, skillManifestName), data, 0o644)
}

type skillScope string

const (
	skillScopeProject skillScope = "project"
	skillScopeUser    skillScope = "user"
	skillScopeCustom  skillScope = "custom"
)

type skillCandidate struct {
	Agent    string         `json:"agent"`
	Scope    skillScope     `json:"scope"`
	Dir      string         `json:"dir"`
	Detected bool           `json:"detected"`
	Manifest *SkillManifest `json:"manifest,omitempty"`
}

func skillBaseDir(global bool) (string, error) {
	if global {
		return os.UserHomeDir()
	}
	return os.Getwd()
}

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

type skillInstallRecord struct {
	Agent    string        `json:"agent"`
	Scope    skillScope    `json:"scope"`
	Dir      string        `json:"dir"`
	Manifest SkillManifest `json:"manifest"`
}

func skillIndexPath() string {
	return filepath.Join(discovery.DataDir(), skillIndexName)
}

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

func deregisterSkillDir(dir string) error {
	if err := os.Remove(filepath.Join(dir, skillManifestName)); err != nil && !os.IsNotExist(err) {
		return err
	}
	removeSkillIndexDir(dir)
	return nil
}

func findInstalledSkills() []skillCandidate {
	exePath, err := os.Executable()
	if err != nil {
		exePath = ""
	}
	return findInstalledSkillsIn(exePath)
}

func findInstalledSkillsIn(exePath string) []skillCandidate {
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
	if exePath != "" {
		if m := readSkillManifest(filepath.Dir(exePath)); m != nil {
			add(skillCandidate{Agent: "source", Scope: skillScopeCustom, Dir: filepath.Dir(exePath), Detected: true, Manifest: m})
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Dir < out[j].Dir })
	return out
}

func sameDir(a, b string) bool {
	aa, ea := filepath.Abs(a)
	bb, eb := filepath.Abs(b)
	return ea == nil && eb == nil && strings.EqualFold(aa, bb)
}

func pathWithin(dir, p string) bool {
	rel, err := filepath.Rel(dir, p)
	if err != nil {
		return false
	}
	return rel != ".." && !strings.HasPrefix(rel, ".."+string(filepath.Separator))
}

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
		if name == skillFileName || strings.EqualFold(name, "revit-cli.exe") || name == skillManifestName {
			continue
		}
		src := filepath.Join(srcDir, name)
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

func parseSelection(input string, n int) ([]int, bool) {
	input = strings.TrimSpace(strings.ToLower(input))
	if input == "" {
		return nil, true
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

func stdinIsTTY() bool {
	fi, err := os.Stdin.Stat()
	return err == nil && (fi.Mode()&os.ModeCharDevice) != 0
}
