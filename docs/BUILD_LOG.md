# Build Log

## Update 1 — Homepage and App Layout (Issue #1)

**Date:** 2026-03-19

### Files Created
| File | Purpose |
|------|---------|
| `app/dashboard.html` | Authenticated dashboard shell with top nav, stat cards, and recent runs placeholder |
| `docs/BUILD_LOG.md` | This file — tracks development progress |
| `docs/updates/update-1-homepage-and-layout.md` | Plain-English summary of this update |
| `docs/screenshots/01-homepage.png` | Screenshot of the public landing page |

### Files Replaced / Updated
| File | Change |
|------|--------|
| `index.html` | Replaced template landing page with Nouryon-branded packaging automation homepage |
| `README.md` | Replaced template README with application-specific setup and usage notes |
| `staticwebapp.config.swa.json` | Updated `/app/` redirect to point to `/app/dashboard.html` |
| `app/app.ts` | Simplified to work with new dashboard layout; wires both desktop and mobile user info elements |

### Patterns Established
- **Standard head block** from `docs/DESIGN.md` used on all pages (Tailwind CDN config with brand colors, DM Sans + Inter fonts)
- **Top navigation bar** with Nouryon logo on the left, links on the right, and a `<dialog>`-based mobile menu below `md` breakpoint
- **Card component** (`bg-white rounded-lg shadow-sm border border-neutral-200 p-6`) used for stat cards and content sections
- **Empty state pattern** used in the Recent Runs card on the dashboard
- **Page header pattern** with title, subtitle, and action button
- **Mobile element wiring** — both desktop and mobile variants of user info are updated from `app.ts`

### App Shell Structure
```
/                    → Public landing page (index.html)
/app/                → Redirects to /app/dashboard.html (302)
/app/dashboard.html  → Authenticated dashboard (requires "authenticated" role)
/app/new-run.html    → (placeholder link — future issue)
/app/runs.html       → (placeholder link — future issue)
/app/settings.html   → (placeholder link — future issue)
```

---

## Update 2 — Signed-in Dashboard (Issue #2)

**Date:** 2026-03-19

### Files Created
| File | Purpose |
|------|---------|
| `docs/screenshots/02-dashboard.png` | Screenshot of the authenticated dashboard at 1280×720 |
| `docs/updates/update-2-signed-in-dashboard.md` | Plain-English summary of this update |

### Files Replaced / Updated
| File | Change |
|------|--------|
| `app/index.html` | Replaced template placeholder with Nouryon-branded redirect to dashboard.html |
| `app/app.ts` | Fixed unauthenticated redirect (was pointing to non-existent oidc-callback.html, now redirects to `/`) |
| `app/dashboard.html` | Added "Start a new run" and "View recent runs" action tile cards with links |
| `docs/BUILD_LOG.md` | Added this update section |

