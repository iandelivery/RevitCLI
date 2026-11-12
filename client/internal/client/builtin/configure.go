package builtin

import (
	"bufio"
	"bytes"
	"context"
	"crypto/rand"
	"encoding/base64"
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
	"revit-cli/internal/client/auth"
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
	sub, rest := abstractions.FirstPositional(args)
	if sub == "" {
		fmt.Fprintln(os.Stderr, "Error: sub-command required (setup, teardown, check, port)")
		return 1
	}

	switch sub {
	case "setup":
		return configureSetup(rest)
	case "teardown":
		return configureTeardown(rest)
	case "check":
		return configureCheck(rest)
	case "port":
		return configurePort(rest)
	default:
		fmt.Fprintf(os.Stderr, "Unknown configure sub-command: %s\n", sub)
		fmt.Fprintln(os.Stderr, "Available: setup, teardown, check, port")
		return 1
	}
}

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

	bridgeRoot := findBridgeFiles()
	if bridgeRoot == "" {
		fmt.Fprintln(os.Stderr, "Error: Cannot find bridge files (RevitCliBridge.dll, .addin).")
		fmt.Fprintln(os.Stderr, "Ensure the bridge distribution is in the same directory as revit-cli, or set BRIDGE_DIR.")
		return 1
	}

	skipped := 0
	for _, inst := range installations {
		srcDir := findBridgeFilesForVersion(bridgeRoot, inst.Version)
		if srcDir == "" {
			fmt.Printf("  [✗] Revit %d: no bridge files for this version in distribution\n", inst.Version)
			fmt.Printf("         expected at %s\\Revit%d\\\n", bridgeRoot, inst.Version)
			skipped++
			continue
		}

		apiKey, err := installBridgeForVersion(srcDir, inst)
		if err != nil {
			fmt.Printf("  [✗] Revit %d: %v\n", inst.Version, err)
			continue
		}

		baseURL := fmt.Sprintf("http://localhost:%d", portForVersion(inst.Version))
		if syncErr := auth.SetAPIKey(baseURL, apiKey); syncErr != nil {
			fmt.Printf("  [!] Revit %d: installed but failed to sync API key: %v\n", inst.Version, syncErr)
		}
		fmt.Printf("  [✓] Revit %d — base port %d (API key synced)\n", inst.Version, portForVersion(inst.Version))
	}

	if skipped > 0 && skipped == len(installations) {
		fmt.Fprintln(os.Stderr, "\nNo versions could be installed. The bridge distribution must contain Revit<year>/ subfolders matching the installed Revit versions.")
		return 1
	}

	fmt.Println("\nVerifying...")
	for _, inst := range installations {
		if addinManifestExists(inst.AddinsDir) {
			fmt.Printf("  [✓] Revit %d — add-in registered\n", inst.Version)
		} else {
			fmt.Printf("  [✗] Revit %d — add-in NOT found\n", inst.Version)
		}
	}

	fmt.Println("\nDone! Restart Revit to activate the bridge.")
	fmt.Println("Using an AI agent? Run 'revit-cli.exe skill install' to register this tool with Claude Code / Cursor / Trae / CodeBuddy.")
	return 0
}

