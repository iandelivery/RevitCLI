package abstractions

import "testing"

var suggestPool = []SuggestCandidate{
	{Name: "create_wall", Aliases: []string{"wall_create"}},
	{Name: "create_walls"},
	{Name: "delete_element"},
	{Name: "get_elements", Aliases: []string{"elements"}},
}

func TestSuggestNames_NearMissRankedFirst(t *testing.T) {
	got := SuggestNames("create-wal", suggestPool)
	if len(got) == 0 || got[0] != "create_wall" {
		t.Fatalf("got %v, want create_wall first", got)
	}
	if len(got) < 2 || got[1] != "create_walls" {
		t.Fatalf("got %v, want create_walls second", got)
	}
}

func TestSuggestNames_AliasScores(t *testing.T) {
	got := SuggestNames("wall_creat", suggestPool)
	if len(got) == 0 || got[0] != "create_wall" {
		t.Fatalf("alias must surface create_wall, got %v", got)
	}
}

func TestSuggestNames_GarbageGetsNoSuggestions(t *testing.T) {
	if got := SuggestNames("zzzzzzzz", suggestPool); len(got) != 0 {
		t.Fatalf("garbage input must return no suggestions, got %v", got)
	}
}

func TestSuggestNames_EmptyTypedName(t *testing.T) {
	if got := SuggestNames("", suggestPool); got != nil {
		t.Fatalf("empty input must return nil, got %v", got)
	}
}

func TestSuggestNames_LimitsToThree(t *testing.T) {
	pool := []SuggestCandidate{
		{Name: "create_wall"},
		{Name: "create_walls"},
		{Name: "create_wall_x"},
		{Name: "create_wall_y"},
	}
	got := SuggestNames("create_wall", pool)
	if len(got) != 3 {
		t.Fatalf("got %d suggestions, want 3", len(got))
	}
}

func TestLevenshtein_Distances(t *testing.T) {
	cases := []struct {
		a, b string
		want int
	}{
		{"", "", 0},
		{"abc", "abc", 0},
		{"create_wall", "create_wall", 0},
		{"create-wal", "create_wall", 2},
		{"kitten", "sitting", 3},
		{"", "abc", 3},
	}
	for _, c := range cases {
		if got := Levenshtein(c.a, c.b); got != c.want {
			t.Errorf("Levenshtein(%q, %q) = %d, want %d", c.a, c.b, got, c.want)
		}
	}
}

func TestJoinSuggestions(t *testing.T) {
	cases := []struct {
		names []string
		want  string
	}{
		{nil, ""},
		{[]string{"a"}, "a"},
		{[]string{"a", "b"}, "a or b"},
		{[]string{"a", "b", "c"}, "a, b, or c"},
	}
	for _, c := range cases {
		if got := JoinSuggestions(c.names); got != c.want {
			t.Errorf("JoinSuggestions(%v) = %q, want %q", c.names, got, c.want)
		}
	}
}
