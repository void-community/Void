package main

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"html"
	"io"
	"log"
	"net"
	"net/http"
	"net/http/httputil"
	"net/url"
	"os"
	"os/exec"
	"os/signal"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
)

const (
	dockerQuickCommandTimeout   = time.Minute
	dockerComposeCommandTimeout = 5 * time.Minute
	dockerBuildCommandTimeout   = 30 * time.Minute
	failureMessageMaximumLength = 300
	clientWatchInterval         = 15 * time.Second
	clientMaximumRelaunchCount  = 3
)

type LogPrefixWriter struct {
	Prefix string
	Buffer strings.Builder
	Mutex  sync.Mutex
}

type Session struct {
	Id                  string
	SanitizedId         string
	DashboardHost       string
	ClientHost          string
	VoidHost            string
	CreatedUtc          time.Time
	ExpiresUtc          time.Time
	DeleteTimer         *time.Timer
	Ready               bool
	Failed              bool
	FailureMessage      string
	ClientRelaunchCount int
	Cancel              context.CancelFunc
}

type Server struct {
	ListenAddress          string
	SessionTtl             time.Duration
	SessionStartTimeout    time.Duration
	FailedSessionRetention time.Duration
	RefreshInterval        time.Duration
	BackendRecreateGrace   time.Duration
	RedirectLogs           bool

	RootContext      context.Context
	UpstreamClient   *http.Client
	ImagesReady      chan struct{}
	ImagesReadyOnce  sync.Once
	RefreshRequested chan struct{}

	SessionImagesBuilt bool

	Sessions      map[string]*Session
	SessionsMutex sync.RWMutex

	Backend      BackendState
	BackendMutex sync.Mutex
}

type clientGameStatus struct {
	State          string `json:"state"`
	OperationId    int64  `json:"operationId"`
	OperationState string `json:"operationState"`
	Error          string `json:"error"`
}

func main() {
	rootContext, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	server := &Server{
		SessionTtl:             time.Duration(getEnvInt("SESSION_TTL_SECONDS", 7200)) * time.Second,
		SessionStartTimeout:    time.Duration(getEnvInt("SESSION_START_TIMEOUT_SECONDS", 1200)) * time.Second,
		FailedSessionRetention: 10 * time.Minute,
		RefreshInterval:        time.Duration(getEnvInt("REFRESH_INTERVAL_SECONDS", 600)) * time.Second,
		BackendRecreateGrace:   time.Duration(getEnvInt("BACKEND_RECREATE_GRACE_SECONDS", 3600)) * time.Second,
		ListenAddress:          getEnvString("LISTEN_ADDRESS", "0.0.0.0:80"),
		RedirectLogs:           getEnvBool("REDIRECT_LOGS", false),
		RootContext:            rootContext,
		UpstreamClient:         newUpstreamClient(),
		ImagesReady:            make(chan struct{}),
		RefreshRequested:       make(chan struct{}, 1),
		Sessions:               map[string]*Session{},
	}

	if output, err := runDockerCommand(rootContext, dockerQuickCommandTimeout, "network", "prune", "-f"); err != nil {
		log.Fatalf("Failed to prune docker networks: %v. Output: %s", err, string(output))
	}

	server.cleanupOrphanSessions(rootContext)

	go server.runRefreshLoop(rootContext)

	mux := http.NewServeMux()
	mux.HandleFunc("/", func(writer http.ResponseWriter, request *http.Request) {
		if request.URL.Path != "/" {
			http.NotFound(writer, request)
			return
		}

		server.handleNewSession(writer, request)
	})
	mux.HandleFunc("/status/", server.handleStatus)
	mux.HandleFunc("/session/", server.handleSession)

	httpServer := &http.Server{
		Addr:              server.ListenAddress,
		Handler:           withBasicLogging(mux),
		ReadHeaderTimeout: 10 * time.Second,
	}

	go func() {
		<-rootContext.Done()

		shutdownContext, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()

		_ = httpServer.Shutdown(shutdownContext)
	}()

	log.Printf("Listening on http://%s", server.ListenAddress)
	log.Printf("Session TTL: %s", server.SessionTtl)
	log.Printf("Session start timeout: %s", server.SessionStartTimeout)
	log.Printf("Refresh interval: %s", server.RefreshInterval)
	log.Printf("Backend recreate grace: %s", server.BackendRecreateGrace)
	log.Printf("Redirect logs: %v", server.RedirectLogs)

	if err := httpServer.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
		log.Fatalf("HTTP server failed: %v", err)
	}
}

