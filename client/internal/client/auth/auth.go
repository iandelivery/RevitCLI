// Package auth provides API key storage and HTTP request authentication
// for the revit-cli client. The API key is synced from the bridge's
// cli_bridge_setting.json during `configure setup` and persisted in the
// client cache directory keyed by server URL.
package auth

import (
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

// apiKeyEntry holds the in-memory cached key for a server.
type apiKeyEntry struct {
	key string
}

var (
	mu       sync.RWMutex
	cache    = make(map[string]*apiKeyEntry)
	cacheDir = defaultCacheDir()
)

// defaultCacheDir returns the default cache directory location.
// Uses the same directory as the schema cache: <userCacheDir>/revit-cli.
func defaultCacheDir() string {
	dir, err := os.UserCacheDir()
	if err != nil || dir == "" {
		// Fall back to a temp-based path if the platform doesn't define one.
		dir = filepath.Join(os.TempDir(), "revit-cli")
	}
	return filepath.Join(dir, "revit-cli")
}

// SetCacheDir overrides the cache directory. Used by tests.
func SetCacheDir(dir string) {
	mu.Lock()
	defer mu.Unlock()
	cacheDir = dir
}

// apiKeyPath returns the path to the API key file for a given server URL.
func apiKeyPath(baseURL string) string {
	key := serverKey(baseURL)
	return filepath.Join(cacheDir, "servers", "apikey_"+key+".txt")
}

// serverKey derives a filesystem-safe key from a server URL, mirroring
// the scheme used by discovery.computeServerKey.
func serverKey(serverURL string) string {
	if serverURL == "" {
		return "default"
	}
	s := serverURL
	s = strings.TrimPrefix(s, "http://")
	s = strings.TrimPrefix(s, "https://")
	hostPort := strings.SplitN(s, "/", 2)[0]
	if hostPort == "" {
		return "default"
	}
	hostPort = strings.ReplaceAll(hostPort, ".", "_")
	hostPort = strings.ReplaceAll(hostPort, ":", "_")
	hostPort = strings.ReplaceAll(hostPort, "[", "")
	hostPort = strings.ReplaceAll(hostPort, "]", "")
	return hostPort
}

// GetAPIKey returns the API key for the given server URL.
// Looks up the in-memory cache first, then falls back to disk.
// Returns "" if no key is set for this server.
func GetAPIKey(baseURL string) string {
	mu.RLock()
	if entry, ok := cache[baseURL]; ok {
		mu.RUnlock()
		return entry.key
	}
	mu.RUnlock()

	// Cache miss — try disk.
	data, err := os.ReadFile(apiKeyPath(baseURL))
	if err != nil {
		return ""
	}
	key := strings.TrimSpace(string(data))
	if key == "" {
		return ""
	}

	mu.Lock()
	cache[baseURL] = &apiKeyEntry{key: key}
	mu.Unlock()
	return key
}

// SetAPIKey persists the API key for the given server URL to disk and
// updates the in-memory cache. Pass an empty key to clear.
func SetAPIKey(baseURL, apiKey string) error {
	mu.Lock()
	defer mu.Unlock()

	path := apiKeyPath(baseURL)
	if apiKey == "" {
		cache[baseURL] = &apiKeyEntry{key: ""}
		_ = os.Remove(path)
		return nil
	}

	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	if err := os.WriteFile(path, []byte(apiKey), 0o600); err != nil {
		return err
	}
	cache[baseURL] = &apiKeyEntry{key: apiKey}
	return nil
}

// WithAuth injects the Authorization: Bearer header into the request if
// an API key is configured for the given server URL. If no key is set,
// the request is returned unmodified (legacy mode).
// The request is modified in place and also returned for chaining.
func WithAuth(req *http.Request, baseURL string) *http.Request {
	if req == nil {
		return req
	}
	if key := GetAPIKey(baseURL); key != "" {
		req.Header.Set("Authorization", "Bearer "+key)
	}
	return req
}
