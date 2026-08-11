package discovery

import (
	"encoding/json"
	"io"
	"log"
	"net/http"
	"strings"
	"sync"

	"revit-cli/internal/models"
)

// SchemaFetcher fetches command schemas from the bridge server's
// GET /api/commands endpoint. Supports ETag/If-None-Match for efficient
// re-fetching and falls back to stale cache on network failure.
// Mirrors C# RevitCliClient.Discovery.SchemaFetcher.
type SchemaFetcher struct {
	baseURL       string
	client        *http.Client
	cache         *SchemaCache
	lastEtag      string
	cachedVersion string // last known bridge version for version-change detection
}

// NewSchemaFetcher creates a fetcher for the given server URL.
func NewSchemaFetcher(baseURL string, client *http.Client) *SchemaFetcher {
	return &SchemaFetcher{
		baseURL: baseURL,
		client:  client,
		cache:   NewSchemaCache(baseURL),
	}
}

// commandCacheEntry holds a cached command definition and its ETag.
type commandCacheEntry struct {
	def  *models.CommandDef
	etag string
}

// commandCache provides in-process caching of individual command definitions
// fetched via /api/commands/{name}. Keyed by baseURL+":"+commandName so
// multiple server targets don't collide. Entries live for the process
// lifetime — no TTL needed since bridge schemas do not change mid-process.
var commandCache sync.Map

// FetchCommand fetches a single command definition via /api/commands/{name}.
// Uses the in-process cache to avoid redundant HTTP calls within the same
// process. Returns nil if the command is not found or the request fails
// (caller should fall back to Fetch for alias resolution or old bridges
// that lack the per-command endpoint).
func (f *SchemaFetcher) FetchCommand(name string) *models.CommandDef {
	cacheKey := f.baseURL + ":" + name
	if cached, ok := commandCache.Load(cacheKey); ok {
		return cached.(*commandCacheEntry).def
	}

	req, err := http.NewRequest(http.MethodGet, f.baseURL+"/api/commands/"+name, nil)
	if err != nil {
		return nil
	}

	resp, err := f.client.Do(req)
	if err != nil {
		return nil
	}
	defer resp.Body.Close()

	// 404 means the command doesn't exist — return nil so the caller can
	// try the full schema fallback (the user may have typed an alias that
	// the server didn't resolve, or the command genuinely doesn't exist).
	if resp.StatusCode == http.StatusNotFound {
		return nil
	}

	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil
	}

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil
	}

	var def models.CommandDef
	if err := json.Unmarshal(body, &def); err != nil {
		return nil
	}

	etagHeader := strings.Trim(resp.Header.Get("ETag"), `"`)
	commandCache.Store(cacheKey, &commandCacheEntry{def: &def, etag: etagHeader})
	return &def
}

// Fetch retrieves the schema from the bridge. Returns the cached version if
// available and not expired. Falls back to stale cache on network error.
// Uses ETag/If-None-Match to avoid re-downloading unchanged schemas.
// Detects bridge version changes and forces a re-fetch when the version differs.
// Mirrors C# SchemaFetcher.FetchAsync.
func (f *SchemaFetcher) Fetch(forceRefresh bool) *models.CommandSchema {
	if !forceRefresh {
		// Check cache with version awareness — if the bridge was upgraded,
		// the cached schema is stale even if TTL hasn't expired.
		if cached := f.cache.LoadWithVersion(f.cachedVersion); cached != nil {
			return cached
		}
	}

	req, err := http.NewRequest(http.MethodGet, f.baseURL+"/api/commands", nil)
	if err != nil {
		return f.cache.LoadStale()
	}

	// Send ETag if we have one from a previous response or cache.
	etag := f.lastEtag
	if etag == "" {
		etag = f.cache.LoadEtag()
	}
	if etag != "" {
		req.Header.Set("If-None-Match", `"`+etag+`"`)
	}

	resp, err := f.client.Do(req)
	if err != nil {
		return f.cache.LoadStale()
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusNotModified {
		// Schema unchanged — return cached version and refresh TTL.
		_ = f.cache.Touch()
		if cached := f.cache.Load(); cached != nil {
			return cached
		}
		return f.cache.LoadStale()
	}

	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		body, err := io.ReadAll(resp.Body)
		if err != nil {
			return f.cache.LoadStale()
		}
		var schema models.CommandSchema
		if err := json.Unmarshal(body, &schema); err != nil {
			return f.cache.LoadStale()
		}

		// Track the bridge version for future version-change detection.
		if schema.ServerInfo != nil && schema.ServerInfo.BridgeVersion != "" {
			if f.cachedVersion != "" && f.cachedVersion != schema.ServerInfo.BridgeVersion {
				log.Printf("[fetch] Bridge version changed: %s -> %s, cache invalidated",
					f.cachedVersion, schema.ServerInfo.BridgeVersion)
			}
			f.cachedVersion = schema.ServerInfo.BridgeVersion
		}

		// Store ETag for future requests.
		etagHeader := resp.Header.Get("ETag")
		etagHeader = strings.Trim(etagHeader, `"`)
		if etagHeader != "" {
			f.lastEtag = etagHeader
			f.cache.SaveEtag(etagHeader)
		}

		f.cache.Save(&schema)
		return &schema
	}

	return f.cache.LoadStale()
}
