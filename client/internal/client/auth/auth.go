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

type apiKeyEntry struct {
	key string
}

var (
	mu       sync.RWMutex
	cache    = make(map[string]*apiKeyEntry)
	override string
	// fallback resolves keys that live outside the disk cache, e.g.
	// in instance registry files. Wired once by the CLI entrypoint;
	// nil (the default) disables the fallback.
	fallback func(baseURL string) string
)

// defaultCacheDir resolves the directory that holds the per-server key
// files. REVIT_CLI_DATA_DIR scopes it like every other cache; otherwise it
// sits under the OS user cache dir.
func defaultCacheDir() string {
	if dir := os.Getenv("REVIT_CLI_DATA_DIR"); dir != "" {
		return dir
	}
	dir, err := os.UserCacheDir()
	if err != nil || dir == "" {
		dir = filepath.Join(os.TempDir(), "revit-cli")
	}
	return filepath.Join(dir, "revit-cli")
}

// SetCacheDir overrides the cache directory. An empty string restores the
// default. Used by tests.
func SetCacheDir(dir string) {
	mu.Lock()
	defer mu.Unlock()
	override = dir
}

// SetFallbackResolver wires a port-based key lookup consulted when no disk
// key exists for the server URL. Called once at startup by the CLI
// entrypoint; pass nil to remove it. Used by tests.
func SetFallbackResolver(fn func(baseURL string) string) {
	mu.Lock()
	defer mu.Unlock()
	fallback = fn
}

func resolveCacheDir() string {
	mu.RLock()
	dir := override
	mu.RUnlock()
	if dir != "" {
		return dir
	}
	return defaultCacheDir()
}

func apiKeyPath(baseURL string) string {
	key := serverKey(baseURL)
	return filepath.Join(resolveCacheDir(), "servers", "apikey_"+key+".txt")
}

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
	hostPort = strings.Replace(hostPort, "127.0.0.1", "localhost", 1)
	hostPort = strings.ReplaceAll(hostPort, ".", "_")
	hostPort = strings.ReplaceAll(hostPort, ":", "_")
	hostPort = strings.ReplaceAll(hostPort, "[", "")
	hostPort = strings.ReplaceAll(hostPort, "]", "")
	return hostPort
}

// GetAPIKey returns the API key for the given server URL.
// Looks up the in-memory cache first, then the disk cache, then the
// fallback resolver when one is wired. Returns "" if no key is set for
// this server.
func GetAPIKey(baseURL string) string {
	mu.RLock()
	entry, cached := cache[baseURL]
	resolver := fallback
	mu.RUnlock()
	if cached {
		return entry.key
	}

	data, err := os.ReadFile(apiKeyPath(baseURL))
	if err == nil {
		if key := strings.TrimSpace(string(data)); key != "" {
			mu.Lock()
			cache[baseURL] = &apiKeyEntry{key: key}
			mu.Unlock()
			return key
		}
	}

	if resolver != nil {
		if key := resolver(baseURL); key != "" {
			mu.Lock()
			cache[baseURL] = &apiKeyEntry{key: key}
			mu.Unlock()
			return key
		}
	}
	return ""
}

// SetAPIKey persists the API key for the given server URL to disk and
// updates the in-memory cache. Pass an empty key to clear.
func SetAPIKey(baseURL, apiKey string) error {
	path := apiKeyPath(baseURL)

	mu.Lock()
	defer mu.Unlock()

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