func (server *Server) handleNewSession(writer http.ResponseWriter, request *http.Request) {
	sessionId, err := createSessionId()
	if err != nil {
		http.Error(writer, "Failed to generate session id", http.StatusInternalServerError)
		return
	}

	sessionContext, cancel := context.WithCancel(server.RootContext)
	session := &Session{Id: sessionId, Cancel: cancel}

	session.SanitizedId = sanitizeForDockerName(sessionId)
	session.CreatedUtc = time.Now().UTC()
	session.ExpiresUtc = session.CreatedUtc.Add(server.SessionTtl)

	server.SessionsMutex.Lock()
	server.Sessions[session.Id] = session
	server.SessionsMutex.Unlock()
	http.Redirect(writer, request, "/session/"+session.Id+"/", http.StatusTemporaryRedirect)

	go func() {
		startContext, cancelStart := context.WithTimeout(sessionContext, server.SessionStartTimeout)
		defer cancelStart()

		log.Printf("Creating new session %s (TTL: %s)", session.Id, server.SessionTtl)

		if err := server.waitForImages(startContext); err != nil {
			server.markSessionFailed(session.Id, fmt.Sprintf("Session images are not available: %v", err))
			return
		}

		if err := server.startSessionContainers(startContext, sessionContext, session); err != nil {
			log.Printf("Failed to start session containers for session %s: %v", session.Id, err)
			server.markSessionFailed(session.Id, err.Error())
			return
		}

		deleteTimer := time.AfterFunc(server.SessionTtl, func() {
			err := server.deleteSession(session.Id)
			if err != nil {
				log.Printf("Failed to delete session on timer %s: %v", session.Id, err)
			}
		})
		server.SessionsMutex.Lock()
		if session.Failed {
			server.SessionsMutex.Unlock()
			deleteTimer.Stop()
			return
		}
		session.DeleteTimer = deleteTimer
		session.Ready = true
		server.SessionsMutex.Unlock()

		log.Printf("Session %s created successfully", session.Id)

		server.watchSessionClient(sessionContext, session.Id)
	}()
}

func (server *Server) handleStatus(writer http.ResponseWriter, request *http.Request) {
	sessionId := strings.TrimPrefix(request.URL.Path, "/status/")
	sessionId = strings.Trim(sessionId, "/")
	if sessionId == "" {
		http.NotFound(writer, request)
		return
	}

	server.SessionsMutex.RLock()
	session, ok := server.Sessions[sessionId]
	if ok {
		sessionSnapshot := *session
		session = &sessionSnapshot
	}
	server.SessionsMutex.RUnlock()

	type statusResponse struct {
		Exists          bool   `json:"exists"`
		Ready           bool   `json:"ready"`
		Failed          bool   `json:"failed"`
		Error           string `json:"error,omitempty"`
		BackendUpdating bool   `json:"backendUpdating"`
		SessionId       string `json:"sessionId"`
		SecondsLeft     int64  `json:"secondsLeft"`
	}

	response := statusResponse{
		Exists:    ok,
		SessionId: sessionId,
	}

	if ok {
		secondsLeft := time.Until(session.ExpiresUtc).Seconds()
		if secondsLeft < 0 {
			secondsLeft = 0
		}
		response.SecondsLeft = int64(secondsLeft)
		response.Failed = session.Failed
		response.Error = session.FailureMessage

		if !session.Failed {
			readyValue := server.isSessionReady(session)
			response.Ready = readyValue
			response.BackendUpdating = !readyValue && server.isBackendUpdating()
		}
	}

	writer.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(writer).Encode(response)
}

func (server *Server) handleSession(writer http.ResponseWriter, request *http.Request) {
	// Path: /session/<sessionId>[/<rest...>]
	path := strings.TrimPrefix(request.URL.Path, "/session/")
	firstSlashIndex := strings.IndexByte(path, '/')

	var sessionId string
	var restPath string

	switch firstSlashIndex {
	case -1:
		sessionId = path
		restPath = ""
	case 0:
		http.NotFound(writer, request)
		return
	default:
		sessionId = path[:firstSlashIndex]
		restPath = path[firstSlashIndex:] // includes leading '/'
	}

	if sessionId == "" {
		http.NotFound(writer, request)
		return
	}

	server.SessionsMutex.RLock()
	session, ok := server.Sessions[sessionId]
	if ok {
		sessionSnapshot := *session
		session = &sessionSnapshot
	}
	server.SessionsMutex.RUnlock()

	if !ok {
		server.writeSessionExpiredHtml(writer)
		return
	}

	if session.Failed {
		server.writeSessionFailedHtml(writer, session)
		return
	}

	targetUrl := &url.URL{
		Scheme: "http",
		Host:   session.DashboardHost,
	}

	reverseProxy := httputil.NewSingleHostReverseProxy(targetUrl)

	originalDirector := reverseProxy.Director
	reverseProxy.Director = func(proxyRequest *http.Request) {
		originalDirector(proxyRequest)

		proxyRequest.URL.Path = restPath
		proxyRequest.URL.RawPath = restPath
		proxyRequest.Host = targetUrl.Host
		proxyRequest.Header.Set("X-Path-Prefix", "session/"+sessionId)
	}

	reverseProxy.ErrorHandler = func(proxyWriter http.ResponseWriter, proxyRequest *http.Request, proxyError error) {
		log.Printf("Session %s upstream not reachable yet: %v", sessionId, proxyError)
		server.writeSessionStartingHtml(proxyWriter, sessionId)
	}

	if !server.isSessionReady(session) {
		reverseProxy.ErrorHandler(writer, request, fmt.Errorf("session %s not ready", sessionId))
	} else {
		reverseProxy.ServeHTTP(writer, request)
	}
}

