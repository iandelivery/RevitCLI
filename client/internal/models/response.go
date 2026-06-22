package models

import "encoding/json"

// CommandResponse represents a command response returned to the CLI client.
// Mirrors C# CliBridge.Abstractions.CommandResponse.
type CommandResponse struct {
	TaskID       string      `json:"task_id"`
	Status       string      `json:"status"`
	Message      string      `json:"message"`
	Data         interface{} `json:"data,omitempty"`
	ErrorDetails string      `json:"error_details,omitempty"`
}

// Success builds a success CommandResponse.
func Success(taskID string, data interface{}, message string) CommandResponse {
	if message == "" {
		message = "Success"
	}
	return CommandResponse{
		TaskID:  taskID,
		Status:  "success",
		Message: message,
		Data:    data,
	}
}

// Error builds an error CommandResponse.
func Error(taskID string, message string, errorDetails string) CommandResponse {
	return CommandResponse{
		TaskID:       taskID,
		Status:       "error",
		Message:      message,
		ErrorDetails: errorDetails,
	}
}

// ToJSON serializes the response to JSON.
func (r CommandResponse) ToJSON() string {
	b, _ := json.Marshal(r)
	return string(b)
}

// QueuedCommand represents a queued command waiting for execution on the
// Revit main thread. Mirrors C# CliBridge.Abstractions.QueuedCommand.
type QueuedCommand struct {
	TaskID     string      `json:"task_id"`
	Command    string      `json:"command"`
	Parameters interface{} `json:"parameters"`
	DryRun     bool        `json:"dry_run"`
}
