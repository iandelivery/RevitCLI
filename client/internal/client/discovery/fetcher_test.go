package discovery

import (
	"net/http"
	"net/http/httptest"
	"testing"
)

func newAuthTestFetcher(t *testing.T, status int) (*SchemaFetcher, func()) {
	t.Helper()
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(status)
		_, _ = w.Write([]byte(`{"error":"Unauthorized. Provide 'Authorization: Bearer <api_key>' header.","auth_enabled":true}`))
	}))
	f := NewSchemaFetcher(srv.URL, srv.Client())
	return f, srv.Close
}

func TestFetchCommand_UnauthorizedSetsAuthRejected(t *testing.T) {
	f, closeSrv := newAuthTestFetcher(t, http.StatusUnauthorized)
	defer closeSrv()

	if def := f.FetchCommand("create_wall"); def != nil {
		t.Fatalf("expected no command definition on 401, got %v", def)
	}
	if !f.AuthRejected() {
		t.Fatal("expected AuthRejected to be true after a 401")
	}
}

func TestFetch_UnauthorizedSetsAuthRejectedAndReturnsNoSchema(t *testing.T) {
	f, closeSrv := newAuthTestFetcher(t, http.StatusUnauthorized)
	defer closeSrv()

	if schema := f.Fetch(false); schema != nil {
		t.Fatalf("expected no schema on 401 without a stale cache, got %v", schema)
	}
	if !f.AuthRejected() {
		t.Fatal("expected AuthRejected to be true after a 401")
	}
}

func TestFetchCommand_ServerErrorDoesNotSetAuthRejected(t *testing.T) {
	f, closeSrv := newAuthTestFetcher(t, http.StatusInternalServerError)
	defer closeSrv()

	if def := f.FetchCommand("create_wall"); def != nil {
		t.Fatalf("expected no command definition on 500, got %v", def)
	}
	if f.AuthRejected() {
		t.Fatal("expected AuthRejected to stay false on a 500")
	}
}

func TestFetchCommand_NotFoundDoesNotSetAuthRejected(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	}))
	defer srv.Close()
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())

	f := NewSchemaFetcher(srv.URL, srv.Client())
	if def := f.FetchCommand("missing_command"); def != nil {
		t.Fatalf("expected no command definition on 404, got %v", def)
	}
	if f.AuthRejected() {
		t.Fatal("expected AuthRejected to stay false on a 404")
	}
}
