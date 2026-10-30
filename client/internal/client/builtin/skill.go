package builtin

import (
	"bufio"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"time"

	"revit-cli/internal/abstractions"
)

// SkillHandler manages the revit-cli AI agent skill: installing SKILL.md
// (with the exe path rewritten to an absolute path) into the skills
// directories of known AI coding agents, and maintaining those installs.
// Usage: skill <install|show|update|uninstall> [options]
type SkillHandler struct{}

func (SkillHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "skill",
		Description: "Install and manage the revit-cli AI agent skill (SKILL.md)",
		Usage:       "skill <install|show|update|uninstall> [options]",
		Category:    abstractions.CategorySystem,
		Examples: []string{
			"revit-cli.exe skill install --list",
			"revit-cli.exe skill install --agent claude,codebuddy",
			"revit-cli.exe skill install --agent claude --global",
			"revit-cli.exe skill install --target D:\\skills",
			"revit-cli.exe skill update",
			"revit-cli.exe skill uninstall --all",
		},
	}
}

func (SkillHandler) Handle(_ context.Context, args []string, _ abstractions.SendCommandFunc) int {
	if len(args) == 0 {
		fmt.Fprintln(os.Stderr, "Error: sub-command required (install, show, update, uninstall)")
		printSkillUsage()
		return 1
	}

	switch args[0] {
	case "install":
		return skillInstall(args[1:])
	case "show":
		return skillShow(args[1:])
	case "update":
		return skillUpdate(args[1:])
	case "uninstall":
		return skillUninstall(args[1:])
	default:
		fmt.Fprintf(os.Stderr, "Unknown skill sub-command: %s\n", args[0])
		fmt.Fprintln(os.Stderr, "Available: install, show, update, uninstall")
		return 1
	}
}

// printSkillUsage prints the flag reference. Agents read this to invoke
// correctly without external docs.
func printSkillUsage() {
	fmt.Fprintln(os.Stderr, `
Usage: revit-cli.exe skill <install|show|update|uninstall> [options]

Options (install / uninstall):
  --agent <ids>   comma-separated agent ids: `+harnessIDs()+`
  --all           all detected agent directories (add --global for user-level)
  --target <dir>  custom skills root; installs into <dir>/revit-cli/
  --global        operate on user-level skill dirs (~) instead of project (cwd)
  --force         overwrite an existing installation
  --list          only list candidate targets, do not install
  --json          machine-readable output
  --all (uninstall)  remove every installed location

Examples:
  revit-cli.exe skill install --list
  revit-cli.exe skill install --agent claude,codebuddy
  revit-cli.exe skill install --agent claude --global
  revit-cli.exe skill update
  revit-cli.exe skill uninstall --all`)
}

// skillOptions holds the shared flag set of skill sub-commands.
type skillOptions struct {
	agents string
	target string
	global bool
	all    bool
	force  bool
	list   bool
	json   bool
}

func parseSkillOptions(args []string) skillOptions {
	agents, _ := abstractions.FindArg(args, "--agent")
	target, _ := abstractions.FindArg(args, "--target")
	return skillOptions{
		agents: agents,
		target: target,
		global: abstractions.HasFlag(args, "--global"),
		all:    abstractions.HasFlag(args, "--all"),
		force:  abstractions.HasFlag(args, "--force"),
		list:   abstractions.HasFlag(args, "--list"),
		json:   abstractions.HasFlag(args, "--json"),
	}
}

// skillTarget is one resolved installation target with its outcome.
type skillTarget struct {
	Agent  string `json:"agent"`
	Scope  string `json:"scope"`
	Dir    string `json:"dir"`
	Status string `json:"status"` // installed | already-installed | skipped | removed | failed
	Error  string `json:"error,omitempty"`
}

// loadSkillSource resolves and rewrites the source SKILL.md. Shared by
// install and update.
func loadSkillSource() (srcDir, exePath, rewritten string, ok bool) {
	srcDir = findSkillSourceDir()
	if srcDir == "" {
		fmt.Fprintln(os.Stderr, "Error: cannot find SKILL.md.")
		fmt.Fprintln(os.Stderr, "Expected next to revit-cli.exe, under <exe dir>\\skills\\revit-cli\\, or set REVIT_CLI_SKILL_DIR.")
		return "", "", "", false
	}
	exePath, err := os.Executable()
	if err != nil || exePath == "" {
		exePath = "revit-cli.exe"
	}
	content, err := os.ReadFile(filepath.Join(srcDir, skillFileName))
	if err != nil {
		fmt.Fprintf(os.Stderr, "Error: cannot read %s: %v\n", filepath.Join(srcDir, skillFileName), err)
		return "", "", "", false
	}
	rewritten, anchored := rewriteSkillContent(string(content), exePath)
	if !anchored {
		fmt.Println("  [WARN] SKILL.md lacks the exe path anchor; appended a Local Binary Path section.")
	}
	return srcDir, exePath, rewritten, true
}