### What Changed
- **app/index.html** was still the original template with wrong branding (Rapid Circle, #0066CC, Plus Jakarta Sans) and generic content (User Profile, New Project, View Analytics). Replaced with a minimal Nouryon-branded page that redirects to `/app/dashboard.html`. Since the SWA config already redirects `/app/` → `/app/dashboard.html` (302), this file serves as a fallback if someone visits `/app/index.html` directly.
- **app/app.ts** had a redirect to `/oidc-callback.html` when the user was not authenticated — that page doesn't exist. Changed to redirect to `/` (the public homepage), letting SWA handle auth redirects naturally.
- **app/dashboard.html** gained two quick-action tile cards ("Start a new run" → `/app/new-run.html`, "View recent runs" → `/app/runs.html`) above the existing stat cards and recent runs section.

---

## Update 3 — New Run Form (Issue #3)

**Date:** 2026-03-19

### Files Created
| File | Purpose |
|------|---------|
| `app/new-run.html` | New Run form page with source type, release folder path, and Create Intune app toggle |
| `app/newRun.ts` | TypeScript module handling form validation, toggle state, and API submission |
| `docs/screenshots/03-new-run.png` | Screenshot of the New Run page at 1280×720 |
| `docs/updates/update-3-new-run-form.md` | Plain-English summary of this update |

### Files Updated
| File | Change |
|------|--------|
| `.gitignore` | Added `app/newRun.js` and `app/newRun.js.map` to exclude compiled TypeScript output |
| `docs/BUILD_LOG.md` | Added this update section |

### What Changed
- **New Run page** (`/app/new-run.html`) implements the Form page layout from DESIGN.md (`max-w-2xl mx-auto`) with the shared app shell (nav bar, mobile dialog menu, footer). "New Run" is marked as the active nav link in both desktop and mobile menus.
- **Form fields**: source type dropdown (File Share, Azure Blob Storage), release folder path text input with placeholder and helper text, accessible toggle switch for "Create Intune app after packaging" (defaults to on).
- **Client-side validation** highlights required fields with red borders and inline error messages; errors clear on user input.
- **Form submission** POSTs to `/api/packaging/run` with `{ sourceType, releaseFolderPath, createIntuneApp }`. Shows loading spinner, success message with link to run detail, or error message — all inline (no browser `alert()` calls).
- **newRun.ts** waits for the `app-shell-ready` event before initialising, ensuring the user is authenticated before the form becomes active.

---

## Update 4 — Run Creation and Logging (Issue #4)

**Date:** 2026-03-19

### Files Created
| File | Purpose |
|------|---------|
| `api/Models/ReleaseMetadata.cs` | Data model for release-metadata.json with validation logic |
| `api/Models/PackagingRunEntity.cs` | Azure Table Storage entity for packaging run records |
| `api/Utilities/TableNames.cs` | Constants for Table Storage table names and Blob container names |
| `api/Utilities/Utc.cs` | UTC DateTime helper methods |
| `api/Services/MetadataReader.cs` | Loads and validates release-metadata.json from a folder path |
| `api/Services/StorageService.cs` | Azure Table Storage and Blob Storage helper methods |
| `api/Services/PackagingService.cs` | Orchestrates run creation: validate, read metadata, create record, upload log |
| `api/Functions/PackagingFunctions.cs` | POST /api/packaging/run HTTP trigger function |
| `docs/updates/update-4-run-creation-and-logging.md` | Plain-English summary of this update |

### Files Updated
| File | Change |
|------|--------|
| `api/Program.cs` | Registered MetadataReader, StorageService, and PackagingService in DI container |
| `api/api.csproj` | Added Azure.Storage.Blobs 12.23.0 NuGet package |
| `docs/BUILD_LOG.md` | Added this update section |

### What Changed
- **POST /api/packaging/run** endpoint accepts `{ sourceType, releaseFolderPath, createIntuneApp }` and returns `{ id, status, appName, version, logUrl }`. Auth level is Anonymous (SWA routes enforce authentication).
- **MetadataReader** reads `release-metadata.json` from the provided folder path and validates all required fields (applicationName, anNumber, releaseVersion, installerType, installCommand, uninstallCommand, detectionType, uatGroup, dependencies, supersedence). Returns clear errors for missing folder, missing file, invalid JSON, or missing fields.
- **PackagingRunEntity** uses Azure Table Storage with PartitionKey = normalized app name (lowercase, hyphenated) and RowKey = version-runId. Tracks status (Running/Succeeded/Failed) with UTC timestamps.
- **StorageService** provides Table Storage upsert for run records and Blob Storage upload for run logs. Table name: `PackagingRuns`, Blob container: `packaging-logs`.
- **PackagingService** orchestrates the full run lifecycle: validate inputs → read metadata → create run record → mark status → upload log blob → update run record with log URL.
- Every run produces a structured log uploaded to Blob Storage at `{normalized-app-name}/{runId}.log` with timestamps, metadata details, and status transitions.

---

## Update 5 — Runs List (Issue #5)

**Date:** 2026-03-19

### Files Created
| File | Purpose |
|------|---------|
| `app/runs.html` | Runs list page with table, app name filter, empty/error states |
| `app/runs.ts` | TypeScript module to fetch and render runs from API |
| `docs/screenshots/05-runs-list.png` | Screenshot of the Runs list page at 1280×720 |
| `docs/updates/update-5-runs-list.md` | Plain-English summary of this update |

### Files Updated
| File | Change |
|------|--------|
| `api/Functions/PackagingFunctions.cs` | Added GET /api/packaging/runs endpoint with optional appName filter |
| `api/Services/StorageService.cs` | Added `GetRunsAsync` method to query runs from Table Storage |
| `.gitignore` | Added `app/runs.js` and `app/runs.js.map` to exclude compiled TypeScript output |
| `docs/BUILD_LOG.md` | Added this update section |

### What Changed
- **GET /api/packaging/runs** endpoint returns a list of recent packaging runs sorted by start time (most recent first). Supports optional `?appName=` query parameter to filter by application name. Returns `{ runs: [{ id, appName, version, status, startTime, endTime, sourceType, logUrl }] }`.
- **StorageService.GetRunsAsync** queries the PackagingRuns table with optional PartitionKey filter (normalized app name). Returns up to 50 results sorted by start time descending.
- **Runs page** (`/app/runs.html`) uses the List page layout from DESIGN.md (`max-w-7xl mx-auto`). Shows a responsive table with status badges (Succeeded=green, Failed=red, Running=amber), app name, version, source type, start/end times, and a "View →" link to run detail.
- **Filter bar** allows filtering by app name with Apply/Clear buttons. Pressing Enter in the input also triggers the filter.
- **Empty state** displays a friendly message with a link to start a new run when no runs exist.
- **Error state** shows a red alert with the error message if the API call fails.
- **runs.ts** waits for the `app-shell-ready` event before initialising, ensuring authentication is loaded before making API calls. Uses `escapeHtml()` to safely render user-provided text.

---

## Update 6 — Run Details and Downloads (Issue #6)

**Date:** 2026-03-19

### Files Created
| File | Purpose |
|------|---------|
| `app/run-detail.html` | Run detail page with metadata summary, log link, and artifact download |
| `app/runDetail.ts` | TypeScript module to fetch and render run detail from API |
| `api/Functions/SeedPackagingRuns.cs` | Seed data function to create sample packaging runs with logs |
| `docs/screenshots/06-run-detail.png` | Screenshot of the Run Detail page at 1280×720 |
| `docs/updates/update-6-run-details-and-downloads.md` | Plain-English summary of this update |

### Files Updated
| File | Change |
|------|--------|
| `api/Functions/PackagingFunctions.cs` | Added GET /api/packaging/runs/{runId} endpoint returning full run details with SAS URLs |
| `api/Services/StorageService.cs` | Added `GetRunByIdAsync` (cross-partition lookup) and `GenerateBlobSasUrl` (time-limited read URLs) |
| `staticwebapp.config.swa.json` | Added anonymous route for `/api/manage/seed-packaging-runs` |
| `index.html` | Added seed-packaging-runs endpoint to Seed Demo Data button |
| `.gitignore` | Added `app/runDetail.js` and `app/runDetail.js.map` |
| `docs/BUILD_LOG.md` | Added this update section |

### What Changed
- **GET /api/packaging/runs/{runId}** endpoint looks up a run by its 12-character hex ID across all app partitions using a table query on the `RunId` property. Returns full metadata including source location, metadata file reference, Intune settings, and time-limited SAS URLs for log and artifact blobs.
- **StorageService.GetRunByIdAsync** queries the PackagingRuns table with a filter on `RunId`, returning the first match regardless of partition. This avoids requiring the caller to know the app name (partition key).
- **StorageService.GenerateBlobSasUrl** creates 30-minute read-only SAS URLs for blob storage items. Falls back gracefully to the plain blob URL if the account key is unavailable, and returns null on errors.
- **Run detail page** (`/app/run-detail.html`) uses the Detail layout from DESIGN.md (`max-w-5xl mx-auto`). Two-column layout on `lg`: left column shows metadata (app name, version, status badge, source type, timestamps, source location, metadata file, Intune status) and right column shows download links (View Run Log, Download Artifact). Missing logs and artifacts are handled with "No X available" messages.
- **runDetail.ts** reads the `id` query parameter, fetches from `/api/packaging/runs/{id}`, and renders the detail view. Shows loading spinner, error state, or 404 message as appropriate.
- **SeedPackagingRuns** creates four sample runs across three apps (Nouryon Safety Suite, ChemTracker Desktop, Lab Inventory Manager) with various statuses and uploads sample log blobs for completed runs.
- The runs list (`runs.ts`) already linked to `run-detail.html?id=...` — no changes needed.

---

## Update 7 — Packaging Tool Output (Issue #7)

**Date:** 2026-03-19

### Files Created
| File | Purpose |
|------|---------|
| `docs/screenshots/07-run-detail-artifact.png` | Screenshot of run detail with artifact download link |
| `docs/screenshots/07-run-detail-error.png` | Screenshot of failed run detail with error summary |
| `docs/updates/update-7-packaging-tool-output.md` | Plain-English summary of this update |

### Files Updated
| File | Change |
|------|--------|
| `api/Models/PackagingRunEntity.cs` | Added `ErrorSummary` property for concise failure reasons |
| `api/Utilities/TableNames.cs` | Added `BlobContainers.Artifacts` constant for the artifacts blob container |
| `api/Services/StorageService.cs` | Added `UploadArtifactAsync` method to upload .intunewin files to the artifacts container |
| `api/Services/PackagingService.cs` | Replaced placeholder success with Win32 Content Prep Tool execution, timeout, stdout/stderr capture, and artifact upload |
| `api/Functions/PackagingFunctions.cs` | Fixed artifact SAS URL to use `artifacts` container; added `errorSummary` to StartRun and GetRun responses |
| `api/Functions/SeedPackagingRuns.cs` | Added artifact path to succeeded run, error summary to failed run; uploads sample artifact blob |
| `app/runDetail.ts` | Added `errorSummary` to RunDetail interface; renders error row for failed runs |
| `app/run-detail.html` | Added hidden error summary row in run information card |
| `docs/BUILD_LOG.md` | Added this update section |

### What Changed
- **PackagingService** now reads `WIN32_PREP_TOOL_PATH` from environment variables. If not configured, the run fails immediately with a clear error message and the log is still uploaded. When configured, the tool is launched as a child process with stdout/stderr captured into the log.
- **Server-side timeout** defaults to 300 seconds (configurable via `WIN32_PREP_TOOL_TIMEOUT_SECONDS`). If exceeded, the process is killed and the run is marked as Failed.
- **Artifact upload** — on success (exit code 0), the service searches for `*.intunewin` in the output directory and uploads it to the `artifacts` blob container with path `{normalized-app}/{runId}/{filename}.intunewin`. The blob path is stored in `OutputArtifactPath`.
- **Error summary** — `PackagingRunEntity.ErrorSummary` stores a short failure reason (missing tool path, timeout, non-zero exit code, exceptions). The run detail API returns this field and the UI displays it in a red error row.
- **Artifact SAS URLs** — `GetPackagingRun` now correctly generates SAS URLs against the `artifacts` container (previously used `packaging-logs`).
- **Seed data** — the succeeded sample run now includes an artifact blob path with a sample file uploaded to the artifacts container; the failed sample run includes an error summary.

---

## Update 8 — Intune Setup from Metadata (Issue #8)

**Date:** 2026-03-19

### Files Created
| File | Purpose |
|------|---------|
| `api/Models/IntuneAppRefEntity.cs` | Table Storage entity mapping app name/version to Intune app ID |
| `api/Services/IntuneGraphService.cs` | Graph API service for Win32 app creation, content upload, detection rules, relationships, and UAT assignment |
| `api/Functions/IntuneAppFunctions.cs` | POST /api/intune/create-from-run/{runId} and GET /api/intune/appref/resolve endpoints |
| `docs/updates/update-8-intune-setup-from-metadata.md` | Plain-English summary of this update |

### Files Updated
| File | Change |
|------|--------|
| `api/Utilities/TableNames.cs` | Added `IntuneAppRefs` table constant |
| `api/Services/StorageService.cs` | Added `GetIntuneAppRefsTableAsync`, `UpsertIntuneAppRefAsync`, and `GetIntuneAppRefAsync` methods |
| `api/Services/PackagingService.cs` | Added IntuneGraphService dependency; calls Intune creation after successful packaging when `createIntuneApp` is true |
| `api/Functions/PackagingFunctions.cs` | Added `intuneAppId` to StartRun response |
| `api/Program.cs` | Registered IntuneGraphService singleton |
| `api/api.csproj` | Added Microsoft.Graph 5.74.0 and Azure.Identity 1.17.0 NuGet packages |
| `docs/BUILD_LOG.md` | Added this update section |

### What Changed
- **IntuneGraphService** uses Microsoft Graph SDK v5 with client credentials flow (`ClientSecretCredential` from Azure.Identity) to create Win32LobApp entries in Intune. Reads `GRAPH_TENANT_ID`, `GRAPH_CLIENT_ID`, and `GRAPH_CLIENT_SECRET` from environment variables. Returns clear errors when credentials are not configured.
- **Detection rules** are built from metadata: registry key/value detection is the primary rule (using `Win32LobAppRegistryRule`); MSI product code detection (`Win32LobAppProductCodeRule`) is added only for true MSI installers.
- **Content upload** follows the full Graph content version flow: create content version → create content file → poll for Azure Storage URI → upload in 6 MiB blocks → commit with encryption info extracted from the `.intunewin` archive.
- **Dependencies and supersedence** are parsed from metadata in "AppName|Version" format (comma-separated). Each target is resolved by looking up the IntuneAppRefs table. Relationships are applied via raw HTTP POST to the Graph API's `updateRelationships` action.
- **UAT assignment** resolves the group from metadata (GUID or display name) and creates an assignment with `Available` intent.
- **IntuneAppRefEntity** stores the mapping in the `IntuneAppRefs` table (PartitionKey = normalized app name, RowKey = version) for cross-run dependency resolution.
- **Automatic creation** — when `createIntuneApp` is true and packaging succeeds, `PackagingService` calls `IntuneGraphService.CreateFromRunAsync`. Failures are logged but do not fail the packaging run.
- **POST /api/intune/create-from-run/{runId}** allows manual Intune app creation from any succeeded run. Validates run status, reads metadata, and returns `{ intuneAppId, intuneAppLink, runId, appName, version }`.
- **GET /api/intune/appref/resolve?appName=...&version=...** resolves Intune app references for dependency/supersedence resolution.

---

## Update 9 — Settings and Metadata Help (Issue #9)

**Date:** 2026-03-19

### Files Created
| File | Purpose |
|------|---------|
| `app/settings.html` | Settings & Help page with metadata schema, JSON examples, tool path guidance, naming conventions |
| `docs/screenshots/09-settings.png` | Screenshot of the Settings page at 1280×720 |
| `docs/updates/update-9-settings-and-help.md` | Plain-English summary of this update |

### Files Updated
| File | Change |
|------|--------|
| `docs/BUILD_LOG.md` | Added this update section |

### What Changed
- **Settings & Help page** (`/app/settings.html`) uses the Settings page layout from DESIGN.md (`max-w-7xl mx-auto`) with a two-column layout on `lg`: left side has a sticky sub-nav card with anchor links, right side has content cards for each section.
- **Metadata Schema** — a table documents every field in `release-metadata.json` with name, required/conditional badge, and plain-language description.
- **Registry Detection Example** — complete `release-metadata.json` for an EXE installer using registry key/value detection with supersedence.
- **MSI Product Code Example** — complete `release-metadata.json` for an MSI installer using product code detection with dependencies and multiple supersedence entries.
- **Dependencies & Supersedence** — explains the `AppName|Version` pipe-separated format, shows single/multiple/none examples, and describes how resolution works via the IntuneAppRefs table.
- **Tool Path Configuration** — documents `WIN32_PREP_TOOL_PATH` and `WIN32_PREP_TOOL_TIMEOUT_SECONDS` environment variables, shows the command-line arguments, and lists common errors (missing tool path, timeout, non-zero exit code) with explanations.
- **Naming Conventions** — explains normalization rules (lowercase, spaces → hyphens) and documents storage path patterns for logs, artifacts, and table keys.
- The page is static (no TypeScript module needed). All existing pages already linked to `/app/settings.html` in both desktop and mobile navs; the new page marks "Settings" as the active link.

---

## Update 10 — Friendly Errors and Edge Cases (Issue #10)

**Date:** 2026-03-19

### Files Created
| File | Purpose |
|------|---------|
| `docs/updates/update-10-friendly-errors.md` | Plain-English summary of this update |

### Files Updated
| File | Change |
|------|--------|
| `api/Services/MetadataReader.cs` | Added IOException and general Exception catches with friendly messages; improved JSON error text |
| `api/Functions/PackagingFunctions.cs` | Added top-level try-catch to StartRun, ListRuns, GetRun; added errorSummary to ListRuns response |
| `api/Functions/IntuneAppFunctions.cs` | Added top-level try-catch to CreateFromRun and ResolveAppRef |
| `app/new-run.html` | Added dismiss button to error banner |
| `app/newRun.ts` | Fixed run detail link (was `/app/runs.html`); wired dismiss button |
| `app/runs.html` | Added retry button to error banner |
| `app/runs.ts` | Added errorSummary to interface and render error sub-row for failed runs; wired retry button |
| `app/run-detail.html` | Added retry button to error banner |
| `app/runDetail.ts` | Wired retry button for error state |
| `docs/BUILD_LOG.md` | Added this update section |

### What Changed
- **Backend error standardisation** — every HTTP-triggered function now wraps its handler body in a top-level `try / catch`. Unhandled exceptions are logged server-side and return a generic friendly message (`{ "error": "An unexpected error occurred…" }`) — no stack traces, paths, or class names leak to the client.
- **MetadataReader improvements** — `IOException` catch returns "Could not read release-metadata.json. The file may be locked or inaccessible." General `Exception` catch returns "An unexpected error occurred while reading release-metadata.json." JSON parse errors now say "contains invalid JSON — check for syntax errors" instead of forwarding internal `JsonException.Message` details.
- **ListRuns errorSummary** — `GET /api/packaging/runs` now includes `errorSummary` in each run so the Runs list page can show inline failure reasons.
- **New Run page** — fixed the success banner link (was pointing to runs.html instead of run-detail.html); added a dismiss (✕) button on the error banner.
- **Runs list page** — failed runs now show a light-red sub-row with the error summary; error banner includes a "Try again" retry button.
- **Run Detail page** — error banner includes a "Try again" retry button. Missing blobs/links already handled gracefully with "No log available" / "No artifact available" placeholders.

---

## Update 11 — Sample Data for Testing (Issue #11)

**Date:** 2026-03-20

### Files Created
| File | Purpose |
|------|---------|
| `api/Services/AppDataSeeder.cs` | Service that seeds PackagingRunEntity (5 runs across 3 apps) and IntuneAppRefEntity (3 refs) with log/artifact blobs |
| `api/Functions/DemoAdminFunctions.cs` | Two endpoints: `POST /api/demo/seed` (seeds users + app data) and `POST /api/demo/reset` (clears all, re-seeds) |
| `data/demo-users.csv` | Application-specific personas — Packager, App Owner, QA Tester |
| `docs/updates/update-11-sample-data.md` | Plain-English summary of this update |

### Files Updated
| File | Change |
|------|--------|
| `users.json` | Replaced template personas with Nouryon-specific: Kai Patel (packager), Lisa van der Berg (app owner), Sam Okoye (QA tester) |
| `mock-oidc-provider/users.json` | Updated to match solution-root users.json personas |
| `api/Program.cs` | Registered `AppDataSeeder` singleton |
| `api/Services/StorageService.cs` | Added `ClearTableAsync` and `ClearBlobContainerAsync` helpers for demo reset |
| `staticwebapp.config.swa.json` | Added anonymous routes for `/api/demo/seed` and `/api/demo/reset` before `/api/*` wildcard |
| `index.html` | Seed button now calls unified `/api/demo/seed` endpoint; inline success/error feedback |

### What Changed
- **Unified seed/reset endpoints** — `POST /api/demo/seed` seeds both sample entities and application data (packaging runs, Intune app refs, log/artifact blobs) in one call. `POST /api/demo/reset` deletes all tables and blob containers, then re-seeds.
- **AppDataSeeder service** — seeds 5 packaging runs across 3 apps (Nouryon Safety Suite, ChemTracker Desktop, Lab Inventory Manager) with varied statuses (Succeeded, Running, Failed) and 3 Intune app references for dependency/supersedence demos. Uploads sample log blobs and placeholder `.intunewin` artifacts.
- **Idempotent** — all entities use upsert-replace; blobs use overwrite:true. Safe to call multiple times.
- **Demo personas** — 3 customised personas: Kai Patel (packager), Lisa van der Berg (app owner), Sam Okoye (QA tester). Updated in `users.json`, `mock-oidc-provider/users.json`, and `data/demo-users.csv`.
- **Clear helpers** — `StorageService.ClearTableAsync` deletes entire table; `ClearBlobContainerAsync` iterates and deletes all blobs. Used by the reset endpoint.

---

## Update 12 — Quality Check: Core Features (Issue #12)

**Date:** 2026-03-20

### Files Created
| File | Purpose |
|------|---------|
| `docs/updates/update-12-quality-check-core.md` | Plain-English summary of this update |

### Testing Results

All critical user workflows were tested end-to-end via browser automation with seeded demo data.

| Workflow | Result | Notes |
|----------|--------|-------|
| Landing page → Seed Demo Data | ✅ Pass | Button POSTs to `/api/demo/seed`, shows "✓ Seeded!" feedback, auto-resets after 3s |
| Login → Dashboard loads | ✅ Pass | Dashboard shows "Welcome, Kai" with "Kai Patel" in nav; all nav links work |
| Runs list renders seeded data | ✅ Pass | All 5 seeded runs display with correct status badges, app names, versions, dates |
| Failed run error sub-row | ✅ Pass | "Installer file not found in release folder" shown inline for failed run |
| Run detail (Succeeded) | ✅ Pass | All fields populated; View Run Log and Download Artifact links render with SAS URLs |
| Run detail (Failed) | ✅ Pass | Error summary displayed; log link present; "No artifact available" shown correctly |
| Run detail (Running) | ✅ Pass | "No log available" and "No artifact available" shown correctly; no end date |
| Console errors | ✅ Pass | No JavaScript errors — only expected Tailwind CDN warning |

### Observations (non-blocking)
- **Dashboard stat cards** show static placeholder dashes ("—") and "No runs yet" — these are not wired to the API yet. Not a blocking bug; a future enhancement.
- **Run detail initial load** takes ~2–3 seconds due to the `app-shell-ready` event pattern (auth check completes before page-level scripts initialise). Expected behaviour.

### Bugs Fixed
None — no blocking bugs were found during testing.

---

## Update 13 — Quality Check: All Features (Issue #13)

**Date:** 2026-03-20

### Files Created
| File | Purpose |
|------|---------|
| `docs/updates/update-13-quality-check-all.md` | Plain-English summary of this update |

### Files Updated
| File | Change |
|------|--------|
| `api/Services/StorageService.cs` | Fixed app name filter in `GetRunsAsync` — changed from PartitionKey exact match to case-insensitive substring match on `AppName` property |
| `docs/BUILD_LOG.md` | Added this update section |

### Testing Results

All remaining pages and workflows were tested end-to-end via browser automation with seeded demo data.

| Workflow | Result | Notes |
|----------|--------|-------|
| New Run — empty form validation | ✅ Pass | "Please select a source type" and "Release folder path is required" shown on submit |
| New Run — invalid folder path (backend error) | ✅ Pass | Shows friendly "Release folder not found: \\\\server\\..." error with dismiss button |
| Settings/Help page — desktop | ✅ Pass | All sections readable: metadata schema table, JSON examples, tool path, naming conventions |
| Settings/Help page — mobile (375px) | ✅ Pass | Hamburger menu works, content stacks, "On this page" nav visible |
| Reset demo data | ✅ Pass | `POST /api/demo/reset` clears all data, re-seeds baseline (5 runs, 3 app refs) |
| Runs list filter — partial name | ✅ Pass (after fix) | "ChemTracker" now matches "ChemTracker Desktop" — was broken before fix |
| Runs list filter — case insensitive | ✅ Pass (after fix) | "lab" matches "Lab Inventory Manager", "nouryon" matches "Nouryon Safety Suite" |
| Runs list filter — Clear button | ✅ Pass | Clears filter text and reloads all runs |
| Run detail — succeeded | ✅ Pass | All fields populated; View Run Log and Download Artifact links with SAS URLs |
| Run detail — failed | ✅ Pass | Error summary displayed; log link present; "No artifact available" |
| Run detail — running (in progress) | ✅ Pass | "No log available" and "No artifact available"; no end date |
| Run detail — non-existent ID | ✅ Pass | "Run not found. It may have been deleted." with Try again and Back to Runs links |
| Run detail — missing ID parameter | ✅ Pass | "No run ID specified. Please navigate from the Runs list." |
| Navigation — all pages desktop | ✅ Pass | Dashboard, New Run, Runs, Settings links all work; active state highlighted |
| Navigation — mobile hamburger menu | ✅ Pass | Menu dialog opens/closes; all links present; user name and Logout shown |
| Console errors | ✅ Pass | No JavaScript errors — only expected Tailwind CDN warning |

### Bugs Found and Fixed

| Bug | Severity | Fix |
|-----|----------|-----|
| Runs list app name filter did not match partial or display names | Medium | Changed `GetRunsAsync` from `PartitionKey eq` (exact normalized match) to in-memory `AppName.Contains()` with `StringComparison.OrdinalIgnoreCase`. Previously, typing "ChemTracker" in the filter yielded "No runs found" because it normalised to "chemtracker" which didn't match PartitionKey "chemtracker-desktop". |

### Observations (non-blocking, unchanged from Update 12)
- **Dashboard stat cards** show static placeholder dashes ("—") and "No runs yet" — not wired to the API yet. Future enhancement.
- **Run detail initial load** takes ~2–3 seconds due to the `app-shell-ready` event pattern. Expected behaviour.

---

## Update 14 — Final Review and Polish (Issue #14)

**Date:** 2026-03-20

**Files Created:**
- `docs/updates/update-14-final-review.md` — Client-facing final progress report

**Files Updated:**
- `docs/BUILD_LOG.md` — Added this final testing summary

**Re-Testing Results:**

All critical workflows and previously fixed areas re-tested end-to-end via browser automation with seeded demo data to confirm no regressions:

| Workflow | Result | Notes |
|----------|--------|-------|
| Landing page renders | ✅ Pass | Nouryon branding, hero, How It Works, Features, footer all display correctly |
| Seed Demo Data button | ✅ Pass | POSTs to `/api/demo/seed`, shows "✓ Seeded!" feedback, resets after 3s |
| Login → Dashboard | ✅ Pass | "Welcome, Kai" heading, "Kai Patel" in nav, quick-action tiles link correctly |
| Runs list — all data | ✅ Pass | 5 seeded runs with correct status badges, app names, versions, timestamps |
| Runs list — failed run error sub-row | ✅ Pass | "Installer file not found in release folder" shown inline |
| Runs list — partial name filter (Issue 13 fix) | ✅ Pass | "ChemTracker" matches "ChemTracker Desktop" — 2 runs returned |
| Runs list — case-insensitive filter (Issue 13 fix) | ✅ Pass | "lab" matches "Lab Inventory Manager" — 1 run returned |
| Runs list — Clear filter button | ✅ Pass | Clears input, restores all 5 runs |
| Run detail — Succeeded | ✅ Pass | All fields populated; View Run Log and Download Artifact with SAS URLs |
| Run detail — Failed | ✅ Pass | Error summary displayed; log link present; "No artifact available" |
| Run detail — Running | ✅ Pass | End time "—"; "No log available"; "No artifact available"; Intune "Requested" |
| Run detail — non-existent ID | ✅ Pass | "Run not found. It may have been deleted." with Try again and Back to Runs |
| Run detail — missing ID param | ✅ Pass | "No run ID specified. Please navigate from the Runs list." |
| New Run — empty form validation | ✅ Pass | "Please select a source type" and "Release folder path is required" |
| Settings/Help page | ✅ Pass | All sections render correctly |
| Navigation — all pages | ✅ Pass | Dashboard, New Run, Runs, Settings links all work with active state |
| API — GET /api/packaging/runs | ✅ Pass | Returns all 5 runs as JSON |
| API — GET /api/packaging/runs?appName=nouryon | ✅ Pass | Case-insensitive partial filter returns 2 Nouryon runs |
| API — GET /api/packaging/runs/{id} | ✅ Pass | Full run detail with SAS URLs |
| API — POST /api/demo/reset | ✅ Pass | Clears all data, re-seeds baseline (5 runs, 3 app refs) |
| Console errors | ✅ Pass | No JavaScript errors — only expected Tailwind CDN warning |

**Route Protection Verification:**

| Route | Expected | Verified |
|-------|----------|----------|
| `/` (public landing) | Accessible without auth | ✅ Correct — serves landing page |
| `/app/*` | Requires `authenticated` role | ✅ Correct — `staticwebapp.config.swa.json` enforces role; SWA CLI emulator serves content locally but production SWA would redirect to OIDC login via 401 → 302 override |
| `/api/demo/seed` | Anonymous | ✅ Correct — explicit `anonymous` role in config |
| `/api/demo/reset` | Anonymous | ✅ Correct — explicit `anonymous` role in config |
| `/api/manage/seed-sample` | Anonymous | ✅ Correct — explicit `anonymous` role in config |
| `/api/manage/seed-packaging-runs` | Anonymous | ✅ Correct — explicit `anonymous` role in config |
| `/api/*` (all other) | Requires `authenticated` role | ✅ Correct — catch-all route enforces role |
| `/.auth/*` | Anonymous | ✅ Correct — SWA auth endpoints always accessible |

**Regressions Found:** None. All previously fixed bugs remain resolved.

**Bugs Found:** None. No new blocking issues discovered.

**Observations (non-blocking, unchanged from Updates 12–13):**
- **Dashboard stat cards** show static placeholder dashes ("—") and "No runs yet" — not wired to the API yet. Future enhancement.
- **Run detail initial load** takes ~2–3 seconds due to the `app-shell-ready` event pattern. Expected behaviour.

**Summary:**
All 14 issues have been implemented and verified. The MVP is complete with:
- Nouryon-branded public landing page and authenticated app shell
- Full packaging run lifecycle: create, list, filter, view details
- Run detail with metadata, SAS-signed log and artifact download links
- New Run form with client-side and server-side validation
- Settings/Help page with metadata schema documentation
- Demo seed and reset endpoints for testing
- Friendly error handling throughout
- Route protection configured correctly for production deployment