func (server *Server) isSessionReady(session *Session) bool {
	if !session.Ready || session.Failed {
		return false
	}

	httpClient := &http.Client{
		Timeout: time.Second,
	}

	probe := func(host string, path string) bool {
		if strings.TrimSpace(host) == "" {
			log.Printf("Host for probing is still empty, skipping probe")
			return false
		}

		request, err := http.NewRequest(http.MethodGet, "http://"+host+path, nil)
		if err != nil {
			log.Printf("Failed to create probe request for host %s: %v", host, err)
			return false
		}

		response, err := httpClient.Do(request)
		if err != nil {
			log.Printf("Probe request to host %s failed: %v", host, err)
			return false
		}

		_ = response.Body.Close()

		log.Printf("Probed %s: %d", host, response.StatusCode)

		return response.StatusCode == http.StatusOK
	}

	return probe(session.DashboardHost, "/") && probe(session.VoidHost, "/") && probe(session.ClientHost, "/api/health")
}

func (server *Server) writeSessionStartingHtml(writer http.ResponseWriter, sessionId string) {
	server.writeLiveHtml(writer, http.StatusOK, "Starting session", "Your session container is starting", "/session/"+sessionId+"/")
}

func (server *Server) writeSessionFailedHtml(writer http.ResponseWriter, session *Session) {
	server.writeLiveHtml(writer, http.StatusServiceUnavailable, "Session failed", session.FailureMessage, "/session/"+session.Id+"/")
}

func (server *Server) writeSessionExpiredHtml(writer http.ResponseWriter) {
	server.writeLiveHtml(writer, http.StatusNotFound, "Session expired", "This session no longer exists", "/")
}