// installSkillDir writes the rewritten SKILL.md, supporting files, and a
// fresh manifest into one target directory and records it in the index.
func installSkillDir(srcDir, rewritten, exePath string, c skillCandidate) error {
	if err := os.MkdirAll(c.Dir, 0o755); err != nil {
		return err
	}
	if err := copySkillFiles(srcDir, c.Dir); err != nil {
		return err
	}
	if err := os.WriteFile(filepath.Join(c.Dir, skillFileName), []byte(rewritten), 0o644); err != nil {
		return err
	}
	m := &SkillManifest{
		SkillName:   skillDirName,
		SourceExe:   exePath,
		Version:     ClientVersion,
		InstalledAt: time.Now().UTC().Format(time.RFC3339),
	}
	if err := writeSkillManifest(c.Dir, m); err != nil {
		return err
	}
	upsertSkillIndex(skillInstallRecord{Agent: c.Agent, Scope: c.Scope, Dir: c.Dir, Manifest: *m})
	return nil
}

// skillCandidateStatus renders the detected/installed state of one
// candidate for listings.
func skillCandidateStatus(c skillCandidate) string {
	switch {
	case c.Manifest != nil:
		return fmt.Sprintf("installed v%s", c.Manifest.Version)
	case c.Detected:
		return "detected"
	default:
		return "not detected"
	}
}

func printSkillCandidates(w io.Writer, cands []skillCandidate) {
	for _, c := range cands {
		fmt.Fprintf(w, "  %-10s %-7s %-16s %s\n", c.Agent, c.Scope, skillCandidateStatus(c), c.Dir)
	}
}

// promptSkillSelection shows a numbered candidate list and reads a
// multi-select answer from stdin ("1,3", "all", Enter to cancel).
// Re-prompts on invalid input; EOF cancels.
func promptSkillSelection(cands []skillCandidate, title string) []skillCandidate {
	fmt.Println(title)
	for i, c := range cands {
		extra := ""
		if c.Manifest != nil {
			extra = fmt.Sprintf("  (installed v%s)", c.Manifest.Version)
		}
		fmt.Printf("  %d) %-10s %s%s\n", i+1, c.Agent, c.Dir, extra)
	}
	fmt.Print("Select (e.g. 1,3 | all | Enter to cancel): ")

	reader := bufio.NewReader(os.Stdin)
	for {
		line, err := reader.ReadString('\n')
		if err != nil && line == "" {
			return nil // EOF -> cancel
		}
		idx, ok := parseSelection(line, len(cands))
		if !ok {
			fmt.Print("Invalid selection. Try again (e.g. 1,3 | all | Enter to cancel): ")
			continue
		}
		var out []skillCandidate
		for _, i := range idx {
			out = append(out, cands[i])
		}
		return out
	}
}

