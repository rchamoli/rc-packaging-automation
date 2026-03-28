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

WORKDIR /app

# Copy published app (includes SvRooij.ContentPrep for native .intunewin creation)
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "api.dll"]
