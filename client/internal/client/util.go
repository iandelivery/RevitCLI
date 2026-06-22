package client

import (
	"fmt"
	"os"
)

// warnPrintln writes a warning to stderr. Used by OutputProcessor.
func warnPrintln(msg string) {
	fmt.Fprintln(os.Stderr, msg)
}
