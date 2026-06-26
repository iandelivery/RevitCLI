package builtin

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
	"text/tabwriter"

	"revit-cli/internal/abstractions"
	"revit-cli/internal/client/discovery"
	"revit-cli/internal/instance"
)

// ConfigureHandler is the parent handler for configure sub-commands.
// Usage: configure <setup|teardown|check|port> [options]
type ConfigureHandler struct{}

func (ConfigureHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "configure",
		Description: "Manage bridge installation and configuration",
		Usage:       "configure <setup|teardown|check|port> [options]",
		Category:    abstractions.CategorySystem,
		Examples: []string{
			"revit-cli.exe configure setup",
			"revit-cli.exe configure check",
			"revit-cli.exe configure teardown",
			"revit-cli.exe configure port",
		},
	}
}

func (ConfigureHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	if len(args) == 0 {
		fmt.Fprintln(os.Stderr, "Error: sub-command required (setup, teardown, check, port)")
		return 1
	}

	switch args[0] {
	case "setup":
		return configureSetup(args[1:])
	case "teardown":
		return configureTeardown(args[1:])
	case "check":
		return configureCheck(args[1:])
	case "port":
		return configurePort(args[1:])
	default:
		fmt.Fprintf(os.Stderr, "Unknown configure sub-command: %s\n", args[0])
		fmt.Fprintln(os.Stderr, "Available: setup, teardown, check, port")
		return 1
	}
}

// configureSetup installs the bridge add-in for all detected Revit versions.
func configureSetup(args []string) int {
	if runtime.GOOS != "windows" {
		fmt.Fprintln(os.Stderr, "configure setup is only supported on Windows (requires access to Revit add-ins directory).")
		fmt.Fprintln(os.Stderr, "On this platform, you can configure client-side settings only.")
		return 1
	}

	fmt.Println("Scanning for Revit installations...")
	installations := detectRevitInstallations()
	if len(installations) == 0 {
		fmt.Fprintln(os.Stderr, "No Revit installations found.")
		fmt.Fprintln(os.Stderr, "Searched HKLM\\SOFTWARE\\Autodesk\\Revit and %APPDATA%\\Autodesk\\Revit\\Addins.")
		return 1
	}

	// Ensure every detected installation has an AddinsDir derived from
	// APPDATA. The registry-detected entries only know the version, so
	// the per-user addins folder is computed here.
	appData := os.Getenv("APPDATA")
	if appData == "" {
		fmt.Fprintln(os.Stderr, "APPDATA environment variable not set.")
		return 1
	}
	addinsRoot := filepath.Join(appData, "Autodesk", "Revit", "Addins")
	for i := range installations {
		inst := &installations[i]
		if inst.AddinsDir == "" {
			inst.AddinsDir = filepath.Join(addinsRoot, fmt.Sprintf("%d", inst.Version))
		}
	}

	for _, inst := range installations {
		fmt.Printf("  [✓] Revit %d — %s\n", inst.Version, inst.AddinsDir)
	}

	fmt.Println("\nInstalling Revit CLI Bridge...")

	// Find the bridge distribution root.  For a bundled release this is
	// the "bridge" directory containing Revit20XX/ subfolders; for a flat
	// distribution it is the directory holding RevitCliBridge.dll.
	bridgeRoot := findBridgeFiles()
	if bridgeRoot == "" {
		fmt.Fprintln(os.Stderr, "Error: Cannot find bridge files (RevitCliBridge.dll, .addin).")
		fmt.Fprintln(os.Stderr, "Ensure the bridge distribution is in the same directory as revit-cli, or set BRIDGE_DIR.")
		return 1
	}

	skipped := 0
	for _, inst := range installations {
		// Resolve the version-specific bridge source directory.
		srcDir := findBridgeFilesForVersion(bridgeRoot, inst.Version)
		if srcDir == "" {
			fmt.Printf("  [✗] Revit %d: no bridge files for this version in distribution\n", inst.Version)
			fmt.Printf("         expected at %s\\Revit%d\\\n", bridgeRoot, inst.Version)
			skipped++
			continue
		}

		err := installBridgeForVersion(srcDir, inst)
		if err != nil {
			fmt.Printf("  [✗] Revit %d: %v\n", inst.Version, err)
		} else {
			fmt.Printf("  [✓] Revit %d — base port %d\n", inst.Version, portForVersion(inst.Version))
		}
	}

	if skipped > 0 && skipped == len(installations) {
		fmt.Fprintln(os.Stderr, "\nNo versions could be installed. The bridge distribution must contain Revit<year>/ subfolders matching the installed Revit versions.")
		return 1
	}

	fmt.Println("\nVerifying...")
	for _, inst := range installations {
		addinPath := filepath.Join(inst.AddinsDir, "RevitCliBridge.addin")
		if fileExists(addinPath) {
			fmt.Printf("  [✓] Revit %d — add-in registered\n", inst.Version)
		} else {
			fmt.Printf("  [✗] Revit %d — add-in NOT found\n", inst.Version)
		}
	}

	fmt.Println("\nDone! Restart Revit to activate the bridge.")
	return 0
}