func configureTeardown(args []string) int {
	if runtime.GOOS != "windows" {
		fmt.Fprintln(os.Stderr, "configure teardown is only supported on Windows.")
		return 1
	}

	fmt.Println("Scanning for Revit installations...")
	installations := detectRevitInstallations()

	for _, inst := range installations {
		bridgeDir := filepath.Join(inst.AddinsDir, "RevitCliBridge")

		removed := false
		for _, name := range bridgeAddinManifests {
			addinPath := filepath.Join(inst.AddinsDir, name)
			if fileExists(addinPath) {
				os.Remove(addinPath)
				fmt.Printf("  [✓] Removed %s\n", addinPath)
				removed = true
			}
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

func configureCheck(args []string) int {
	fmt.Println("Checking Revit CLI Bridge installation...")
	fmt.Println()

	instances := instance.Discover()
	if len(instances) > 0 {
		fmt.Printf("Running instances (%d):\n", len(instances))
		instance.PrintInstances(instances)
	} else {
		fmt.Println("Running instances: none")
	}
	fmt.Println()

	if runtime.GOOS == "windows" {
		installations := detectRevitInstallations()
		if len(installations) > 0 {
			fmt.Printf("Installed add-ins (%d):\n", len(installations))
			for _, inst := range installations {
				bridgeDir := filepath.Join(inst.AddinsDir, "RevitCliBridge")
				status := "not installed"
				if addinManifestExists(inst.AddinsDir) && dirExists(bridgeDir) {
					status = "installed"
				}
				fmt.Printf("  Revit %d: %s\n", inst.Version, status)
			}
		} else {
			fmt.Println("Installed add-ins: none found")
		}
		fmt.Println()
	}

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

	fmt.Printf("Data directory:        %s\n", discovery.DataDir())
	fmt.Printf("Schema cache:          %s\n", discovery.CacheDir())
	fmt.Printf("Instance registry:     %s\n", discovery.InstancesDir())

	return 0
}

func configurePort(args []string) int {
	if abstractions.HasFlag(args, "--json") {
		instances := instance.Discover()
		type portInfo struct {
			Version  int `json:"version"`
			BasePort int `json:"base_port"`
			Port     int `json:"port,omitempty"`
			Pid      int `json:"pid,omitempty"`
		}
		var infos []portInfo
		for _, v := range supportedRevitVersions {
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
	for _, v := range supportedRevitVersions {
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

var supportedRevitVersions = []int{2019, 2020, 2021, 2022}

func isSupportedVersion(v int) bool {
	for _, sv := range supportedRevitVersions {
		if sv == v {
			return true
		}
	}
	return false
}

type revitInstallation struct {
	Version     int
	InstallPath string
	AddinsDir   string
}

func detectRevitInstallations() []revitInstallation {
	if runtime.GOOS != "windows" {
		return nil
	}

	var installations []revitInstallation
	seen := map[int]bool{}

	regPaths := []string{
		`HKLM\SOFTWARE\Autodesk\Revit`,
		`HKLM\SOFTWARE\WOW6432Node\Autodesk\Revit`,
	}
	for _, regPath := range regPaths {
		collectRevitVersionsFromRegistry(regPath, &installations, seen)
	}

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
				if isSupportedVersion(v) && !seen[v] {
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

func collectRevitVersionsFromRegistry(regPath string, installations *[]revitInstallation, seen map[int]bool) {
	cmd := exec.Command("reg", "query", regPath)
	output, err := cmd.Output()
	if err != nil {
		return
	}

	scanner := bufio.NewScanner(bytes.NewReader(output))
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" {
			continue
		}
		parts := strings.Split(line, `\`)
		name := parts[len(parts)-1]
		v := 0
		fmt.Sscanf(name, "%d", &v)
		if isSupportedVersion(v) && !seen[v] {
			seen[v] = true
			*installations = append(*installations, revitInstallation{
				Version: v,
			})
		}
	}
}

func portForVersion(version int) int {
	return 5000 + (version-2018)*10 + 1
}

func findBridgeFiles() string {
	if dir := os.Getenv("BRIDGE_DIR"); dir != "" && fileExists(filepath.Join(dir, "RevitCliBridge.dll")) {
		return dir
	}

	exePath, err := os.Executable()
	if err != nil {
		return ""
	}
	exeDir := filepath.Dir(exePath)

	if fileExists(filepath.Join(exeDir, "RevitCliBridge.dll")) {
		return exeDir
	}

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

func findBridgeFilesForVersion(root string, version int) string {
	versioned := filepath.Join(root, fmt.Sprintf("Revit%d", version))
	if fileExists(filepath.Join(versioned, "RevitCliBridge.dll")) {
		return versioned
	}

	if fileExists(filepath.Join(root, "RevitCliBridge.dll")) {
		return root
	}

	return ""
}

const bridgeAddinManifest = "@RevitCliBridge.addin"

var bridgeAddinManifests = []string{bridgeAddinManifest, "RevitCliBridge.addin"}

func addinManifestExists(addinsDir string) bool {
	for _, name := range bridgeAddinManifests {
		if fileExists(filepath.Join(addinsDir, name)) {
			return true
		}
	}
	return false
}

func installBridgeForVersion(bridgeDir string, inst revitInstallation) (string, error) {
	targetAddinDir := filepath.Join(inst.AddinsDir, "RevitCliBridge")
	if err := os.MkdirAll(targetAddinDir, 0o755); err != nil {
		return "", fmt.Errorf("cannot create directory %s: %w", targetAddinDir, err)
	}

	entries, err := os.ReadDir(bridgeDir)
	if err != nil {
		return "", fmt.Errorf("cannot read bridge source %s: %w", bridgeDir, err)
	}
	for _, entry := range entries {
		if entry.IsDir() {
			continue
		}
		name := entry.Name()
		if !strings.EqualFold(filepath.Ext(name), ".dll") {
			continue
		}
		src := filepath.Join(bridgeDir, name)
		dst := filepath.Join(targetAddinDir, name)
		if err := copyFile(src, dst); err != nil {
			return "", fmt.Errorf("cannot copy %s: %w", name, err)
		}
	}

	addinDst := filepath.Join(inst.AddinsDir, bridgeAddinManifest)
	addinSrc := ""
	for _, name := range bridgeAddinManifests {
		candidate := filepath.Join(bridgeDir, name)
		if fileExists(candidate) {
			addinSrc = candidate
			break
		}
	}
	if addinSrc != "" {
		if err := copyFile(addinSrc, addinDst); err != nil {
			return "", fmt.Errorf("cannot copy .addin: %w", err)
		}
	}
	if legacy := filepath.Join(inst.AddinsDir, "RevitCliBridge.addin"); fileExists(legacy) {
		os.Remove(legacy)
	}

	configDir := filepath.Join(targetAddinDir, ".config")
	if err := os.MkdirAll(configDir, 0o755); err != nil {
		return "", fmt.Errorf("cannot create config dir: %w", err)
	}

	configPath := filepath.Join(configDir, "cli_bridge_setting.json")

	existingAPIKey := ""
	if existingData, readErr := os.ReadFile(configPath); readErr == nil {
		var existing map[string]interface{}
		if jsonErr := json.Unmarshal(existingData, &existing); jsonErr == nil {
			if k, ok := existing["api_key"].(string); ok && k != "" {
				existingAPIKey = k
			}
		}
	}

	apiKey := existingAPIKey
	if apiKey == "" {
		generated, err := generateAPIKey()
		if err != nil {
			return "", fmt.Errorf("cannot generate API key: %w", err)
		}
		apiKey = generated
	}

	config := map[string]interface{}{
		"enabled":                     true,
		"port":                        portForVersion(inst.Version),
		"auto_port":                   true,
		"timeout_seconds":             180,
		"max_command_queue_size":      100,
		"max_request_body_size_bytes": 10 * 1024 * 1024,
		"allow_raw_execution":         false,
		"api_key":                     apiKey,
	}
	configData, _ := json.MarshalIndent(config, "", "  ")
	if err := os.WriteFile(configPath, configData, 0o644); err != nil {
		return "", fmt.Errorf("cannot write config: %w", err)
	}

	return apiKey, nil
}

func generateAPIKey() (string, error) {
	b := make([]byte, 32)
	if _, err := rand.Read(b); err != nil {
		return "", err
	}
	s := base64.RawURLEncoding.EncodeToString(b)
	return s, nil
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
