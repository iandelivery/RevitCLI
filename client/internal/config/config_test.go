package config

import (
	"os"
	"path/filepath"
	"testing"
)

func TestDefault_ReturnsSaneValues(t *testing.T) {
	cfg := Default()
	if !cfg.Enabled {
		t.Error("expected Enabled=true by default")
	}
	if cfg.Port != 5000 {
		t.Errorf("expected Port=5000, got %d", cfg.Port)
	}
	if !cfg.AutoPort {
		t.Error("expected AutoPort=true by default")
	}
	if cfg.TimeoutSeconds != 180 {
		t.Errorf("expected TimeoutSeconds=180, got %d", cfg.TimeoutSeconds)
	}
	if cfg.MaxCommandQueueSize != 100 {
		t.Errorf("expected MaxCommandQueueSize=100, got %d", cfg.MaxCommandQueueSize)
	}
	if cfg.AllowRawExecution {
		t.Error("expected AllowRawExecution=false by default")
	}
}

func TestValidate_RejectsInvalidPort(t *testing.T) {
	cfg := Default()
	cfg.Port = 0
	if err := cfg.Validate(); err == nil {
		t.Error("expected validation error for port=0")
	}
	cfg.Port = 70000
	if err := cfg.Validate(); err == nil {
		t.Error("expected validation error for port=70000")
	}
}

func TestValidate_RejectsZeroTimeout(t *testing.T) {
	cfg := Default()
	cfg.TimeoutSeconds = 0
	if err := cfg.Validate(); err == nil {
		t.Error("expected validation error for timeout=0")
	}
}

func TestValidate_RejectsZeroQueueSize(t *testing.T) {
	cfg := Default()
	cfg.MaxCommandQueueSize = 0
	if err := cfg.Validate(); err == nil {
		t.Error("expected validation error for queue size=0")
	}
}

func TestValidate_AcceptsValidConfig(t *testing.T) {
	cfg := Default()
	if err := cfg.Validate(); err != nil {
		t.Errorf("expected no error for valid config, got %v", err)
	}
}

func TestLoad_ReturnsDefaultWhenFileMissing(t *testing.T) {
	cfg := Load("/nonexistent/path/cli_bridge_setting.json")
	if cfg.Port != 5000 {
		t.Errorf("expected default Port=5000, got %d", cfg.Port)
	}
}

func TestLoad_ParsesApiKey(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "cli_bridge_setting.json")
	content := `{
		"enabled": true,
		"port": 5041,
		"auto_port": true,
		"timeout_seconds": 60,
		"max_command_queue_size": 50,
		"allow_raw_execution": false,
		"api_key": "test-key-12345"
	}`
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}

	cfg := Load(path)
	if cfg.APIKey != "test-key-12345" {
		t.Errorf("expected APIKey='test-key-12345', got %q", cfg.APIKey)
	}
	if cfg.Port != 5041 {
		t.Errorf("expected Port=5041, got %d", cfg.Port)
	}
	if cfg.TimeoutSeconds != 60 {
		t.Errorf("expected TimeoutSeconds=60, got %d", cfg.TimeoutSeconds)
	}
}

func TestLoad_FillsZeroValuesWithDefaults(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "cli_bridge_setting.json")
	// Port/timeout/queue all zero — Load should fill in defaults.
	content := `{"enabled": true}`
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}

	cfg := Load(path)
	if cfg.Port != 5000 {
		t.Errorf("expected default Port=5000, got %d", cfg.Port)
	}
	if cfg.TimeoutSeconds != 180 {
		t.Errorf("expected default TimeoutSeconds=180, got %d", cfg.TimeoutSeconds)
	}
	if cfg.MaxCommandQueueSize != 100 {
		t.Errorf("expected default MaxCommandQueueSize=100, got %d", cfg.MaxCommandQueueSize)
	}
}

func TestLoad_FallsBackToDefaultOnBadJSON(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "cli_bridge_setting.json")
	if err := os.WriteFile(path, []byte("{not valid json"), 0o644); err != nil {
		t.Fatal(err)
	}

	cfg := Load(path)
	// Should fall back to Default(), not panic.
	if cfg.Port != 5000 {
		t.Errorf("expected default Port=5000 after parse failure, got %d", cfg.Port)
	}
}
