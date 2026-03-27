# Update 5 — Runs List

**Issue:** #5 — Runs list

Added a Runs list page and backend endpoint so users can view recent packaging runs with status, app name, version, and timestamps.

### New Files
- **`app/runs.html`** — Runs list page using the List layout from DESIGN.md (`max-w-7xl mx-auto`). Includes a responsive table with status badges, an app name filter bar, an empty state with a link to start a new run, and an error state for API failures.
- **`app/runs.ts`** — TypeScript module that fetches runs from `GET /api/packaging/runs`, renders the table with status badges (Succeeded=green, Failed=red, Running=amber), handles the filter bar (Apply/Clear/Enter key), and manages loading/empty/error states. Waits for the `app-shell-ready` event before initialising.
- **`docs/screenshots/05-runs-list.png`** — Screenshot of the Runs list page at 1280×720.
- **`docs/updates/update-5-runs-list.md`** — This file.

### Updated Files
- **`api/Functions/PackagingFunctions.cs`** — Added `ListPackagingRuns` function with `GET /api/packaging/runs` route. Accepts optional `?appName=` query parameter. Returns `{ runs: [{ id, appName, version, status, startTime, endTime, sourceType, logUrl }] }`. Injected `StorageService` into the constructor alongside `PackagingService`.
- **`api/Services/StorageService.cs`** — Added `GetRunsAsync(appName?, maxResults)` method. Queries the `PackagingRuns` table with optional PartitionKey filter (normalised app name). Returns up to 50 results sorted by start time descending.
- **`.gitignore`** — Added `app/runs.js` and `app/runs.js.map` to exclude compiled TypeScript output.
- **`docs/BUILD_LOG.md`** — Added Update 5 section.

### Design Decisions
- **Existing nav links** — The "Runs" link to `/app/runs.html` was already wired in the dashboard and new-run page nav bars from previous updates; the runs page marks it as the active link.
- **Client-side filtering** — The filter bar sends the `appName` parameter to the API, which filters by PartitionKey for efficient Table Storage queries rather than fetching all runs and filtering client-side.
- **HTML escaping** — All user-provided text (app name, version, source type) is escaped via `escapeHtml()` before insertion into the DOM to prevent XSS.
- **Sort order** — Runs are sorted by start time descending (most recent first) server-side, matching user expectations for a "recent runs" view.
- **Status badges** — Use the colour mapping from DESIGN.md: Succeeded (green), Failed (red), Running (amber), other (neutral).