func (server *Server) writeLiveHtml(writer http.ResponseWriter, statusCode int, title string, subtitle string, retryPath string) {
	writer.Header().Set("Content-Type", "text/html; charset=utf-8")
	writer.WriteHeader(statusCode)

	sessionId := ""
	trimmed := strings.TrimPrefix(retryPath, "/session/")
	if trimmed != retryPath {
		trimmed = strings.TrimLeft(trimmed, "/")

		firstSlashIndex := strings.IndexByte(trimmed, '/')
		if firstSlashIndex == -1 {
			sessionId = trimmed
		} else if firstSlashIndex > 0 {
			sessionId = trimmed[:firstSlashIndex]
		}
	}

	sessionIdJs := strconv.Quote(sessionId)
	retryPathJs := strconv.Quote(retryPath)

	titleHtml := html.EscapeString(title)
	subtitleHtml := html.EscapeString(subtitle)

	page := fmt.Sprintf(`<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover"/>
  <title>%s</title>
  <style>
    :root {
      --bg-color: #0f0b1e;
      --card-bg: #1a162d;
      --card-border: #4c2f7a;
      --text-main: #e9d5ff;
      --text-muted: #a39eb5;
      --accent: #d946ef; /* Neon Purple/Pink */
      --accent-glow: rgba(217, 70, 239, 0.4);
      --pill-bg: #281f3f;
    }

    html, body {
      height: 100%%;
      margin: 0;
      background-color: var(--bg-color);
      /* Subtle purple gradient background */
      background-image: radial-gradient(circle at 50%% 0%%, #2e1065 0%%, #0f0b1e 75%%);
      color: var(--text-main);
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
      -webkit-font-smoothing: antialiased;
    }

    .wrap {
      min-height: 100%%;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 20px;
      box-sizing: border-box;
    }

    .card {
      width: 100%%;
      max-width: 680px;
      background: var(--card-bg);
      border: 1px solid var(--card-border);
      border-radius: 20px;
      padding: 32px;
      /* Modern purple shadow */
      box-shadow: 0 10px 40px -10px rgba(0, 0, 0, 0.5), 0 0 20px -5px rgba(124, 58, 237, 0.2);
      backdrop-filter: blur(10px);
    }

    h1 {
      margin: 0 0 16px 0;
      font-size: 26px;
      font-weight: 700;
      color: #fff;
      letter-spacing: -0.5px;
    }

    p {
      margin: 0 0 20px 0;
      line-height: 1.6;
      color: var(--text-muted);
      font-size: 16px;
    }

    .row {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
      align-items: center;
    }

    .pill {
      display: inline-flex;
      align-items: center;
      gap: 12px;
      padding: 10px 16px;
      border-radius: 999px;
      background: var(--pill-bg);
      border: 1px solid var(--card-border);
      font-weight: 500;
      font-size: 14px;
      color: var(--text-main);
      transition: border-color 0.2s;
    }

    .dot {
      width: 10px;
      height: 10px;
      border-radius: 50%%;
      background: var(--accent);
      box-shadow: 0 0 10px var(--accent);
      animation: pulse 2s infinite cubic-bezier(0.4, 0, 0.6, 1);
    }

    .retry-link {
      color: var(--accent);
      font-weight: 600;
      font-size: 14px;
    }

    @keyframes pulse {
      0%% { opacity: 1; transform: scale(1); box-shadow: 0 0 0 0 var(--accent-glow); }
      50%% { opacity: .7; transform: scale(1.1); box-shadow: 0 0 0 6px rgba(0,0,0,0); }
      100%% { opacity: 1; transform: scale(1); box-shadow: 0 0 0 0 rgba(0,0,0,0); }
    }

    .footer-text {
      margin-top: 24px;
      font-size: 13px;
      color: #6b7280;
      border-top: 1px solid rgba(255,255,255,0.05);
      padding-top: 16px;
    }

    /* Mobile Tweaks */
    @media (max-width: 480px) {
      .card { padding: 24px; }
      h1 { font-size: 22px; }
      p { font-size: 15px; }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="card">
      <h1>%s</h1>
      <p>%s</p>

      <div class="row">
        <div class="pill">
          <span class="dot"></span>
          <span id="statusText">Checking status…</span>
        </div>
        <a id="retryLink" class="retry-link" href="/" hidden>Start a new session</a>
      </div>

      <p class="footer-text">
        System is auto-checking session connectivity. You will be redirected automatically once the live session is reachable.
      </p>
    </div>
  </div>

<script>
(function() {
  // Go variables injected here
  const retryPath = %s;
  const sessionId = %s;

  const statusTextElement = document.getElementById("statusText");
  const retryLinkElement = document.getElementById("retryLink");
  let stopped = false;

  if (!statusTextElement) {
    console.error("Critical: Status text element missing.");
    return;
  }

  // Direct redirection if no session ID implies immediate retry/login needed
  if (!sessionId && retryPath) {
    location.replace(retryPath);
    return;
  }

  async function tick() {
    try {
      // Added timestamp to prevent aggressive browser caching issues
      const timestamp = new Date().getTime();
      const response = await fetch("/status/" + sessionId + "?t=" + timestamp, {
        cache: "no-store",
        headers: { 'Cache-Control': 'no-cache' }
      });

      if (!response.ok) return;

      const status = await response.json();

      // Session no longer exists -> go home
      if (!status.exists) {
        location.replace("/");
        return;
      }

      // Session failed -> show the reason and stop polling instead of starting another session
      if (status.failed) {
        statusTextElement.textContent = "Session failed: " + (status.error || "unknown error");
        statusTextElement.style.color = "#fca5a5";
        if (retryLinkElement) retryLinkElement.hidden = false;
        stopped = true;
        return;
      }

      // Session is ready -> go to application
      if (status.ready) {
        statusTextElement.textContent = "Session Ready! Redirecting...";
        statusTextElement.style.color = "#a7f3d0"; // Little visual green hint
        location.replace(retryPath);
        return;
      }

      statusTextElement.textContent = status.backendUpdating ? "Updating Minecraft server..." : "Starting environment...";

    } catch(error) {
      console.warn("Status check failed, retrying...", error);
    } finally {
      if (!stopped) setTimeout(tick, 1000);
    }
  }

  tick();
})();
</script>
</body>
</html>`, titleHtml, titleHtml, subtitleHtml, retryPathJs, sessionIdJs)

	_, _ = writer.Write([]byte(page))
}

func (server *Server) deleteSession(sessionId string) error {
	server.SessionsMutex.Lock()
	session, ok := server.Sessions[sessionId]
	if ok {
		delete(server.Sessions, sessionId)
	}
	server.SessionsMutex.Unlock()

	if !ok {
		return nil
	}

	log.Printf("Deleting session %s", session.Id)

	if session.Cancel != nil {
		session.Cancel()
	}

	err := server.stopSession(session)
	if err != nil {
		return err
	}

	log.Printf("Session %s cleanup completed", session.Id)
	server.requestRefreshIfBackendPending()
	return nil
}

// markSessionFailed keeps the session visible with its failure reason (so the page can show it instead of
// silently creating a new session), stops its containers, and drops it after FailedSessionRetention.
func (server *Server) markSessionFailed(sessionId string, message string) {
	server.SessionsMutex.Lock()
	session, ok := server.Sessions[sessionId]
	if !ok || session.Failed {
		server.SessionsMutex.Unlock()
		return
	}

	session.Failed = true
	session.Ready = false
	session.FailureMessage = truncateFailureMessage(message)
	if session.DeleteTimer != nil {
		session.DeleteTimer.Stop()
	}
	session.DeleteTimer = time.AfterFunc(server.FailedSessionRetention, func() {
		server.SessionsMutex.Lock()
		delete(server.Sessions, sessionId)
		server.SessionsMutex.Unlock()
	})
	cancel := session.Cancel
	sessionSnapshot := *session
	server.SessionsMutex.Unlock()

	log.Printf("Session %s failed: %s", sessionId, sessionSnapshot.FailureMessage)

	if cancel != nil {
		cancel()
	}

	go func() {
		if err := server.stopSession(&sessionSnapshot); err != nil {
			log.Printf("Failed to stop failed session %s: %v", sessionId, err)
		}

		server.requestRefreshIfBackendPending()
	}()
}

