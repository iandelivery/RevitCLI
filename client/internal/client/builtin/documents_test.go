package builtin

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"revit-cli/internal/instance"
)

func TestEnrichInstancesAndDisplay(t *testing.T) {
	healthy := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/api/documents" {
			http.NotFound(w, r)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(documentsResponse{
			Documents: []documentEntry{
				{Title: "A", Path: strPtr(`C:\a.rvt`), IsActive: true},
				{Title: "B", Path: strPtr(`C:\b.rvt`)},
			},
			ActivePath: strPtr(`C:\a.rvt`),
		})
	}))
	defer healthy.Close()
	healthyURL := healthy.URL
	healthyPort := portFromURL(t, healthyURL)

	slow := httptest.NewServer(http.HandlerFunc(func(_ http.ResponseWriter, _ *http.Request) {
		time.Sleep(documentsTimeout + 2*time.Second)
	}))
	defer slow.Close()
	slowPort := portFromURL(t, slow.URL)

	unauthorized := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		http.Error(w, "unauthorized", http.StatusUnauthorized)
	}))
	defer unauthorized.Close()
	unauthPort := portFromURL(t, unauthorized.URL)

	instances := []instance.InstanceInfo{
		{Pid: 1, Version: 2022, Port: healthyPort},
		{Pid: 2, Version: 2019, Port: slowPort},
		{Pid: 3, Version: 2021, Port: unauthPort},
	}

	enriched := enrichInstances(context.Background(), instances)
	if len(enriched) != 3 {
		t.Fatalf("len(enriched) = %d, want 3", len(enriched))
	}

	byPid := map[int]enrichedInstance{}
	for _, e := range enriched {
		byPid[e.Pid] = e
	}

	healthyInst := byPid[1]
	if !healthyInst.Reachable {
		t.Fatal("healthy instance should be reachable")
	}
	if len(healthyInst.Documents) != 2 {
		t.Fatalf("documents = %d, want 2", len(healthyInst.Documents))
	}
	if got := documentDisplay(healthyInst); got != `C:\a.rvt (2 open)` {
		t.Fatalf("healthy display = %q, want %q", got, `C:\a.rvt (2 open)`)
	}

	slowInst := byPid[2]
	if slowInst.Reachable {
		t.Fatal("slow instance should be unreachable")
	}
	if got := documentDisplay(slowInst); got != "- (unreachable)" {
		t.Fatalf("slow display = %q, want %q", got, "- (unreachable)")
	}
	if slowInst.Version != 2019 {
		t.Fatalf("slow instance Version = %d, want 2019", slowInst.Version)
	}

	unauthInst := byPid[3]
	if unauthInst.Reachable {
		t.Fatal("401 instance should be unreachable")
	}
	if got := documentDisplay(unauthInst); got != "- (unreachable)" {
		t.Fatalf("401 display = %q, want %q", got, "- (unreachable)")
	}
}

func TestDocumentDisplayCases(t *testing.T) {
	cases := []struct {
		name string
		inst enrichedInstance
		want string
	}{
		{
			name: "single active document",
			inst: enrichedInstance{
				Reachable:  true,
				ActivePath: strPtr(`C:\only.rvt`),
				Documents:  []documentEntry{{Title: "Only", Path: strPtr(`C:\only.rvt`), IsActive: true}},
			},
			want: `C:\only.rvt`,
		},
		{
			name: "reachable, no documents (home screen)",
			inst: enrichedInstance{Reachable: true},
			want: "-",
		},
		{
			name: "reachable, documents open but none active",
			inst: enrichedInstance{
				Reachable: true,
				Documents: []documentEntry{
					{Title: "Unsaved", Path: nil},
				},
			},
			want: "- (1 open)",
		},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			if got := documentDisplay(tc.inst); got != tc.want {
				t.Fatalf("documentDisplay = %q, want %q", got, tc.want)
			}
		})
	}
}

func strPtr(s string) *string { return &s }

func portFromURL(t *testing.T, rawURL string) int {
	t.Helper()
	trimmed := strings.TrimPrefix(strings.TrimPrefix(rawURL, "http://"), "https://")
	parts := strings.Split(trimmed, ":")
	if len(parts) < 2 {
		t.Fatalf("cannot parse port from %q", rawURL)
	}
	var port int
	for _, c := range parts[1] {
		if c < '0' || c > '9' {
			t.Fatalf("cannot parse port from %q", rawURL)
		}
		port = port*10 + int(c-'0')
	}
	return port
}
