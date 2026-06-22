// Package abstractions defines the command contracts and shared utilities
// for the CLI client. Mirrors C# RevitCliClient.Abstractions.
package abstractions

import "context"

// CommandCategory enumerates command grouping categories.
// Mirrors C# RevitCliClient.Abstractions.CommandCategory.
type CommandCategory int

const (
	CategorySystem CommandCategory = iota
	CategoryDocument
	CategoryQuery
	CategoryCreate
	CategoryModify
	CategoryTransform
	CategoryViewExport
	CategoryRaw
	CategoryCustom
)

// DisplayName returns the human-readable category name.
// Mirrors C# CommandCategoryDisplay.GetDisplayName.
func (c CommandCategory) DisplayName() string {
	switch c {
	case CategorySystem:
		return "System"
	case CategoryDocument:
		return "Document"
	case CategoryQuery:
		return "Document & Query"
	case CategoryCreate:
		return "Create"
	case CategoryModify:
		return "Modify"
	case CategoryTransform:
		return "Transform"
	case CategoryViewExport:
		return "View & Export"
	case CategoryRaw:
		return "Raw"
	case CategoryCustom:
		return "Custom"
	default:
		return "Custom"
	}
}

// MapCategory maps a server-side category string to a CommandCategory.
// Mirrors C# DynamicCommand.MapCategory.
func MapCategory(category string) CommandCategory {
	switch toLowerASCII(category) {
	case "system":
		return CategorySystem
	case "document":
		return CategoryDocument
	case "query":
		return CategoryQuery
	case "create":
		return CategoryCreate
	case "modify":
		return CategoryModify
	case "transform":
		return CategoryTransform
	case "view", "viewexport", "export":
		return CategoryViewExport
	case "raw":
		return CategoryRaw
	default:
		return CategoryCustom
	}
}

func toLowerASCII(s string) string {
	b := []byte(s)
	for i, c := range b {
		if c >= 'A' && c <= 'Z' {
			b[i] = c + ('a' - 'A')
		}
	}
	return string(b)
}

// CategoryOrder defines the preferred display order for help text.
// Mirrors C# HelpText.CategoryOrder.
var CategoryOrder = []CommandCategory{
	CategorySystem,
	CategoryQuery,
	CategoryCreate,
	CategoryModify,
	CategoryTransform,
	CategoryViewExport,
	CategoryRaw,
}

// SendCommandFunc sends a command to the bridge server and returns an exit code.
// Mirrors C# RevitCliClient.Abstractions.SendCommandFunc delegate.
// The parameters argument may be nil, a map, or any JSON-serializable value.
type SendCommandFunc func(ctx context.Context, command string, parameters interface{}) int