// skillInstall installs the skill into agent skill directories.
func skillInstall(args []string) int {
	opts := parseSkillOptions(args)
	explicit := opts.agents != "" || opts.target != "" || opts.all

	// Resolve candidates according to the explicit flags.
	var candidates []skillCandidate
	switch {
	case opts.target != "":
		abs, err := filepath.Abs(opts.target)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Error: cannot resolve --target %s: %v\n", opts.target, err)
			return 1
		}
		candidates = []skillCandidate{{
			Agent: "custom", Scope: skillScopeCustom,
			Dir: filepath.Join(abs, skillDirName), Detected: true,
		}}
		if m := readSkillManifest(candidates[0].Dir); m != nil {
			candidates[0].Manifest = m
		}

	case opts.agents != "":
		harnesses, err := findHarnesses(opts.agents)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Error: %v\n", err)
			return 1
		}
		base, err := skillBaseDir(opts.global)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Error: cannot resolve %s directory: %v\n", map[bool]string{true: "home", false: "current"}[opts.global], err)
			return 1
		}
		scope := skillScopeProject
		if opts.global {
			scope = skillScopeUser
		}
		for _, h := range harnesses {
			dir := filepath.Join(base, h.Dir, "skills", skillDirName)
			c := skillCandidate{Agent: h.ID, Scope: scope, Dir: dir, Detected: true}
			if m := readSkillManifest(dir); m != nil {
				c.Manifest = m
			}
			candidates = append(candidates, c)
		}

	default:
		cs, err := skillCandidates(opts.global)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Error: %v\n", err)
			return 1
		}
		candidates = cs
	}

	// --list: print and exit without installing.
	if opts.list {
		return printSkillList(opts, candidates)
	}

	// No explicit target: interactive multi-select on TTY, self-describing
	// error otherwise (agents retry with explicit flags).
	if !explicit {
		var detected []skillCandidate
		for _, c := range candidates {
			if c.Detected {
				detected = append(detected, c)
			}
		}
		if len(detected) == 0 {
			scopeName := "current directory"
			if opts.global {
				scopeName = "user home directory"
			}
			fmt.Fprintf(os.Stderr, "Error: no known agent directories found under the %s.\n", scopeName)
			fmt.Fprintf(os.Stderr, "Use --target <dir> for a custom skills root, or --agent <ids> (valid: %s).\n", harnessIDs())
			return 1
		}
		if !stdinIsTTY() {
			fmt.Fprintln(os.Stderr, "Error: no installation target selected (stdin is not interactive).")
			fmt.Fprintln(os.Stderr, "\nDetected candidates:")
			printSkillCandidates(os.Stderr, detected)
			fmt.Fprintln(os.Stderr, "\nRetry with an explicit target, e.g.:")
			fmt.Fprintf(os.Stderr, "  revit-cli.exe skill install --agent <id>[,<id>...]   valid: %s\n", harnessIDs())
			fmt.Fprintln(os.Stderr, "  revit-cli.exe skill install --all")
			fmt.Fprintln(os.Stderr, "  revit-cli.exe skill install --target <skills-root-dir>")
			return 1
		}
		selected := promptSkillSelection(detected, "Install revit-cli skill into:")
		if len(selected) == 0 {
			fmt.Println("Cancelled.")
			return 0
		}
		candidates = selected
	}

	// --all (with or without --global): everything detected.
	if opts.all {
		var keep []skillCandidate
		for _, c := range candidates {
			if c.Detected {
				keep = append(keep, c)
			}
		}
		if len(keep) == 0 {
			fmt.Fprintln(os.Stderr, "Error: --all found no detected agent directories. Use --agent or --target instead.")
			return 1
		}
		candidates = keep
	}

	srcDir, exePath, rewritten, ok := loadSkillSource()
	if !ok {
		return 1
	}

	if !opts.json {
		fmt.Printf("Skill source: %s (v%s)\n", srcDir, ClientVersion)
		fmt.Println("Installing...")
	}

	var targets []skillTarget
	exitCode := 0
	for _, c := range candidates {
		t := skillTarget{Agent: c.Agent, Scope: string(c.Scope), Dir: c.Dir}

		switch {
		case c.Manifest != nil && !opts.force:
			t.Status = "already-installed"
			if !opts.json {
				fmt.Printf("  [=] %-10s %s — already installed v%s (use --force to overwrite)\n", c.Agent, c.Dir, c.Manifest.Version)
			}
		case c.Manifest == nil && dirExists(c.Dir) && !isEmptyDir(c.Dir) && !opts.force:
			// Directory exists without our manifest: user-maintained
			// content. Never clobber it without an explicit --force.
			t.Status = "skipped"
			t.Error = "directory exists and is not managed by revit-cli (use --force to overwrite)"
			if !opts.json {
				fmt.Printf("  [✗] %-10s %s — %s\n", c.Agent, c.Dir, t.Error)
			}
			exitCode = 1
		default:
			if err := installSkillDir(srcDir, rewritten, exePath, c); err != nil {
				t.Status = "failed"
				t.Error = err.Error()
				exitCode = 1
				if !opts.json {
					fmt.Printf("  [✗] %-10s %s — %v\n", c.Agent, c.Dir, err)
				}
			} else {
				t.Status = "installed"
				if !opts.json {
					fmt.Printf("  [✓] %-10s %s\n", c.Agent, c.Dir)
				}
			}
		}
		targets = append(targets, t)
	}

	if opts.json {
		printSkillJSON(map[string]interface{}{
			"source":  srcDir,
			"exe":     exePath,
			"version": ClientVersion,
			"targets": targets,
		})
	} else if exitCode == 0 {
		fmt.Println("\nDone! Restart your AI agent to load the skill.")
	}
	return exitCode
}