func (server *Server) activeSessionCount() int {
	server.SessionsMutex.RLock()
	defer server.SessionsMutex.RUnlock()

	count := 0
	for _, session := range server.Sessions {
		if !session.Failed {
			count++
		}
	}

	return count
}

func (server *Server) endActiveSessions(message string) {
	server.SessionsMutex.RLock()
	sessionIds := make([]string, 0, len(server.Sessions))
	for sessionId, session := range server.Sessions {
		if !session.Failed {
			sessionIds = append(sessionIds, sessionId)
		}
	}
	server.SessionsMutex.RUnlock()

	for _, sessionId := range sessionIds {
		server.markSessionFailed(sessionId, message)
	}
}

func (server *Server) stopSession(session *Session) error {
	stopOutput, stopErr := runDockerCommand(context.Background(), dockerComposeCommandTimeout, "compose", "--project-name", session.SanitizedId, "--file", "session.yml", "down", "--remove-orphans", "--volumes")
	if stopErr != nil {
		return fmt.Errorf("docker compose down failed: %v: %s", stopErr, string(stopOutput))
	}
	return nil
}

// cleanupOrphanSessions removes session projects left behind by a previous controller process, whose
// in-memory session map (and with it every delete timer) was lost.
func (server *Server) cleanupOrphanSessions(ctx context.Context) {
	output, err := runDockerCommand(ctx, dockerQuickCommandTimeout, "compose", "ls", "--all", "--format", "json")
	if err != nil {
		log.Printf("Failed to list compose projects for orphan cleanup: %v: %s", err, string(output))
		return
	}

	var projects []struct {
		Name        string `json:"Name"`
		ConfigFiles string `json:"ConfigFiles"`
	}
	if err := json.Unmarshal(output, &projects); err != nil {
		log.Printf("Failed to parse compose projects for orphan cleanup: %v", err)
		return
	}

	for _, project := range projects {
		if project.Name == "demo" || !strings.Contains(project.ConfigFiles, "session.yml") {
			continue
		}

		log.Printf("Removing orphaned session project %s", project.Name)

		if downOutput, downErr := runDockerCommand(ctx, dockerComposeCommandTimeout, "compose", "--project-name", project.Name, "--file", "session.yml", "down", "--remove-orphans", "--volumes"); downErr != nil {
			log.Printf("Failed to remove orphaned session project %s: %v: %s", project.Name, downErr, string(downOutput))
		}
	}
}

func (server *Server) startSessionContainers(ctx context.Context, sessionContext context.Context, session *Session) error {
	log.Printf("Session %s: Starting containers", session.Id)

	startOutputBytes, startError := runDockerCommand(ctx, dockerComposeCommandTimeout, "compose", "--project-name", session.SanitizedId, "--file", "session.yml", "up", "--detach", "--no-build")
	if startError != nil {
		log.Printf("Session %s: Failed to start containers with docker compose: %v, output: %s", session.Id, startError, string(startOutputBytes))
		return fmt.Errorf("failed to start containers with docker compose: %v: %s", startError, string(startOutputBytes))
	}

	log.Printf("Session %s: Containers started with docker compose", session.Id)

	containerIdBytes, listContainersError := dockerCommand(ctx, dockerQuickCommandTimeout, "compose", "--project-name", session.SanitizedId, "--file", "session.yml", "ps", "-q")
	if listContainersError != nil {
		log.Printf("Session %s: Failed to list compose containers: %v", session.Id, listContainersError)
		return fmt.Errorf("failed to list compose containers: %v", listContainersError)
	}

	containerIds := strings.Fields(string(containerIdBytes))
	if len(containerIds) == 0 {
		log.Printf("Session %s: No containers returned by docker compose ps -q", session.Id)
		return fmt.Errorf("no containers returned by docker compose ps -q")
	}

	inspectArguments := []string{
		"inspect",
		"--format",
		"{{.Name}} {{index .Config.Labels \"com.docker.compose.service\"}}",
	}
	inspectArguments = append(inspectArguments, containerIds...)

	inspectOutputBytes, inspectError := runDockerCommand(ctx, dockerQuickCommandTimeout, inspectArguments...)
	if inspectError != nil {
		return fmt.Errorf("failed to inspect compose containers: %v: %s", inspectError, string(inspectOutputBytes))
	}

	dashboardContainerName := ""
	clientContainerName := ""
	voidContainerName := ""

	inspectLines := strings.SplitSeq(strings.TrimSpace(string(inspectOutputBytes)), "\n")
	for inspectLine := range inspectLines {
		inspectFields := strings.Fields(inspectLine)
		if len(inspectFields) < 2 {
			continue
		}

		containerName := strings.TrimPrefix(strings.TrimSpace(inspectFields[0]), "/")
		composeServiceName := strings.TrimSpace(inspectFields[1])

		switch composeServiceName {
		case "dashboard":
			dashboardContainerName = containerName
		case "client":
			clientContainerName = containerName
		case "void":
			voidContainerName = containerName
		}

		if server.RedirectLogs {
			if containerName == "" {
				continue
			}

			server.streamContainerLogs(sessionContext, containerName)
		}
	}

	if strings.TrimSpace(dashboardContainerName) == "" {
		return fmt.Errorf("dashboard container name not found")
	}

	if strings.TrimSpace(clientContainerName) == "" {
		return fmt.Errorf("client container name not found")
	}

	if strings.TrimSpace(voidContainerName) == "" {
		return fmt.Errorf("void container name not found")
	}

	server.SessionsMutex.Lock()
	session.DashboardHost = dashboardContainerName
	session.ClientHost = clientContainerName
	session.VoidHost = voidContainerName
	server.SessionsMutex.Unlock()

	if err := server.startAndJoinPortableMinecraftClient(ctx, clientContainerName, createMinecraftUsername(session.SanitizedId)); err != nil {
		return err
	}

	return nil
}

