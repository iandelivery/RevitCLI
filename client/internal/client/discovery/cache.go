// Package discovery implements schema-driven command discovery: fetching
// command schemas from the bridge server, caching them locally, and building
// dynamic CLI commands from the schema. Mirrors C# RevitCliClient.Discovery.
package discovery

import (
	"encoding/json"
	"log"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"revit-cli/internal/models"
)

// SchemaCache provides local file caching for command schemas with TTL-based
// expiration, stale-cache fallback, and per-server keys.
// Mirrors C# RevitCliClient.Discovery.SchemaCache.
type SchemaCache struct {
	serverKey string
	cachePath string
	etagPath  string
	mu        sync.Mutex
}

// cacheTTL is the freshness window for cached schemas.
const cacheTTL = 30 * time.Minute

// NewSchemaCache creates a cache instance for a specific server URL.
// If serverURL is empty, uses the global default cache path.
func NewSchemaCache(serverURL string) *SchemaCache {
	key := computeServerKey(serverURL)
	dir := cacheDir()

	var cachePath, etagPath string
	if key == "default" {
		cachePath = filepath.Join(dir, "schema-cache.json")
		etagPath = filepath.Join(dir, "schema-etag.txt")
	} else {
		serverDir := filepath.Join(dir, "servers")
		cachePath = filepath.Join(serverDir, "schema-cache_"+key+".json")
		etagPath = filepath.Join(serverDir, "schema-etag_"+key+".txt")
	}

	return &SchemaCache{
		serverKey: key,
		cachePath: cachePath,
		etagPath:  etagPath,
	}
}

// Load returns the cached schema if it exists and is not expired.
// Returns nil if cache is missing or expired.
func (c *SchemaCache) Load() *models.CommandSchema {
	c.mu.Lock()
	defer c.mu.Unlock()

	return c.loadLocked()
}

// LoadWithVersion returns the cached schema if it exists, is not expired,
// and matches the given bridge version. Returns nil if any check fails,
// which signals the caller to re-fetch from the server.
func (c *SchemaCache) LoadWithVersion(bridgeVersion string) *models.CommandSchema {
	c.mu.Lock()
	defer c.mu.Unlock()

	schema := c.loadLocked()
	if schema == nil {
		return nil
	}

	// If a bridge version is provided and the cached schema has a different
	// version, treat the cache as stale so the caller re-fetches.
	if bridgeVersion != "" && schema.ServerInfo != nil &&
		schema.ServerInfo.BridgeVersion != "" &&
		schema.ServerInfo.BridgeVersion != bridgeVersion {
		return nil
	}

	return schema
}

// loadLocked is the internal implementation of Load. Caller must hold c.mu.
func (c *SchemaCache) loadLocked() *models.CommandSchema {
	info, err := os.Stat(c.cachePath)
	if err != nil {
		return nil
	}
	if time.Since(info.ModTime()) > cacheTTL {
		return nil
	}

	data, err := os.ReadFile(c.cachePath)
	if err != nil {
		return nil
	}
	var schema models.CommandSchema
	if err := json.Unmarshal(data, &schema); err != nil {
		return nil
	}
	return &schema
}

// LoadStale returns the cached schema regardless of TTL. Used as fallback
// when the bridge is unreachable.
func (c *SchemaCache) LoadStale() *models.CommandSchema {
	c.mu.Lock()
	defer c.mu.Unlock()

	data, err := os.ReadFile(c.cachePath)
	if err != nil {
		return nil
	}
	var schema models.CommandSchema
	if err := json.Unmarshal(data, &schema); err != nil {
		return nil
	}
	return &schema
}

// Save writes the schema to the local cache using atomic write (temp file + rename)
// to prevent corruption if the process crashes mid-write.
func (c *SchemaCache) Save(schema *models.CommandSchema) {
	c.mu.Lock()
	defer c.mu.Unlock()

	if err := os.MkdirAll(filepath.Dir(c.cachePath), 0o755); err != nil {
		log.Printf("[cache] cannot create dir: %v", err)
		return
	}
	data, err := json.MarshalIndent(schema, "", "  ")
	if err != nil {
		log.Printf("[cache] cannot marshal schema: %v", err)
		return
	}

	// Atomic write: write to temp file, then rename.
	tmpPath := c.cachePath + ".tmp"
	if err := os.WriteFile(tmpPath, data, 0o644); err != nil {
		log.Printf("[cache] cannot write temp cache: %v", err)
		return
	}
	if err := os.Rename(tmpPath, c.cachePath); err != nil {
		log.Printf("[cache] cannot rename temp cache: %v", err)
		// Clean up temp file on failure.
		os.Remove(tmpPath)
	}
}