// printSkillList implements --list: show candidates without installing.
func printSkillList(opts skillOptions, candidates []skillCandidate) int {
	if opts.json {
		printSkillJSON(map[string]interface{}{
			"version":    ClientVersion,
			"scope":      map[bool]string{true: "user", false: "project"}[opts.global],
			"candidates": candidates,
		})
		return 0
	}
	scopeName := "project (cwd)"
	if opts.global {
		scopeName = "user (home)"
	}
	fmt.Printf("revit-cli skill candidates — scope: %s (valid agent ids: %s)\n", scopeName, harnessIDs())
	printSkillCandidates(os.Stdout, candidates)
	fmt.Println("\nInstall with: revit-cli.exe skill install --agent <ids> | --all | --target <dir>")
	return 0
}

// skillShow reports the source skill and every candidate/installed state.
func skillShow(args []string) int {
	opts := parseSkillOptions(args)
	srcDir := findSkillSourceDir()
	exePath, _ := os.Executable()

	project, _ := skillCandidates(false)
	user, err := skillCandidates(true)
	if err != nil {
		user = nil
	}
	installed := findInstalledSkills()

	if opts.json {
		printSkillJSON(map[string]interface{}{
			"source":    srcDir,
			"exe":       exePath,
			"version":   ClientVersion,
			"project":   project,
			"user":      user,
			"installed": installed,
		})
		return 0
	}

	fmt.Println("revit-cli skill status")
	fmt.Printf("  Version:      %s\n", ClientVersion)
	fmt.Printf("  Executable:   %s\n", exePath)
	if srcDir != "" {
		fmt.Printf("  Source:       %s\n", srcDir)
	} else {
		fmt.Println("  Source:       not found (set REVIT_CLI_SKILL_DIR)")
	}

	fmt.Println("\nProject targets (cwd):")
	if len(project) == 0 {
		fmt.Println("  (none)")
	} else {
		printSkillCandidates(os.Stdout, project)
	}
	fmt.Println("\nUser targets (home):")
	if len(user) == 0 {
		fmt.Println("  (none)")
	} else {
		printSkillCandidates(os.Stdout, user)
	}

	fmt.Println("\nInstalled locations:")
	if len(installed) == 0 {
		fmt.Println("  (none — run 'revit-cli.exe skill install')")
	} else {
		for _, c := range installed {
			fmt.Printf("  [✓] %-10s v%s  %s\n", c.Agent, c.Manifest.Version, c.Dir)
		}
	}
	return 0
}

// skillUpdate re-installs the skill from the current executable into
// every location recorded by a manifest (batch, no interaction).
func skillUpdate(args []string) int {
	opts := parseSkillOptions(args)
	installed := findInstalledSkills()
	if len(installed) == 0 {
		fmt.Fprintln(os.Stderr, "No installed skill locations found.")
		fmt.Fprintln(os.Stderr, "Run 'revit-cli.exe skill install' first.")
		return 1
	}

	srcDir, exePath, rewritten, ok := loadSkillSource()
	if !ok {
		return 1
	}

	if !opts.json {
		fmt.Printf("Skill source: %s (v%s)\n", srcDir, ClientVersion)
		fmt.Println("Updating...")
	}

	var targets []skillTarget
	exitCode := 0
	for _, c := range installed {
		t := skillTarget{Agent: c.Agent, Scope: string(c.Scope), Dir: c.Dir}
		old := ""
		if c.Manifest != nil {
			old = c.Manifest.Version
		}
		if err := installSkillDir(srcDir, rewritten, exePath, c); err != nil {
			t.Status = "failed"
			t.Error = err.Error()
			exitCode = 1
			if !opts.json {
				fmt.Printf("  [✗] %-10s %s — %v\n", c.Agent, c.Dir, err)
			}
		} else {
			t.Status = "installed"
			if !opts.json {
				fmt.Printf("  [✓] %-10s %s  (v%s -> v%s)\n", c.Agent, c.Dir, old, ClientVersion)
			}
		}
		targets = append(targets, t)
	}

	if opts.json {
		printSkillJSON(map[string]interface{}{
			"source":  srcDir,
			"exe":     exePath,
			"version": ClientVersion,
			"targets": targets,
		})
	} else if exitCode == 0 {
		fmt.Println("\nDone! Restart your AI agent to reload the skill.")
	}
	return exitCode
}

