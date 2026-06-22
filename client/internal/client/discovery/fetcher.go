package discovery

import (
	"encoding/json"
	"io"
	"net/http"
	"strings"

	"revit-cli/internal/models"
)

// SchemaFetcher fetches command schemas from the bridge server's
// GET /api/commands endpoint. Supports ETag/If-None-Match for efficient
// re-fetching and falls back to stale cache on network failure.
// Mirrors C# RevitCliClient.Discovery.SchemaFetcher.
type SchemaFetcher struct {
	baseURL   string
	client    *http.Client
	cache     *SchemaCache
	lastEtag  string
}

// NewSchemaFetcher creates a fetcher for the given server URL.
func NewSchemaFetcher(baseURL string, client *http.Client) *SchemaFetcher {
	return &SchemaFetcher{
		baseURL: baseURL,
		client:  client,
		cache:   NewSchemaCache(baseURL),
	}
}

// Fetch retrieves the schema from the bridge. Returns the cached version if
// available and not expired. Falls back to stale cache on network error.
// Uses ETag/If-None-Match to avoid re-downloading unchanged schemas.
// Mirrors C# SchemaFetcher.FetchAsync.
func (f *SchemaFetcher) Fetch(forceRefresh bool) *models.CommandSchema {
	if !forceRefresh {
		if cached := f.cache.Load(); cached != nil {
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
		// Schema unchanged — return cached version.
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
