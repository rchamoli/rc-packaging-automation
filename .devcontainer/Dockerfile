# Optimized dev container with Browser (VNC + CDP)
# .NET 8 included in base image; Node.js, Azure CLI, GitHub CLI via features
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/devcontainers/dotnet:8.0-bookworm

# Remove Yarn apt repository (has expired GPG key in base image, causing apt-get update to fail)
RUN rm -f /etc/apt/sources.list.d/yarn.list

# Configure passwordless sudo for vscode user (enables frictionless dev experience)
RUN echo "vscode ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/vscode \
    && chmod 0440 /etc/sudoers.d/vscode

# ============================================
# Consolidated apt-get: All system packages in one layer
# ============================================
RUN apt-get update && apt-get install -y --no-install-recommends \
    # Redis server
    redis-server \
    # Utilities
    dos2unix \
    jq \
    socat \
    # Browser (works via apt on Debian, unlike Ubuntu which requires snap)
    chromium \
    chromium-driver \
    # X server and window manager
    xvfb \
    fluxbox \
    x11-xserver-utils \
    # VNC server
    x11vnc \
    # noVNC for web-based VNC access
    novnc \
    websockify \
    # Fonts (minimal set for browser rendering)
    fonts-liberation \
    # Process management
    tini \
    && rm -rf /var/lib/apt/lists/* \
    && apt-get clean

# Install TailwindCSS standalone CLI (architecture-aware)
RUN ARCH=$(dpkg --print-architecture) && \
    if [ "$ARCH" = "arm64" ]; then \
        curl -sLO https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-arm64; \
        chmod +x tailwindcss-linux-arm64; \
        mv tailwindcss-linux-arm64 /usr/local/bin/tailwindcss; \
    else \
        curl -sLO https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64; \
        chmod +x tailwindcss-linux-x64; \
        mv tailwindcss-linux-x64 /usr/local/bin/tailwindcss; \
    fi

# Create browser user and data directory
RUN useradd -m -s /bin/bash browser && \
    mkdir -p /data/chrome-profile && \
    chown -R browser:browser /data

# Configure passwordless sudo for browser user (enables frictionless dev experience)
RUN echo "browser ALL=(ALL) NOPASSWD:ALL" >> /etc/sudoers.d/vscode \
    && chmod 0440 /etc/sudoers.d/vscode

# Copy browser startup script
COPY start-browser.sh /usr/local/bin/start-browser.sh
RUN chmod +x /usr/local/bin/start-browser.sh

# Browser environment variables
ENV DISPLAY=:99
ENV SCREEN_WIDTH=1920
ENV SCREEN_HEIGHT=1080
ENV SCREEN_DEPTH=24
ENV CDP_PORT=9222
ENV VNC_PORT=5900
ENV NOVNC_PORT=6080

# Expose browser ports (VNC, noVNC, CDP)
EXPOSE 5900 6080 9222