// watchSessionClient relaunches the game when it crashes or is quit, and fails the session after
// clientMaximumRelaunchCount relaunches. It returns when the session context ends.
func (server *Server) watchSessionClient(ctx context.Context, sessionId string) {
	ticker := time.NewTicker(clientWatchInterval)
	defer ticker.Stop()

	httpClient := &http.Client{Timeout: 5 * time.Second}

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
		}

		server.SessionsMutex.RLock()
		session, ok := server.Sessions[sessionId]
		if !ok || session.Failed {
			server.SessionsMutex.RUnlock()
			return
		}
		clientHost := session.ClientHost
		sanitizedId := session.SanitizedId
		server.SessionsMutex.RUnlock()

		clientApiUrl := &url.URL{Scheme: "http", Host: net.JoinHostPort(clientHost, "80"), Path: "/api/game/status"}
		responseStatusCode, responseBody, err := requestPortableMinecraftClient(ctx, httpClient, http.MethodGet, clientApiUrl, nil)
		if err != nil || responseStatusCode != http.StatusOK {
			log.Printf("Session %s: client status check failed: %v (HTTP %d)", sessionId, err, responseStatusCode)
			continue
		}

		var status clientGameStatus
		if err := json.Unmarshal([]byte(responseBody), &status); err != nil {
			log.Printf("Session %s: client returned malformed status: %v", sessionId, err)
			continue
		}

		if status.State != "failed" && status.State != "idle" {
			continue
		}

		server.SessionsMutex.Lock()
		session.ClientRelaunchCount++
		relaunchCount := session.ClientRelaunchCount
		server.SessionsMutex.Unlock()

		if relaunchCount > clientMaximumRelaunchCount {
			server.markSessionFailed(sessionId, fmt.Sprintf("The Minecraft client stopped %d times (last state %s: %s)", relaunchCount, status.State, status.Error))
			return
		}

		log.Printf("Session %s: client is %s (%s), relaunching (%d/%d)", sessionId, status.State, status.Error, relaunchCount, clientMaximumRelaunchCount)

		relaunchContext, cancelRelaunch := context.WithTimeout(ctx, server.SessionStartTimeout)
		err = server.startAndJoinPortableMinecraftClient(relaunchContext, clientHost, createMinecraftUsername(sanitizedId))
		cancelRelaunch()

		if err != nil && ctx.Err() == nil {
			log.Printf("Session %s: client relaunch failed: %v", sessionId, err)
		}
	}
}

