// Package models defines data transfer objects matching the C# CliBridge
// JSON wire contract. All field names use snake_case JSON tags to remain
// byte-compatible with the Revit CLI Bridge server.
package models

// RevitCommandInput represents a command request from CLI to Revit.
// Mirrors C# RevitCliBridge.Models.RevitCommandInput.
type RevitCommandInput struct {
	TaskID         string                 `json:"task_id,omitempty"`
	Command        string                 `json:"command"`
	Parameters     map[string]interface{} `json:"parameters,omitempty"`
	TimeoutSeconds *int                   `json:"timeout_seconds,omitempty"`
	Async          *bool                  `json:"async,omitempty"`
	DryRun         bool                   `json:"dry_run,omitempty"`
}
