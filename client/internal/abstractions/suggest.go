package abstractions

import (
	"sort"
	"strings"
)

// SuggestCandidate is one command eligible for unknown-name suggestions:
// its primary name and its aliases.
type SuggestCandidate struct {
	Name    string
	Aliases []string
}

// Levenshtein returns the edit distance between a and b.
func Levenshtein(a, b string) int {
	ar := []rune(a)
	br := []rune(b)
	if len(ar) == 0 {
		return len(br)
	}
	if len(br) == 0 {
		return len(ar)
	}

	prev := make([]int, len(br)+1)
	curr := make([]int, len(br)+1)
	for j := range prev {
		prev[j] = j
	}

	for i := 1; i <= len(ar); i++ {
		curr[0] = i
		for j := 1; j <= len(br); j++ {
			cost := 1
			if ar[i-1] == br[j-1] {
				cost = 0
			}
			curr[j] = minInt(prev[j]+1, curr[j-1]+1, prev[j-1]+cost)
		}
		prev, curr = curr, prev
	}
	return prev[len(br)]
}

func minInt(values ...int) int {
	best := values[0]
	for _, v := range values[1:] {
		if v < best {
			best = v
		}
	}
	return best
}

// SuggestNames returns up to three candidate names closest to the typed
// name, scored by the best edit distance across each candidate's name and
// aliases. Candidates farther than half the typed name's length are
// skipped, so garbage input gets no suggestions. Ties break alphabetically.
func SuggestNames(typed string, candidates []SuggestCandidate) []string {
	if typed == "" {
		return nil
	}
	limit := len([]rune(typed)) / 2

	type scored struct {
		name string
		dist int
	}
	var matches []scored
	for _, c := range candidates {
		best := Levenshtein(typed, c.Name)
		for _, alias := range c.Aliases {
			if d := Levenshtein(typed, alias); d < best {
				best = d
			}
		}
		if best <= limit {
			matches = append(matches, scored{name: c.Name, dist: best})
		}
	}

	sort.Slice(matches, func(i, j int) bool {
		if matches[i].dist != matches[j].dist {
			return matches[i].dist < matches[j].dist
		}
		return strings.Compare(matches[i].name, matches[j].name) < 0
	})

	if len(matches) > 3 {
		matches = matches[:3]
	}
	names := make([]string, len(matches))
	for i, m := range matches {
		names[i] = m.name
	}
	return names
}

// JoinSuggestions renders suggestion names as "a, b, or c" for an error
// message. It returns an empty string when names is empty.
func JoinSuggestions(names []string) string {
	switch len(names) {
	case 0:
		return ""
	case 1:
		return names[0]
	case 2:
		return names[0] + " or " + names[1]
	default:
		return strings.Join(names[:len(names)-1], ", ") + ", or " + names[len(names)-1]
	}
}