func (server *Server) startAndJoinPortableMinecraftClient(ctx context.Context, clientHost string, minecraftUsername string) error {
	clientApiUrl := &url.URL{
		Scheme: "http",
		Host:   net.JoinHostPort(clientHost, "80"),
	}
	if err := waitForPortableMinecraftClient(ctx, clientApiUrl); err != nil {
		return err
	}

	maximumMinecraftVersion, err := server.waitForBackendReady(ctx)
	if err != nil {
		return fmt.Errorf("minecraft server is not ready: %w", err)
	}

	clientApiUrl.Path = "/api/game/start/neoforge"
	startRequest := map[string]any{
		"arguments": []string{"--username", minecraftUsername, "--jvm-arg=-Djava.awt.headless=false"},
	}
	if maximumMinecraftVersion != "" {
		startRequest["maximumMinecraftVersion"] = maximumMinecraftVersion
	}
	log.Printf("Starting portable Minecraft client at %s (maximum Minecraft version %q)", clientHost, maximumMinecraftVersion)

	httpClient := &http.Client{Timeout: 30 * time.Second}
	responseStatusCode, responseBody, err := requestPortableMinecraftClient(ctx, httpClient, http.MethodPost, clientApiUrl, startRequest)
	if err != nil {
		return fmt.Errorf("portable Minecraft client launch request failed: %w", err)
	}
	if responseStatusCode != http.StatusAccepted {
		return fmt.Errorf("portable Minecraft client launch returned %d: %s", responseStatusCode, responseBody)
	}

	var acceptedStatus clientGameStatus
	if err := json.Unmarshal([]byte(responseBody), &acceptedStatus); err != nil || acceptedStatus.OperationId <= 0 {
		return fmt.Errorf("portable Minecraft client launch returned malformed status: %s", responseBody)
	}

	clientApiUrl.Path = "/api/game/status"
	deadline := time.Now().Add(10 * time.Minute)
	for {
		responseStatusCode, responseBody, err = requestPortableMinecraftClient(ctx, httpClient, http.MethodGet, clientApiUrl, nil)
		if err != nil {
			return fmt.Errorf("portable Minecraft client status request failed: %w", err)
		}
		if responseStatusCode != http.StatusOK {
			return fmt.Errorf("portable Minecraft client status returned %d: %s", responseStatusCode, responseBody)
		}

		var status clientGameStatus
		if err := json.Unmarshal([]byte(responseBody), &status); err != nil {
			return fmt.Errorf("portable Minecraft client returned malformed status: %w", err)
		}
		if status.OperationId != acceptedStatus.OperationId {
			return fmt.Errorf("portable Minecraft client launch operation %d was superseded by %d", acceptedStatus.OperationId, status.OperationId)
		}
		if status.State == "ready" && status.OperationState == "succeeded" {
			break
		}
		if status.State == "failed" || status.OperationState == "failed" || status.OperationState == "canceled" {
			return fmt.Errorf("portable Minecraft client launch failed: %s", status.Error)
		}
		if time.Now().After(deadline) {
			return fmt.Errorf("portable Minecraft client launch operation %d timed out", acceptedStatus.OperationId)
		}

		if err := sleepContext(ctx, 250*time.Millisecond); err != nil {
			return err
		}
	}

	log.Printf("Portable Minecraft client launch confirmed from %s", clientHost)

	clientApiUrl.Path = "/api/game/connect"
	httpClient.Timeout = 5 * time.Minute
	responseStatusCode, responseBody, err = requestPortableMinecraftClient(ctx, httpClient, http.MethodPost, clientApiUrl, map[string]any{"host": "void", "port": 25565})
	if err != nil {
		return fmt.Errorf("portable Minecraft client failed to join the server: %w", err)
	}
	if responseStatusCode != http.StatusOK {
		return fmt.Errorf("portable Minecraft client connection returned %d: %s", responseStatusCode, responseBody)
	}

	log.Printf("Portable Minecraft client joined the server from %s", clientHost)
	return nil
}

func waitForPortableMinecraftClient(ctx context.Context, clientApiUrl *url.URL) error {
	clientApiUrl.Path = "/api/health"
	httpClient := &http.Client{Timeout: 2 * time.Second}
	deadline := time.Now().Add(2 * time.Minute)
	lastResult := "no response"

	for time.Now().Before(deadline) {
		responseStatusCode, responseBody, err := requestPortableMinecraftClient(ctx, httpClient, http.MethodGet, clientApiUrl, nil)
		if err == nil && responseStatusCode == http.StatusOK {
			log.Printf("Portable Minecraft client API is ready at %s", clientApiUrl.Host)
			return nil
		}

		if err != nil {
			lastResult = err.Error()
		} else {
			lastResult = fmt.Sprintf("HTTP %d: %s", responseStatusCode, responseBody)
		}

		if err := sleepContext(ctx, 250*time.Millisecond); err != nil {
			return err
		}
	}

	return fmt.Errorf("portable Minecraft client API did not become ready within 2 minutes: %s", lastResult)
}

func requestPortableMinecraftClient(ctx context.Context, httpClient *http.Client, method string, clientApiUrl *url.URL, requestBody any) (int, string, error) {
	var bodyReader io.Reader
	if requestBody != nil {
		body, err := json.Marshal(requestBody)
		if err != nil {
			return 0, "", err
		}
		bodyReader = bytes.NewReader(body)
	}

	request, err := http.NewRequestWithContext(ctx, method, clientApiUrl.String(), bodyReader)
	if err != nil {
		return 0, "", err
	}
	if requestBody != nil {
		request.Header.Set("Content-Type", "application/json")
	}

	response, err := httpClient.Do(request)
	if err != nil {
		return 0, "", err
	}

	responseBody, err := io.ReadAll(io.LimitReader(response.Body, 4096))
	closeError := response.Body.Close()
	if err != nil {
		return 0, "", err
	}
	if closeError != nil {
		return 0, "", closeError
	}

	return response.StatusCode, strings.TrimSpace(string(responseBody)), nil
}

func sleepContext(ctx context.Context, duration time.Duration) error {
	timer := time.NewTimer(duration)
	defer timer.Stop()

	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-timer.C:
		return nil
	}
}