// configureTeardown removes the bridge add-in from all Revit versions.
func configureTeardown(args []string) int {
	if runtime.GOOS != "windows" {
		fmt.Fprintln(os.Stderr, "configure teardown is only supported on Windows.")
		return 1
	}

	fmt.Println("Scanning for Revit installations...")
	installations := detectRevitInstallations()

	for _, inst := range installations {
		addinPath := filepath.Join(inst.AddinsDir, "RevitCliBridge.addin")
		bridgeDir := filepath.Join(inst.AddinsDir, "RevitCliBridge")

		removed := false
		if fileExists(addinPath) {
			os.Remove(addinPath)
			fmt.Printf("  [✓] Removed %s\n", addinPath)
			removed = true
		}
		if dirExists(bridgeDir) {
			os.RemoveAll(bridgeDir)
			fmt.Printf("  [✓] Removed %s\n", bridgeDir)
			removed = true
		}
		if !removed {
			fmt.Printf("  [-] Revit %d — no bridge installation found\n", inst.Version)
		}
	}

	fmt.Println("\nTeardown complete.")
	return 0
}

// configureCheck verifies the health of the bridge installation.
func configureCheck(args []string) int {
	fmt.Println("Checking Revit CLI Bridge installation...")
	fmt.Println()

	// 1. Check running instances.
	instances := instance.Discover()
	if len(instances) > 0 {
		fmt.Printf("Running instances (%d):\n", len(instances))
		instance.PrintInstances(instances)
	} else {
		fmt.Println("Running instances: none")
	}
	fmt.Println()

	// 2. Check installed add-ins (Windows only).
	if runtime.GOOS == "windows" {
		installations := detectRevitInstallations()
		if len(installations) > 0 {
			fmt.Printf("Installed add-ins (%d):\n", len(installations))
			for _, inst := range installations {
				addinPath := filepath.Join(inst.AddinsDir, "RevitCliBridge.addin")
				bridgeDir := filepath.Join(inst.AddinsDir, "RevitCliBridge")
				status := "not installed"
				if fileExists(addinPath) && dirExists(bridgeDir) {
					status = "installed"
				}
				fmt.Printf("  Revit %d: %s\n", inst.Version, status)
			}
		} else {
			fmt.Println("Installed add-ins: none found")
		}
		fmt.Println()
	}

	// 3. Check connectivity to running instances.
	if len(instances) > 0 {
		fmt.Println("Connectivity:")
		for _, inst := range instances {
			url := fmt.Sprintf("http://localhost:%d/api/health", inst.Port)
			resp, err := http.Get(url)
			if err != nil {
				fmt.Printf("  [✗] Revit %d  PID %d  port %d: unreachable\n", inst.Version, inst.Pid, inst.Port)
				continue
			}
			resp.Body.Close()
			doc := inst.Document
			if doc == "" {
				doc = "(no document)"
			}
			fmt.Printf("  [✓] Revit %d  PID %d  port %d: healthy  %s\n", inst.Version, inst.Pid, inst.Port, doc)
		}
		fmt.Println()
	}

	// 4. Print the resolved data directories.
	fmt.Printf("Data directory:        %s\n", discovery.DataDir())
	fmt.Printf("Schema cache:          %s\n", discovery.CacheDir())
	fmt.Printf("Instance registry:     %s\n", discovery.InstancesDir())

	return 0
}

