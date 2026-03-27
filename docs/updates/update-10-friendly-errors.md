# Update 10 — Friendly Errors and Edge Cases

**Issue:** #10 — Friendly errors and edge cases
**Date:** 2026-03-19

## What Changed

This update makes failure messages understandable and safe across the main workflow. Every API endpoint now returns consistent `{ error }` JSON on failure, and frontend pages show user-friendly banners instead of raw status codes or stack traces.

## Backend

### Standardised Error Responses

Every HTTP-triggered function (`PackagingFunctions`, `IntuneAppFunctions`) now wraps its handler body in a top-level `try / catch`. Unhandled exceptions are logged server-side and return a generic friendly message to the client — no stack traces, no internal paths, no class names.

**Before:** An unhandled exception in `ListRuns` or `GetRun` would produce a raw 500 with the ASP.NET error page or an empty body.

**After:** The client always receives `{ "error": "An unexpected error occurred while …" }` with an appropriate HTTP status code.

### MetadataReader Validation

`MetadataReader.ReadAsync` already validated missing folder, missing file, empty/null parse result, and field-level errors. This update adds:

- **`IOException` catch** — returns _"Could not read release-metadata.json. The file may be locked or inaccessible."_ instead of a raw exception message.
- **General `Exception` catch** — returns _"An unexpected error occurred while reading release-metadata.json. Please try again."_ so no internal details leak.
- **JSON parse errors** — the message now says _"The file release-metadata.json contains invalid JSON. Please check for syntax errors such as missing commas, brackets, or quotes."_ instead of forwarding the `JsonException.Message` which contained internal line/column details.

### ListRuns Now Includes errorSummary

`GET /api/packaging/runs` now includes `errorSummary` in each run object so the Runs list page can display inline failure reasons without loading each run individually.

## Frontend

### New Run Page (`new-run.html` / `newRun.ts`)

- **Run detail link fixed** — the success banner link was pointing to `/app/runs.html?id=…` instead of `/app/run-detail.html?id=…`. Corrected.
- **Dismiss button** — the error banner now has an ✕ button so the user can dismiss it and retry without refreshing the page.

### Runs List Page (`runs.html` / `runs.ts`)

- **Error summary row** — when a run has status `Failed` and an `errorSummary`, a light-red sub-row appears beneath the main row showing the reason.
- **Retry button** — the error banner now includes a "Try again" link that re-fetches the runs.

### Run Detail Page (`run-detail.html` / `runDetail.ts`)

- **Retry button** — the error banner now includes a "Try again" link that re-fetches the run details.
- Missing blobs/links already handled gracefully (shows "No log available" / "No artifact available" placeholders).

## Files Changed

| File | Change |
|------|--------|
| `api/Services/MetadataReader.cs` | Added IOException and general Exception catches with friendly messages; improved JSON error text |
| `api/Functions/PackagingFunctions.cs` | Added top-level try-catch to StartRun, ListRuns, GetRun; added errorSummary to ListRuns response |
| `api/Functions/IntuneAppFunctions.cs` | Added top-level try-catch to CreateFromRun and ResolveAppRef |
| `app/new-run.html` | Added dismiss button to error banner |
| `app/newRun.ts` | Fixed run detail link; wired dismiss button |
| `app/runs.html` | Added retry button to error banner |
| `app/runs.ts` | Added errorSummary to interface; render error sub-row for failed runs; wired retry button |
| `app/run-detail.html` | Added retry button to error banner |
| `app/runDetail.ts` | Wired retry button |
| `docs/BUILD_LOG.md` | Added Update 10 section |
| `docs/updates/update-10-friendly-errors.md` | This file |
