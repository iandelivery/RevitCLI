package builtin

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"sync"
	"time"

	"revit-cli/internal/client/auth"
	"revit-cli/internal/instance"
)

const documentsTimeout = 2 * time.Second

type documentEntry struct {
	Title    string  `json:"title"`
	Path     *string `json:"path"`
	IsActive bool    `json:"is_active"`
}

type documentsResponse struct {
	Documents  []documentEntry `json:"documents"`
	ActivePath *string         `json:"active_path"`
}

type enrichedInstance struct {
	instance.InstanceInfo

	Documents  []documentEntry `json:"documents,omitempty"`
	ActivePath *string         `json:"active_path,omitempty"`
	Reachable  bool            `json:"reachable"`
}

func fetchDocuments(ctx context.Context, client *http.Client, baseURL string) (*documentsResponse, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, baseURL+"/api/documents", nil)
	if err != nil {
		return nil, err
	}
	auth.WithAuth(req, baseURL)

	resp, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("status %d", resp.StatusCode)
	}

	var payload documentsResponse
	if err := json.NewDecoder(resp.Body).Decode(&payload); err != nil {
		return nil, err
	}
	return &payload, nil
}

func enrichInstances(parent context.Context, instances []instance.InstanceInfo) []enrichedInstance {
	enriched := make([]enrichedInstance, len(instances))
	for i, inst := range instances {
		enriched[i] = enrichedInstance{InstanceInfo: inst}
	}

	client := &http.Client{Timeout: documentsTimeout}
	var wg sync.WaitGroup
	for i := range enriched {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			baseURL := fmt.Sprintf("http://localhost:%d", enriched[i].Port)
			ctx, cancel := context.WithTimeout(parent, documentsTimeout)
			defer cancel()

			payload, err := fetchDocuments(ctx, client, baseURL)
			if err != nil {
				return
			}
			enriched[i].Documents = payload.Documents
			enriched[i].ActivePath = payload.ActivePath
			enriched[i].Reachable = true
		}(i)
	}
	wg.Wait()
	return enriched
}

func documentDisplay(inst enrichedInstance) string {
	if !inst.Reachable {
		return "- (unreachable)"
	}
	if inst.ActivePath != nil && *inst.ActivePath != "" {
		display := *inst.ActivePath
		if len(inst.Documents) > 1 {
			display += fmt.Sprintf(" (%d open)", len(inst.Documents))
		}
		return display
	}
	if len(inst.Documents) > 0 {
		return fmt.Sprintf("- (%d open)", len(inst.Documents))
	}
	return "-"
}

func PrintEnrichedInstances(instances []enrichedInstance) {
	if len(instances) == 0 {
		fmt.Println("No running Revit instances found.")
		return
	}

	fmt.Printf("  %-6s %-8s %-6s %s\n", "PID", "VERSION", "PORT", "DOCUMENT")
	for _, inst := range instances {
		fmt.Printf("  %-6d %-8d %-6d %s\n", inst.Pid, inst.Version, inst.Port, documentDisplay(inst))
	}
}