// configurePort shows port assignments for detected Revit versions.
func configurePort(args []string) int {
	if abstractions.HasFlag(args, "--json") {
		instances := instance.Discover()
		type portInfo struct {
			Version int `json:"version"`
			BasePort int `json:"base_port"`
			Port     int `json:"port,omitempty"`
			Pid      int `json:"pid,omitempty"`
		}
		var infos []portInfo
		for v := 2019; v <= 2029; v++ {
			info := portInfo{Version: v, BasePort: portForVersion(v)}
			for _, inst := range instances {
				if inst.Version == v {
					info.Port = inst.Port
					info.Pid = inst.Pid
				}
			}
			infos = append(infos, info)
		}
		b, _ := json.MarshalIndent(infos, "", "  ")
		fmt.Println(string(b))
		return 0
	}

	w := tabwriter.NewWriter(os.Stdout, 0, 0, 2, ' ', 0)
	fmt.Fprintln(w, "VERSION\tBASE PORT\tACTUAL PORT\tPID")

	instances := instance.Discover()
	for v := 2019; v <= 2029; v++ {
		base := portForVersion(v)
		actual := "-"
		pid := "-"
		for _, inst := range instances {
			if inst.Version == v {
				actual = fmt.Sprintf("%d", inst.Port)
				pid = fmt.Sprintf("%d", inst.Pid)
			}
		}
		fmt.Fprintf(w, "%d\t%d\t%s\t%s\n", v, base, actual, pid)
	}
	w.Flush()
	return 0
}

// --- Helper types and functions ---

type revitInstallation struct {
	Version     int
	InstallPath string
	AddinsDir   string
}

// detectRevitInstallations scans the Windows registry and the addins
// directory for installed Revit versions.  Returns installations sorted
// by version.  Only versions in the supported range (2019+) are returned.
func detectRevitInstallations() []revitInstallation {
	if runtime.GOOS != "windows" {
		return nil
	}

	var installations []revitInstallation
	seen := map[int]bool{}

	// 1. Primary source: Windows registry (HKLM\SOFTWARE\Autodesk\Revit\<version>)
	//    This is the authoritative list of installed Revit products.
	regPaths := []string{
		`HKLM\SOFTWARE\Autodesk\Revit`,
		`HKLM\SOFTWARE\WOW6432Node\Autodesk\Revit`,
	}
	for _, regPath := range regPaths {
		collectRevitVersionsFromRegistry(regPath, &installations, seen)
	}

	// 2. Fallback: %APPDATA%\Autodesk\Revit\Addins\<version>
	//    This catches installations where the registry hive is inaccessible
	//    (e.g. limited user perms) but the addins directory exists.
	appData := os.Getenv("APPDATA")
	if appData != "" {
		revitAddinsRoot := filepath.Join(appData, "Autodesk", "Revit", "Addins")
		entries, err := os.ReadDir(revitAddinsRoot)
		if err == nil {
			for _, entry := range entries {
				if !entry.IsDir() {
					continue
				}
				v := 0
				fmt.Sscanf(entry.Name(), "%d", &v)
				if v >= 2019 && v <= 2099 && !seen[v] {
					seen[v] = true
					installations = append(installations, revitInstallation{
						Version:   v,
						AddinsDir: filepath.Join(revitAddinsRoot, entry.Name()),
					})
				}
			}
		}
	}

	sort.Slice(installations, func(i, j int) bool {
		return installations[i].Version < installations[j].Version
	})

	return installations
}

