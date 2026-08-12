package builtin

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"sort"

	"revit-cli/internal/abstractions"
	"revit-cli/internal/client/auth"
	"revit-cli/internal/models"
)

// CatalogHandler fetches and displays the lightweight command catalog from
// the bridge's GET /api/catalog endpoint. Unlike "commands" which fetches
// the full schema, "catalog" returns only names, categories, and summaries
// — ideal for AI agent discovery where the full schema would waste tokens.
type CatalogHandler struct {
	BaseURL string
	Client  *http.Client
}

func (h CatalogHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "catalog",
		Description: "List all commands (compact index)",
		Usage:       "catalog [--json]",
		Category:    abstractions.CategorySystem,
		Examples:    []string{"revit-cli catalog", "revit-cli catalog --json"},
	}
}

func (h CatalogHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	url := h.BaseURL + "/api/catalog"

	req, err := http.NewRequest(http.MethodGet, url, nil)
	if err != nil {
		printErr(fmt.Sprintf("Cannot build request: %v", err))
		return 1
	}
	auth.WithAuth(req, h.BaseURL)

	resp, err := h.Client.Do(req)
	if err != nil {
		printErr(fmt.Sprintf("Cannot fetch catalog from %s: %v", url, err))
		return 1
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		printErr(fmt.Sprintf("Error reading response: %v", err))
		return 1
	}

	if resp.StatusCode != http.StatusOK {
		printErr(fmt.Sprintf("Server returned %d: %s", resp.StatusCode, string(body)))
		return 1
	}

	var catalog models.CommandCatalog
	if err := json.Unmarshal(body, &catalog); err != nil {
		printErr(fmt.Sprintf("Error parsing catalog: %v", err))
		return 1
	}

	if abstractions.HasFlag(args, "--json") {
		pretty, _ := json.MarshalIndent(catalog, "", "  ")
		fmt.Println(string(pretty))
		return 0
	}

	// Compact text output grouped by category.
	fmt.Printf("Bridge version: %s\n", catalog.CatalogVersion)
	fmt.Printf("Commands (%d):\n", catalog.CommandCount)

	byCategory := make(map[string][]models.CatalogEntry)
	var categories []string
	for _, cmd := range catalog.Commands {
		cat := cmd.Category
		if cat == "" {
			cat = "General"
		}
		if _, exists := byCategory[cat]; !exists {
			categories = append(categories, cat)
		}
		byCategory[cat] = append(byCategory[cat], cmd)
	}
	sort.Strings(categories)

	for _, cat := range categories {
		fmt.Printf("\n  [%s]\n", cat)
		for _, cmd := range byCategory[cat] {
			summary := cmd.Summary
			if summary == "" {
				summary = "-"
			}
			fmt.Printf("    %-28s %s\n", cmd.Name, summary)
		}
	}

	return 0
}