// LoadEtag returns the stored ETag for conditional requests.
func (c *SchemaCache) LoadEtag() string {
	c.mu.Lock()
	defer c.mu.Unlock()

	data, err := os.ReadFile(c.etagPath)
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(data))
}

// SaveEtag persists the ETag from a server response.
func (c *SchemaCache) SaveEtag(etag string) {
	c.mu.Lock()
	defer c.mu.Unlock()

	if err := os.MkdirAll(filepath.Dir(c.etagPath), 0o755); err != nil {
		log.Printf("[cache] cannot create dir for etag: %v", err)
		return
	}
	if err := os.WriteFile(c.etagPath, []byte(etag), 0o644); err != nil {
		log.Printf("[cache] cannot write etag: %v", err)
	}
}

// Touch updates the cache file's modification time, effectively extending the TTL.
// Used when the server returns 304 Not Modified.
func (c *SchemaCache) Touch() error {
	c.mu.Lock()
	defer c.mu.Unlock()

	return os.Chtimes(c.cachePath, time.Now(), time.Now())
}

// CacheDir exposes the resolved cache directory so other packages
// (e.g. configure check) can report which path is in use.
func CacheDir() string {
	return filepath.Join(DataDir(), "cache")
}

// InstancesDir returns the directory where instance registry files are stored.
// Uses the same cascading strategy as the cache directory.
func InstancesDir() string {
	return filepath.Join(DataDir(), "instances")
}

// DataDir returns the base revit-cli data directory using a cascading strategy:
//
//  1. REVIT_CLI_DATA_DIR environment variable (explicit override for
//     headless/CI contexts)
//  2. %LOCALAPPDATA%\revit-cli (best Windows practice for local app data)
//  3. %USERPROFILE%\.revit-cli (standard CLI dot-folder fallback)
//  4. <executable directory>\.revit-cli (portable mode)
func DataDir() string {
	// 1. Explicit override.
	if dir := os.Getenv("REVIT_CLI_DATA_DIR"); dir != "" {
		return dir
	}

	// 2. Local AppData — best Windows practice for local app data.
	if localAppData := os.Getenv("LOCALAPPDATA"); localAppData != "" {
		dir := filepath.Join(localAppData, "revit-cli")
		if tryCreateDir(dir) {
			return dir
		}
	}

	// 3. User profile dot-folder — standard CLI convention.
	if userProfile := os.Getenv("USERPROFILE"); userProfile != "" {
		dir := filepath.Join(userProfile, ".revit-cli")
		if tryCreateDir(dir) {
			return dir
		}
	}

	// 4. Portable mode — next to the executable.
	if exePath, err := os.Executable(); err == nil {
		dir := filepath.Join(filepath.Dir(exePath), ".revit-cli")
		if tryCreateDir(dir) {
			return dir
		}
		return dir
	}

	return ".revit-cli"
}

// cacheDir returns the cache root directory.
// Kept for internal use by SchemaCache.
func cacheDir() string {
	return CacheDir()
}

// tryCreateDir attempts to create the directory and returns true on success.
func tryCreateDir(dir string) bool {
	err := os.MkdirAll(dir, 0o755)
	return err == nil
}

// computeServerKey derives a filesystem-safe key from a server URL.
// "http://localhost:5000" -> "localhost_5000"
// empty -> "default"
func computeServerKey(serverURL string) string {
	if serverURL == "" {
		return "default"
	}
	// Strip scheme.
	s := serverURL
	s = strings.TrimPrefix(s, "http://")
	s = strings.TrimPrefix(s, "https://")
	// Take host:port (assume no path).
	hostPort := strings.SplitN(s, "/", 2)[0]
	if hostPort == "" {
		return "default"
	}
	// Replace unsafe filename characters.
	hostPort = strings.ReplaceAll(hostPort, ".", "_")
	hostPort = strings.ReplaceAll(hostPort, ":", "_")
	// Handle IPv6 brackets: [::1]_5000 -> _1_5000
	hostPort = strings.ReplaceAll(hostPort, "[", "")
	hostPort = strings.ReplaceAll(hostPort, "]", "")
	return hostPort
}