// collectRevitVersionsFromRegistry enumerates subkeys of a Revit registry
// path and appends any version-numbered entries to the installations list.
// Requires Windows; the caller's GOOS check guards the entry point.
func collectRevitVersionsFromRegistry(regPath string, installations *[]revitInstallation, seen map[int]bool) {
	cmd := exec.Command("reg", "query", regPath)
	output, err := cmd.Output()
	if err != nil {
		return
	}

	scanner := bufio.NewScanner(bytes.NewReader(output))
	for scanner.Scan() {
		// Each registry subkey is printed on its own line as "<full path>".
		// The last path segment is the version number (e.g. "2022").
		line := strings.TrimSpace(scanner.Text())
		if line == "" {
			continue
		}
		parts := strings.Split(line, `\`)
		name := parts[len(parts)-1]
		v := 0
		fmt.Sscanf(name, "%d", &v)
		if v >= 2019 && v <= 2099 && !seen[v] {
			seen[v] = true
			*installations = append(*installations, revitInstallation{
				Version: v,
			})
		}
	}
}

// portForVersion returns the base port for a given Revit version.
func portForVersion(version int) int {
	return 5000 + (version-2018)*10 + 1
}

// findBridgeFiles locates the bridge distribution root.  It supports two
// layouts:
//
//   1. Flat layout — revit-cli.exe sits next to RevitCliBridge.dll
//   2. Bundled layout — revit-cli.exe sits next to a "bridge" directory
//      that contains version-specific subfolders named "Revit<year>"
//      (e.g. bridge/Revit2022/RevitCliBridge.dll)
//
// The bundled layout is what release zip packages produce.
func findBridgeFiles() string {
	// Check BRIDGE_DIR env var first — points directly to a flat layout.
	if dir := os.Getenv("BRIDGE_DIR"); dir != "" && fileExists(filepath.Join(dir, "RevitCliBridge.dll")) {
		return dir
	}

	exePath, err := os.Executable()
	if err != nil {
		return ""
	}
	exeDir := filepath.Dir(exePath)

	// Flat layout: bridge DLL next to revit-cli.exe.
	if fileExists(filepath.Join(exeDir, "RevitCliBridge.dll")) {
		return exeDir
	}

	// Bundled layout: bridge/<something>/RevitCliBridge.dll.
	// We return the bridge root (parent of the version subdirs) so callers
	// can resolve version-specific subdirs themselves via findBridgeFilesForVersion.
	bridgeRoot := filepath.Join(exeDir, "bridge")
	if entries, err := os.ReadDir(bridgeRoot); err == nil {
		for _, entry := range entries {
			if !entry.IsDir() {
				continue
			}
			candidate := filepath.Join(bridgeRoot, entry.Name(), "RevitCliBridge.dll")
			if fileExists(candidate) {
				return bridgeRoot
			}
		}
	}

	return ""
}

// findBridgeFilesForVersion returns the directory containing bridge files
// for a specific Revit version.  Used together with findBridgeFiles() to
// resolve the bundled layout.  For a flat layout, the same directory is
// returned for every version (acceptable for single-version distributions).
func findBridgeFilesForVersion(root string, version int) string {
	// Bundled layout: <root>/Revit<year>/
	versioned := filepath.Join(root, fmt.Sprintf("Revit%d", version))
	if fileExists(filepath.Join(versioned, "RevitCliBridge.dll")) {
		return versioned
	}

	// Flat layout: DLLs are directly in the root.
	if fileExists(filepath.Join(root, "RevitCliBridge.dll")) {
		return root
	}

	return ""
}

// installBridgeForVersion copies bridge files to the Revit addins directory.
func installBridgeForVersion(bridgeDir string, inst revitInstallation) error {
	targetAddinDir := filepath.Join(inst.AddinsDir, "RevitCliBridge")
	if err := os.MkdirAll(targetAddinDir, 0o755); err != nil {
		return fmt.Errorf("cannot create directory %s: %w", targetAddinDir, err)
	}

	// Copy DLL files.
	dlls := []string{"RevitCliBridge.dll", "RevitCliBridge.Abstractions.dll"}
	for _, dll := range dlls {
		src := filepath.Join(bridgeDir, dll)
		dst := filepath.Join(targetAddinDir, dll)
		if fileExists(src) {
			if err := copyFile(src, dst); err != nil {
				return fmt.Errorf("cannot copy %s: %w", dll, err)
			}
		}
	}

	// Copy .addin manifest.
	addinSrc := filepath.Join(bridgeDir, "RevitCliBridge.addin")
	addinDst := filepath.Join(inst.AddinsDir, "RevitCliBridge.addin")
	if fileExists(addinSrc) {
		if err := copyFile(addinSrc, addinDst); err != nil {
			return fmt.Errorf("cannot copy .addin: %w", err)
		}
	}

	// Write version-specific config with auto_port.
	configDir := filepath.Join(targetAddinDir, ".config")
	if err := os.MkdirAll(configDir, 0o755); err != nil {
		return fmt.Errorf("cannot create config dir: %w", err)
	}

	config := map[string]interface{}{
		"enabled":               true,
		"port":                  portForVersion(inst.Version),
		"auto_port":             true,
		"timeout_seconds":       180,
		"max_command_queue_size": 100,
		"allow_raw_execution":   false,
	}
	configData, _ := json.MarshalIndent(config, "", "  ")
	configPath := filepath.Join(configDir, "cli_bridge_setting.json")
	if err := os.WriteFile(configPath, configData, 0o644); err != nil {
		return fmt.Errorf("cannot write config: %w", err)
	}

	return nil
}

func copyFile(src, dst string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()

	out, err := os.Create(dst)
	if err != nil {
		return err
	}
	defer out.Close()

	_, err = io.Copy(out, in)
	return err
}

func fileExists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}

func dirExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && info.IsDir()
}
