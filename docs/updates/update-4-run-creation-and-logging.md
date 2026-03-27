# Update 4 — Run Creation and Logging

**Issue:** #4 — Run creation and logging

Added the backend endpoint `POST /api/packaging/run` that accepts a packaging request from the New Run form, reads release metadata from the specified folder, creates a run record in Azure Table Storage, and uploads a structured log to Azure Blob Storage.

### New Files
- **`api/Models/ReleaseMetadata.cs`** — Data model mapping the fields in `release-metadata.json` (applicationName, anNumber, releaseVersion, installerType, install/uninstall commands, detection settings, uatGroup, dependencies, supersedence). Includes a `Validate()` method that returns a list of missing or invalid fields.
- **`api/Models/PackagingRunEntity.cs`** — Azure Table Storage entity implementing `ITableEntity`. PartitionKey is the normalized app name (lowercase, spaces replaced with hyphens); RowKey is `{version}-{runId}`. Tracks run ID, app name, version, source type/location, start/end times, status, log URL, output artifact path, metadata file reference, and Intune settings.
- **`api/Utilities/TableNames.cs`** — Constants for the `PackagingRuns` table name and `packaging-logs` blob container name.
- **`api/Utilities/Utc.cs`** — Helper for UTC DateTime operations (`Utc.Now`, `Utc.EnsureUtc`).
- **`api/Services/MetadataReader.cs`** — Reads and validates `release-metadata.json` from a given folder path. Returns clear, user-friendly errors for: missing folder, missing metadata file, invalid JSON, or missing required fields.
- **`api/Services/StorageService.cs`** — Provides `UpsertRunAsync` (Table Storage) and `UploadLogAsync` (Blob Storage) methods. Uses the `STORAGE` connection string. Creates table/container if they don't exist.
- **`api/Services/PackagingService.cs`** — Orchestrates the full run lifecycle: validates inputs, reads metadata, creates initial run record, marks final status (Succeeded/Failed), uploads structured log, and updates run record with log URL.
- **`api/Functions/PackagingFunctions.cs`** — HTTP trigger function at route `packaging/run` (POST). Auth level is Anonymous since SWA routes enforce authentication. Accepts `{ sourceType, releaseFolderPath, createIntuneApp }` and returns `{ id, status, appName, version, logUrl }`.
- **`docs/updates/update-4-run-creation-and-logging.md`** — This file.

### Updated Files
- **`api/Program.cs`** — Registered `MetadataReader`, `StorageService`, and `PackagingService` as singleton services in the DI container.
- **`api/api.csproj`** — Added `Azure.Storage.Blobs` 12.23.0 NuGet package for log blob uploads.
- **`docs/BUILD_LOG.md`** — Added Update 4 section.

### Design Decisions
- **Auth level Anonymous** — SWA configuration at `/api/*` already requires the `authenticated` role; setting the function to Anonymous avoids double-auth issues.
- **PartitionKey strategy** — Normalized app name groups all runs for the same application together, enabling efficient queries by app.
- **RowKey format** — `{version}-{runId}` allows sorting runs by version and guarantees uniqueness.
- **Structured logs** — Each run generates a timestamped log with metadata details, uploaded as a `.log` blob. The log URL is stored on the run entity for later retrieval.
- **Error handling** — User-facing errors are clear and specific (missing folder, missing file, invalid metadata fields). Internal errors are logged server-side and the run is marked as Failed.
- **Run ID** — A 12-character hex string derived from a GUID, used as the primary run identifier returned to the frontend.
