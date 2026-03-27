#!/bin/bash
# Browser services startup script for combined dev container
# Starts Xvfb, fluxbox, x11vnc, noVNC, and Chromium with CDP

echo "=== Starting Browser Services ==="
echo "Display: $DISPLAY"
echo "Screen: ${SCREEN_WIDTH}x${SCREEN_HEIGHT}x${SCREEN_DEPTH}"
echo "VNC Port: $VNC_PORT"
echo "CDP Port: $CDP_PORT"
echo "noVNC Port: $NOVNC_PORT"

# Use /tmp for logs and browser profile to avoid permission issues in devcontainers
LOG_DIR="/tmp/browser-logs"
CHROME_PROFILE_DIR="/tmp/chrome-profile"

# Create required directories
mkdir -p "$LOG_DIR" "$CHROME_PROFILE_DIR" 2>/dev/null || true

# Start Xvfb (virtual display) with setsid to fully detach from session
echo "Starting Xvfb..."
setsid Xvfb $DISPLAY -screen 0 ${SCREEN_WIDTH}x${SCREEN_HEIGHT}x${SCREEN_DEPTH} \
    -ac -nolisten tcp -nolisten unix > "$LOG_DIR/xvfb.log" 2>&1 &
XVFB_PID=$!

# Wait for X to be ready
echo "Waiting for X server..."
for i in {1..10}; do
    if xdpyinfo -display $DISPLAY >/dev/null 2>&1; then
        echo "X server is ready"
        break
    fi
    if [ $i -eq 10 ]; then
        echo "ERROR: X server failed to start"
        exit 1
    fi
    sleep 1
done

# Start window manager with setsid
echo "Starting fluxbox..."
setsid fluxbox -display $DISPLAY > "$LOG_DIR/fluxbox.log" 2>&1 &
WM_PID=$!

# Start VNC server (no password for local dev)
echo "Starting x11vnc..."
x11vnc -display $DISPLAY \
    -forever \
    -shared \
    -rfbport $VNC_PORT \
    -nopw \
    -bg \
    -o "$LOG_DIR/x11vnc.log"

# Start noVNC (web-based VNC client)
echo "Starting noVNC..."
websockify -D \
    --web=/usr/share/novnc \
    --cert=/dev/null \
    $NOVNC_PORT \
    localhost:$VNC_PORT

# Wait a bit for everything to stabilize
sleep 2

# Create log file with proper permissions
touch "$LOG_DIR/chromium.log"

# Clean up any stale Chromium lock files
rm -f "$CHROME_PROFILE_DIR/SingletonLock"
rm -f "$CHROME_PROFILE_DIR/SingletonSocket"
rm -f "$CHROME_PROFILE_DIR/SingletonCookie"

# Start Chromium directly on CDP_PORT with setsid (fully detached from session)
echo "Starting Chromium with CDP on port $CDP_PORT..."
setsid chromium \
    --remote-debugging-port=$CDP_PORT \
    --remote-debugging-address=0.0.0.0 \
    --remote-allow-origins=* \
    --no-first-run \
    --no-default-browser-check \
    --disable-gpu \
    --disable-dev-shm-usage \
    --disable-software-rasterizer \
    --disable-features=AudioServiceOutOfProcess,TranslateUI \
    --disable-background-networking \
    --disable-sync \
    --metrics-recording-only \
    --disable-default-apps \
    --mute-audio \
    --no-service-autorun \
    --password-store=basic \
    --use-mock-keychain \
    --no-sandbox \
    --disable-setuid-sandbox \
    --user-data-dir="$CHROME_PROFILE_DIR" \
    --window-size=${SCREEN_WIDTH},${SCREEN_HEIGHT} \
    --window-position=0,20 \
    about:blank > "$LOG_DIR/chromium.log" 2>&1 &

# Wait for Chromium's CDP to be ready
sleep 3

# Verify CDP is listening
if timeout 10 bash -c "until curl -s http://localhost:$CDP_PORT/json/version > /dev/null 2>&1; do sleep 0.5; done"; then
    echo "✓ Chromium CDP is ready on port $CDP_PORT"
    curl -s http://localhost:$CDP_PORT/json/version
else
    echo "✗ WARNING: Chromium CDP may not be responding on port $CDP_PORT"
    echo "Check logs at $LOG_DIR/chromium.log"
fi

echo "=== Browser Services Ready ==="
echo "VNC: localhost:$VNC_PORT"
echo "noVNC: http://localhost:$NOVNC_PORT"
echo "CDP: http://localhost:$CDP_PORT/json/version"
echo "Logs: $LOG_DIR"
echo "=============================="
