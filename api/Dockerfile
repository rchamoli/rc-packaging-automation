# ── Stage 1: Build ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY api.csproj ./
RUN dotnet restore

# Copy source and publish
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# ── Stage 2: Runtime ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Install Wine for running IntuneWinAppUtil.exe
RUN dpkg --add-architecture i386 && \
    apt-get update && \
    apt-get install -y --no-install-recommends wine wine32 && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Copy Wine wrapper script and make executable
COPY tools/run-intunewin.sh /app/tools/run-intunewin.sh
RUN chmod +x /app/tools/run-intunewin.sh

# NOTE: IntuneWinAppUtil.exe must be added to /app/tools/ at deploy time
# or downloaded via a startup script. It is not included in the repo.

ENV ASPNETCORE_URLS=http://+:8080
ENV WIN32_PREP_TOOL_PATH=/app/tools/run-intunewin.sh

EXPOSE 8080

ENTRYPOINT ["dotnet", "api.dll"]