func truncateFailureMessage(message string) string {
	message = strings.Join(strings.Fields(message), " ")
	if len(message) > failureMessageMaximumLength {
		message = message[:failureMessageMaximumLength] + "…"
	}

	return message
}

func createSessionId() (string, error) {
	randomBytes := make([]byte, 32)
	if _, err := rand.Read(randomBytes); err != nil {
		return "", err
	}

	return base64.RawURLEncoding.EncodeToString(randomBytes), nil
}

func sanitizeForDockerName(input string) string {
	lowered := strings.ToLower(input)
	builder := strings.Builder{}
	builder.Grow(len(lowered))

	lastWasSeparator := true

	for _, ch := range lowered {
		if (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') {
			builder.WriteRune(ch)
			lastWasSeparator = false
			continue
		}

		if !lastWasSeparator {
			builder.WriteByte('-')
			lastWasSeparator = true
		}
	}

	output := builder.String()
	output = strings.Trim(output, "-")
	if output == "" {
		return "session"
	}

	if len(output) > 60 {
		output = strings.Trim(output[:60], "-")
		if output == "" {
			return "session"
		}
	}

	return output
}

func createMinecraftUsername(sanitizedSessionId string) string {
	suffix := strings.ReplaceAll(sanitizedSessionId, "-", "")
	if len(suffix) > 12 {
		suffix = suffix[:12]
	}

	return "void" + suffix
}

// dockerCommand runs a docker command bounded by timeout and returns its standard output.
func dockerCommand(ctx context.Context, timeout time.Duration, arguments ...string) ([]byte, error) {
	commandContext, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()

	return newDockerCommand(commandContext, arguments...).Output()
}

// runDockerCommand runs a docker command bounded by timeout and returns its combined output.
func runDockerCommand(ctx context.Context, timeout time.Duration, arguments ...string) ([]byte, error) {
	commandContext, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()

	return newDockerCommand(commandContext, arguments...).CombinedOutput()
}

func newDockerCommand(ctx context.Context, arguments ...string) *exec.Cmd {
	command := exec.CommandContext(ctx, "docker", arguments...)
	command.Env = os.Environ()
	command.WaitDelay = 10 * time.Second
	return command
}

func (server *Server) streamContainerLogs(ctx context.Context, containerName string) {
	if !server.RedirectLogs {
		return
	}

	go func() {
		log.Printf("Starting log stream for container: %s", containerName)

		writer := &LogPrefixWriter{Prefix: "[" + containerName + "] "}

		logsCommand := newDockerCommand(ctx, "logs", "--follow", "--tail=0", containerName)

		var stderrBuffer bytes.Buffer
		logsCommand.Stdout = writer
		logsCommand.Stderr = io.MultiWriter(writer, &stderrBuffer)

		err := logsCommand.Run()
		if err == nil || ctx.Err() != nil {
			return
		}

		stderrText := stderrBuffer.String()
		if strings.Contains(stderrText, "No such container") || strings.Contains(stderrText, "is not running") {
			return
		}

		log.Printf("Log streaming ended for %s: %v", containerName, err)
	}()
}

func (writer *LogPrefixWriter) Write(data []byte) (int, error) {
	writer.Mutex.Lock()
	defer writer.Mutex.Unlock()

	_, _ = writer.Buffer.Write(data)
	content := writer.Buffer.String()

	lines := strings.Split(content, "\n")

	if !strings.HasSuffix(content, "\n") {
		writer.Buffer.Reset()
		_, _ = writer.Buffer.WriteString(lines[len(lines)-1])
		lines = lines[:len(lines)-1]
	} else {
		writer.Buffer.Reset()
	}

	for _, line := range lines {
		if line != "" {
			log.Printf("%s%s", writer.Prefix, line)
		}
	}

	return len(data), nil
}

func withBasicLogging(next http.Handler) http.Handler {
	return http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		started := time.Now()

		next.ServeHTTP(writer, request)

		remoteHost, _, _ := net.SplitHostPort(request.RemoteAddr)
		if remoteHost == "" {
			remoteHost = request.RemoteAddr
		}

		log.Printf("%s %s %s %s", request.Method, request.URL.Path, remoteHost, time.Since(started).Truncate(time.Millisecond))
	})
}

func getEnvString(name string, defaultValue string) string {
	value := strings.TrimSpace(os.Getenv(name))
	if value == "" {
		return defaultValue
	}

	return value
}

func getEnvInt(name string, defaultValue int) int {
	value := strings.TrimSpace(os.Getenv(name))
	if value == "" {
		return defaultValue
	}

	parsedValue, err := strconv.Atoi(value)
	if err != nil {
		return defaultValue
	}

	return parsedValue
}

func getEnvBool(name string, defaultValue bool) bool {
	value := strings.TrimSpace(os.Getenv(name))
	if value == "" {
		return defaultValue
	}

	valueLower := strings.ToLower(value)
	return valueLower == "1" || valueLower == "true" || valueLower == "yes" || valueLower == "on"
}
