package discovery

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
	"time"
)

// InstanceInfo mirrors the C# InstanceInfo class written by the bridge.
type InstanceInfo struct {
	Pid           int    `json:"pid"`
	Version       int    `json:"version"`
	Port          int    `json:"port"`
	Document      string `json:"document"`
	StartedAt     string `json:"started_at"`
	Hostname      string `json:"hostname"`
	CommandsCount int    `json:"commands_count"`
}

// DiscoverInstances reads all instance registry files and returns
// information about running Revit instances. Stale entries (PID not alive)
// are filtered out.
func DiscoverInstances() []InstanceInfo {
	dir := InstancesDir()
	entries, err := os.ReadDir(dir)
	if err != nil {
		return nil
	}

	var instances []InstanceInfo
	for _, entry := range entries {
		if entry.IsDir() || !strings.HasPrefix(entry.Name(), "revit-") || !strings.HasSuffix(entry.Name(), ".json") {
			continue
		}

		data, err := os.ReadFile(filepath.Join(dir, entry.Name()))
		if err != nil {
			continue
		}

		var info InstanceInfo
		if err := json.Unmarshal(data, &info); err != nil {
			continue
		}

		if !isPidAlive(info.Pid) {
			// Clean up stale file
			os.Remove(filepath.Join(dir, entry.Name()))
			continue
		}

		instances = append(instances, info)
	}

	// Sort by version desc, then by PID
	sort.Slice(instances, func(i, j int) bool {
		if instances[i].Version != instances[j].Version {
			return instances[i].Version > instances[j].Version
		}
		return instances[i].Pid < instances[j].Pid
	})

	return instances
}

// FindInstanceByPID finds an instance with the given PID.
func FindInstanceByPID(pid int) *InstanceInfo {
	for _, inst := range DiscoverInstances() {
		if inst.Pid == pid {
			// Copy to heap to avoid returning pointer to loop variable.
			copied := inst
			return &copied
		}
	}
	return nil
}

// FindInstancesByVersion finds all instances of a given Revit version.
func FindInstancesByVersion(version int) []InstanceInfo {
	var result []InstanceInfo
	for _, inst := range DiscoverInstances() {
		if inst.Version == version {
			result = append(result, inst)
		}
	}
	return result
}

// ResolveBaseURL determines the bridge URL using the following priority:
// 1. explicitURL (from --url flag) — returned as-is if non-empty
// 2. pid (from --pid flag) — look up instance by PID
// 3. version (from --revit flag) — find first instance of that version
// 4. auto-discover — if exactly one instance, use it; if multiple, return error
// 5. fallback — http://localhost:5000
func ResolveBaseURL(explicitURL string, pid int, version int) (string, error) {
	// 1. Explicit URL
	if explicitURL != "" {
		return explicitURL, nil
	}

	instances := DiscoverInstances()

	// 2. By PID
	if pid > 0 {
		for _, inst := range instances {
			if inst.Pid == pid {
				return fmt.Sprintf("http://localhost:%d", inst.Port), nil
			}
		}
		return "", fmt.Errorf("no running Revit instance with PID %d found", pid)
	}

	// 3. By version
	if version > 0 {
		matches := FindInstancesByVersion(version)
		if len(matches) == 0 {
			return "", fmt.Errorf("no running Revit %d instance found", version)
		}
		if len(matches) > 1 {
			return "", fmt.Errorf("multiple Revit %d instances running (PIDs: %s); use --pid to select one",
				version, pidList(matches))
		}
		return fmt.Sprintf("http://localhost:%d", matches[0].Port), nil
	}

	// 4. Auto-discover
	if len(instances) == 1 {
		return fmt.Sprintf("http://localhost:%d", instances[0].Port), nil
	}
	if len(instances) > 1 {
		return "", fmt.Errorf("multiple Revit instances running; use --revit <version> or --pid <pid> to select one.\nRun 'revit-cli list' to see available instances")
	}

	// 5. Fallback
	return "http://localhost:5000", nil
}

func pidList(instances []InstanceInfo) string {
	pids := make([]string, len(instances))
	for i, inst := range instances {
		pids[i] = strconv.Itoa(inst.Pid)
	}
	return strings.Join(pids, ", ")
}

// isPidAlive checks if a process with the given PID exists.
// On Windows, uses tasklist command since os.FindProcess always succeeds.
// On non-Windows, uses os.FindProcess + Signal(0).
func isPidAlive(pid int) bool {
	if runtime.GOOS == "windows" {
		return isWindowsPidAlive(pid)
	}
	proc, err := os.FindProcess(pid)
	if err != nil {
		return false
	}
	return proc.Signal(nil) == nil
}

// isWindowsPidAlive uses tasklist to check if a PID exists on Windows.
func isWindowsPidAlive(pid int) bool {
	cmd := exec.Command("tasklist", "/FI", fmt.Sprintf("PID eq %d", pid), "/NH", "/FO", "CSV")
	output, err := cmd.Output()
	if err != nil {
		// If tasklist fails, conservatively assume the process is alive.
		return true
	}
	line := strings.TrimSpace(string(output))
	if line == "" || strings.HasPrefix(line, "INFO:") {
		return false
	}
	return strings.Contains(line, fmt.Sprintf("%d", pid))
}

// FormatInstancesTable formats instance info as a human-readable table.
func FormatInstancesTable(instances []InstanceInfo) string {
	if len(instances) == 0 {
		return "No running Revit instances found.\nMake sure Revit is running with the CLI Bridge add-in enabled."
	}

	var sb strings.Builder
	sb.WriteString("Running Revit Instances:\n\n")
	sb.WriteString(fmt.Sprintf("  %-8s %-8s %-8s %s\n", "PID", "VERSION", "PORT", "DOCUMENT"))
	sb.WriteString("  -------- -------- -------- ----------------\n")
	for _, inst := range instances {
		doc := inst.Document
		if doc == "" {
			doc = "-"
		}
		sb.WriteString(fmt.Sprintf("  %-8d %-8d %-8d %s\n", inst.Pid, inst.Version, inst.Port, doc))
	}

	// Calculate uptime for each instance
	sb.WriteString("\n")
	for _, inst := range instances {
		if inst.StartedAt != "" {
			if t, err := time.Parse(time.RFC3339, inst.StartedAt); err == nil {
				uptime := time.Since(t).Truncate(time.Minute)
				sb.WriteString(fmt.Sprintf("  PID %d: uptime %s\n", inst.Pid, uptime))
			}
		}
	}

	return sb.String()
}