// skillUninstall removes installed skill locations. Mirrors install's
// flags; only directories containing our manifest are ever deleted.
func skillUninstall(args []string) int {
	opts := parseSkillOptions(args)
	explicit := opts.agents != "" || opts.target != "" || opts.all

	var targets []skillCandidate
	switch {
	case opts.target != "":
		abs, err := filepath.Abs(opts.target)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Error: cannot resolve --target %s: %v\n", opts.target, err)
			return 1
		}
		c := skillCandidate{Agent: "custom", Scope: skillScopeCustom, Dir: filepath.Join(abs, skillDirName), Detected: true}
		c.Manifest = readSkillManifest(c.Dir)
		targets = []skillCandidate{c}

	case opts.agents != "":
		harnesses, err := findHarnesses(opts.agents)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Error: %v\n", err)
			return 1
		}
		base, err := skillBaseDir(opts.global)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Error: %v\n", err)
			return 1
		}
		scope := skillScopeProject
		if opts.global {
			scope = skillScopeUser
		}
		for _, h := range harnesses {
			dir := filepath.Join(base, h.Dir, "skills", skillDirName)
			c := skillCandidate{Agent: h.ID, Scope: scope, Dir: dir, Detected: true}
			c.Manifest = readSkillManifest(dir)
			targets = append(targets, c)
		}

	default:
		targets = findInstalledSkills()
		if len(targets) == 0 {
			fmt.Fprintln(os.Stderr, "No installed skill locations found (nothing to uninstall).")
			return 0
		}
		if !explicit && !stdinIsTTY() {
			fmt.Fprintln(os.Stderr, "Error: no uninstall target selected (stdin is not interactive).")
			fmt.Fprintln(os.Stderr, "\nInstalled locations:")
			printSkillCandidates(os.Stderr, targets)
			fmt.Fprintln(os.Stderr, "\nRetry with an explicit target, e.g.:")
			fmt.Fprintf(os.Stderr, "  revit-cli.exe skill uninstall --agent <id>[,<id>...]   valid: %s\n", harnessIDs())
			fmt.Fprintln(os.Stderr, "  revit-cli.exe skill uninstall --all")
			fmt.Fprintln(os.Stderr, "  revit-cli.exe skill uninstall --target <skills-root-dir>")
			return 1
		}
		if !explicit && stdinIsTTY() {
			selected := promptSkillSelection(targets, "Uninstall revit-cli skill from:")
			if len(selected) == 0 {
				fmt.Println("Cancelled.")
				return 0
			}
			targets = selected
		}
	}

	if !opts.json {
		fmt.Println("Uninstalling...")
	}
	var results []skillTarget
	exitCode := 0
	for _, c := range targets {
		t := skillTarget{Agent: c.Agent, Scope: string(c.Scope), Dir: c.Dir}
		if c.Manifest == nil {
			t.Status = "skipped"
			if dirExists(c.Dir) {
				t.Error = "not managed by revit-cli (no install manifest); left untouched"
			} else {
				t.Error = "not installed"
			}
			if !opts.json {
				fmt.Printf("  [-] %-10s %s — %s\n", c.Agent, c.Dir, t.Error)
			}
			exitCode = 1
			results = append(results, t)
			continue
		}
		if err := os.RemoveAll(c.Dir); err != nil {
			t.Status = "failed"
			t.Error = err.Error()
			exitCode = 1
			if !opts.json {
				fmt.Printf("  [✗] %-10s %s — %v\n", c.Agent, c.Dir, err)
			}
		} else {
			t.Status = "removed"
			removeSkillIndexDir(c.Dir)
			if !opts.json {
				fmt.Printf("  [✓] %-10s %s\n", c.Agent, c.Dir)
			}
		}
		results = append(results, t)
	}

	if opts.json {
		printSkillJSON(map[string]interface{}{"targets": results})
	} else if exitCode == 0 {
		fmt.Println("\nUninstall complete.")
	}
	return exitCode
}

// printSkillJSON emits the machine-readable output used by AI agents,
// following the project's MarshalIndent convention.
func printSkillJSON(v interface{}) {
	b, _ := json.MarshalIndent(v, "", "  ")
	fmt.Println(string(b))
}

// isEmptyDir reports whether dir contains no entries (missing dirs are
// treated as empty).
func isEmptyDir(dir string) bool {
	entries, err := os.ReadDir(dir)
	return err != nil || len(entries) == 0
}
