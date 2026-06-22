// Package config loads CLI Bridge configuration from .config/cli_bridge_setting.json.
// Mirrors C# RevitCliBridge.Models.CliBridgeConfig + CliBridgeConfigLoader.
package config

import (
	"embed"
	"encoding/json"
	"log"
	"os"
	"path/filepath"
)

//go:embed default_config.json
var defaultConfigFS embed.FS

// CliBridgeConfig mirrors C# CliBridgeConfig.
type CliBridgeConfig struct {
	Enabled              bool `json:"enabled"`
	Port                 int  `json:"port"`
	AutoPort             bool `json:"auto_port"`
	TimeoutSeconds       int  `json:"timeout_seconds"`
	MaxCommandQueueSize  int  `json:"max_command_queue_size"`
	AllowRawExecution    bool `json:"allow_raw_execution"`
}

// Default returns the default configuration. When a default_config.json is
// embedded at compile time, it is used; otherwise hardcoded defaults apply.
func Default() CliBridgeConfig {
	cfg := CliBridgeConfig{
		Enabled:             true,
		Port:                5000,
		AutoPort:            true,
		TimeoutSeconds:      180,
		MaxCommandQueueSize: 100,
		AllowRawExecution:   true,
	}

	data, err := defaultConfigFS.ReadFile("default_config.json")
	if err == nil {
		_ = json.Unmarshal(data, &cfg)
	}

	return cfg
}

// Load reads the config from the given path. If the file is missing or
// cannot be parsed, the default config is returned (non-fatal).
// Mirrors C# CliBridgeConfigLoader behavior.
func Load(path string) CliBridgeConfig {
	cfg := Default()

	data, err := os.ReadFile(path)
	if err != nil {
		log.Printf("[config] cannot read %s: %v; using defaults", path, err)
		return cfg
	}

	if err := json.Unmarshal(data, &cfg); err != nil {
		log.Printf("[config] cannot parse %s: %v; using defaults", path, err)
		return Default()
	}

	// Fill zero values with defaults
	if cfg.Port == 0 {
		cfg.Port = 5000
	}
	if cfg.TimeoutSeconds == 0 {
		cfg.TimeoutSeconds = 180
	}
	if cfg.MaxCommandQueueSize == 0 {
		cfg.MaxCommandQueueSize = 100
	}

	return cfg
}

// LoadFromDir loads config from a <dir>/.config/cli_bridge_setting.json path.
func LoadFromDir(dir string) CliBridgeConfig {
	return Load(filepath.Join(dir, ".config", "cli_bridge_setting.json"))
}
