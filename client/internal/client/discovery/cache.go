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

// Save writes the schema to the local cache.
func (c *SchemaCache) Save(schema *models.CommandSchema) {
	if err := os.MkdirAll(filepath.Dir(c.cachePath), 0o755); err != nil {
		log.Printf("[cache] cannot create dir: %v", err)
		return
	}
	schema.FetchedAt = time.Now().UTC()
	data, err := json.MarshalIndent(schema, "", "  ")
	if err != nil {
		return
	}
	if err := os.WriteFile(c.cachePath, data, 0o644); err != nil {
		log.Printf("[cache] cannot write cache: %v", err)
	}
}

// LoadEtag returns the stored ETag for conditional requests.
func (c *SchemaCache) LoadEtag() string {
	data, err := os.ReadFile(c.etagPath)
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(data))
}

// SaveEtag persists the ETag from a server response.
func (c *SchemaCache) SaveEtag(etag string) {
	if err := os.MkdirAll(filepath.Dir(c.etagPath), 0o755); err != nil {
		return
	}
	if err := os.WriteFile(c.etagPath, []byte(etag), 0o644); err != nil {
		log.Printf("[cache] cannot write etag: %v", err)
	}
}

// cacheDir returns the cache root directory under %AppData%/revit-cli.
func cacheDir() string {
	base, err := os.UserConfigDir() // %AppData% on Windows
	if err != nil || base == "" {
		base = "."
	}
	return filepath.Join(base, "revit-cli")
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
	return hostPort
}
