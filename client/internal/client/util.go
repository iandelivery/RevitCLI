package client

import (
	"fmt"
	"os"
)

func warnPrintln(msg string) {
	fmt.Fprintln(os.Stderr, msg)
}
