#!/usr/bin/env bash

# Runs on devcontainer start.
# Starts Redis and the browser services if they're not already running.
# Writes logs to /tmp/devcontainer-post-start.log for troubleshooting.

set -u

LOG_FILE="/tmp/devcontainer-post-start.log"

log() {
  # shellcheck disable=SC2129
  echo "[$(date -Is)] $*" | tee -a "$LOG_FILE" >/dev/null
}

log "post-start: begin"

# -----------------
# Redis
# -----------------
if command -v redis-cli >/dev/null 2>&1 && redis-cli ping >/dev/null 2>&1; then
  log "redis: already running"
else
  if command -v redis-server >/dev/null 2>&1; then
    log "redis: starting (daemonize)"
    redis-server --daemonize yes >>"$LOG_FILE" 2>&1 || log "redis: failed to start"
  else
    log "redis: redis-server not found"
  fi
fi

# -----------------
# Browser services (Xvfb + VNC + Chromium + nginx CDP proxy)
# -----------------
CDP_URL="http://localhost:9222/json/version"

if timeout 1 curl -fsS "$CDP_URL" >/dev/null 2>&1; then
  log "browser: CDP already up"
else
  log "browser: CDP not up, starting start-browser.sh"

  if command -v start-browser.sh >/dev/null 2>&1; then
    # Avoid launching duplicates if a previous run partially started.
    if pgrep -f "chromium.*remote-debugging-port" >/dev/null 2>&1; then
      log "browser: chromium already running; not starting another instance"
    else
      # Use nohup to prevent processes from being killed when post-start.sh exits
      nohup start-browser.sh >>"$LOG_FILE" 2>&1 &
      disown
      log "browser: start-browser.sh launched (background with nohup)"
    fi

    if timeout 15 bash -c "until curl -fsS '$CDP_URL' >/dev/null 2>&1; do sleep 0.5; done"; then
      log "browser: CDP is up"
    else
      log "browser: CDP still not responding after 15s"
    fi
  else
    log "browser: start-browser.sh not found on PATH"
  fi
fi

log "post-start: done"
