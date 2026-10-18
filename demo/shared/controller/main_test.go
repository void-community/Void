package main

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestWriteLiveHtmlRendersFailurePage(t *testing.T) {
	recorder := httptest.NewRecorder()
	server := &Server{}

	server.writeSessionFailedHtml(recorder, &Session{Id: "abc", FailureMessage: "client <crashed>"})

	body := recorder.Body.String()
	if recorder.Code != http.StatusServiceUnavailable {
		t.Fatalf("unexpected status %d", recorder.Code)
	}

	if strings.Contains(body, "%!") {
		t.Fatal("template has an unescaped format verb")
	}

	for _, expected := range []string{"client &lt;crashed&gt;", `id="retryLink"`, `const sessionId = "abc";`, "status.failed", "height: 100%;"} {
		if !strings.Contains(body, expected) {
			t.Fatalf("expected page to contain %q", expected)
		}
	}
}

func TestTruncateFailureMessage(t *testing.T) {
	if actual := truncateFailureMessage("line one\n  line two"); actual != "line one line two" {
		t.Fatalf("unexpected message %q", actual)
	}

	if actual := truncateFailureMessage(strings.Repeat("x", failureMessageMaximumLength+10)); len(actual) != failureMessageMaximumLength+len("…") {
		t.Fatalf("unexpected length %d", len(actual))
	}
}
