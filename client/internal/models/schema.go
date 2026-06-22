package models

import "time"

// CommandSchema is the full schema response for GET /api/commands.
// Mirrors C# CliBridge.Abstractions.CommandSchema.
type CommandSchema struct {
	Version    string        `json:"version"`
	FetchedAt  time.Time     `json:"fetched_at"`
	ServerInfo *ServerInfo   `json:"server_info,omitempty"`
	Commands   []CommandDef  `json:"commands"`
}

// ServerInfo is server metadata included in the schema response.
type ServerInfo struct {
	BridgeVersion string         `json:"bridge_version,omitempty"`
	Host          string         `json:"host,omitempty"`
	Port          int            `json:"port"`
	Plugins       []string       `json:"plugins,omitempty"`
	Features      *ServerFeatures `json:"features,omitempty"`
}

// ServerFeatures advertises feature flags from the server.
type ServerFeatures struct {
	DryRun        bool     `json:"dry_run"`
	ExecuteRaw    bool     `json:"execute_raw"`
	OutputFormats []string `json:"output_formats,omitempty"`
}

// CommandDef describes a single command's metadata for schema discovery.
// Mirrors C# CliBridge.Abstractions.CommandDef.
type CommandDef struct {
	Name           string              `json:"name"`
	Description    string              `json:"description,omitempty"`
	Category       string              `json:"category,omitempty"`
	Aliases        []string            `json:"aliases,omitempty"`
	DomainPath     string              `json:"domain_path,omitempty"`
	SupportsDryRun bool                `json:"supports_dry_run"`
	Parameters     []CommandParamSchema `json:"parameters,omitempty"`
	Examples       []string            `json:"examples,omitempty"`
}

// CommandParamSchema describes a single parameter of a bridge command.
// Mirrors C# CliBridge.Abstractions.CommandParamSchema.
type CommandParamSchema struct {
	Name        string              `json:"name"`
	Type        string              `json:"type"`
	Required    bool                `json:"required"`
	Description string              `json:"description,omitempty"`
	Default     interface{}         `json:"default,omitempty"`
	ShortFlag   string              `json:"short_flag,omitempty"`
	EnumValues  []string            `json:"enum_values,omitempty"`
	Properties  []CommandParamSchema `json:"properties,omitempty"`
	Context     interface{}         `json:"context,omitempty"`
}
