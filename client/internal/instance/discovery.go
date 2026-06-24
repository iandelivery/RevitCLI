// Package instance provides discovery of running Revit bridge instances
// by reading registry files from the revit-cli data directory.
package instance

import (
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"sort"
	"strconv"
	"strings"

	"revit-cli/internal/client/discovery"
)

// InstanceInfo mirrors the C# InstanceRegistry.InstanceInfo struct.
type InstanceInfo struct {
	Pid           int    `json:"pid"`
	Version       int    `json:"version"`
	Port          int    `json:"port"`
	Document      string `json:"document,omitempty"`
	StartedAt     string `json:"started_at"`
	Hostname      string `json:"hostname"`
	CommandsCount int    `json:"commands_count"`
}

// InstancesDir returns the path to the instances registry directory.
// Uses the same cascading strategy as the schema cache.
func InstancesDir() string {
	return discovery.InstancesDir()
}

// Discover reads all instance registry files and returns the alive instances.
// Stale files (whose PID no longer exists) are cleaned up.
func Discover() []InstanceInfo {
	dir := InstancesDir()
	entries, err := os.ReadDir(dir)
	if err != nil {
		return nil
	}

	var instances []InstanceInfo
	for _, entry := range entries {
		if entry.IsDir() || filepath.Ext(entry.Name()) != ".json" {
			continue
		}
		// Only read files matching revit-*.json pattern.
		if !matchInstanceFile(entry.Name()) {
			continue
		}

		path := filepath.Join(dir, entry.Name())
		data, err := os.ReadFile(path)
		if err != nil {
			continue
		}

		var info InstanceInfo
		if err := json.Unmarshal(data, &info); err != nil {
			continue
		}

		if !isProcessAlive(info.Pid) {
			// Clean up stale registry file.
			os.Remove(path)
			continue
		}

		instances = append(instances, info)
	}

	// Sort by version (desc), then by PID.
	sort.Slice(instances, func(i, j int) bool {
		if instances[i].Version != instances[j].Version {
			return instances[i].Version > instances[j].Version
		}
		return instances[i].Pid < instances[j].Pid
	})

	return instances
}

// ResolveURL determines the bridge URL using the following priority:
// 1. Explicit --url flag (already resolved by caller)
// 2. --pid <pid> — find instance with matching PID
// 3. --revit <version> — find first instance of that version
// 4. Auto-discover — if exactly one instance is alive, use it
// 5. Fallback — http://localhost:5000
func ResolveURL(explicitURL string, pidFlag int, revitFlag int) string {
	if explicitURL != "" {
		return explicitURL
	}

	instances := Discover()

	// --pid takes highest priority after --url.
	if pidFlag > 0 {
		for _, inst := range instances {
			if inst.Pid == pidFlag {
				return fmt.Sprintf("http://localhost:%d", inst.Port)
			}
		}
		fmt.Fprintf(os.Stderr, "No running Revit instance with PID %d found.\n", pidFlag)
		os.Exit(1)
	}

	// --revit: find first instance of that version.
	if revitFlag > 0 {
		for _, inst := range instances {
			if inst.Version == revitFlag {
				return fmt.Sprintf("http://localhost:%d", inst.Port)
			}
		}
		fmt.Fprintf(os.Stderr, "No running Revit %d instance found.\n", revitFlag)
		os.Exit(1)
	}

	// Auto-discover: single instance → use it.
	if len(instances) == 1 {
		return fmt.Sprintf("http://localhost:%d", instances[0].Port)
	}

	// Multiple instances → prompt user.
	if len(instances) > 1 {
		fmt.Fprintln(os.Stderr, "Multiple Revit instances detected. Specify which one to use:")
		PrintInstances(instances)
		fmt.Fprintln(os.Stderr, "\nUse --pid <pid> or --revit <version> to select an instance.")
		os.Exit(1)
	}

	// No instances found → fallback to legacy default.
	return "http://localhost:5000"
}

// PrintInstances formats instance info as a table.
func PrintInstances(instances []InstanceInfo) {
	if len(instances) == 0 {
		fmt.Println("No running Revit instances found.")
		return
	}

	fmt.Printf("  %-6s %-8s %-6s %s\n", "PID", "VERSION", "PORT", "DOCUMENT")
	for _, inst := range instances {
		doc := inst.Document
		if doc == "" {
			doc = "-"
		}
		fmt.Printf("  %-6d %-8d %-6d %s\n", inst.Pid, inst.Version, inst.Port, doc)
	}
}

// matchInstanceFile checks if a filename matches the revit-{version}-{pid}.json pattern.
func matchInstanceFile(name string) bool {
	if len(name) < 6 || name[:6] != "revit-" {
		return false
	}
	ext := filepath.Ext(name)
	if ext != ".json" {
		return false
	}
	return true
}

// isProcessAlive checks if a process with the given PID is running.
// On Windows, uses tasklist command since os.FindProcess always succeeds.
// On non-Windows, uses os.FindProcess + Signal(0).
func isProcessAlive(pid int) bool {
	if runtime.GOOS == "windows" {
		return isWindowsProcessAlive(pid)
	}
	// Unix: use FindProcess + Signal(0).
	proc, err := os.FindProcess(pid)
	if err != nil {
		return false
	}
	return proc.Signal(nil) == nil
}

// isWindowsProcessAlive uses tasklist to check if a PID exists on Windows.
func isWindowsProcessAlive(pid int) bool {
	cmd := exec.Command("tasklist", "/FI", fmt.Sprintf("PID eq %d", pid), "/NH", "/FO", "CSV")
	output, err := cmd.Output()
	if err != nil {
		// If tasklist fails, conservatively assume the process is alive.
		return true
	}
	// tasklist /NH /FO CSV outputs one line per matching process, e.g.:
	// "revit.exe","1234","Console","1","1,234 KB"
	// If no match, output is: "INFO: No tasks are running which match the specified criteria."
	line := strings.TrimSpace(string(output))
	if line == "" || strings.HasPrefix(line, "INFO:") {
		return false
	}
	// Verify the PID appears in the output.
	return strings.Contains(line, fmt.Sprintf("%d", pid))
}

// ParseVersion parses a Revit version string (e.g. "2022") to int.
func ParseVersion(s string) (int, bool) {
	v, err := strconv.Atoi(s)
	if err != nil || v < 2019 || v > 2099 {
		return 0, false
	}
	return v, true
}
