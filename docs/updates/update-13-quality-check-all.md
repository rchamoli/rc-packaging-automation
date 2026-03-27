# Update 13 — Quality Check: All Features

**Issue:** #13
**Date:** 2026-03-20

## What was tested

This update completes the full quality check of all remaining pages and workflows not covered by the initial smoke test (Update 12).

### New Run Form
- **Empty form validation** — submitting with no selections shows inline error messages for both Source Type ("Please select a source type") and Release Folder Path ("Release folder path is required").
- **Invalid folder path** — submitting with a non-existent folder path (e.g. `\\server\nonexistent\FakeApp\1.0.0`) returns a friendly backend error: "Release folder not found: ..." displayed in a dismissible red banner.

### Settings & Help Page
- **Desktop layout** — all sections render correctly: metadata schema table, registry and MSI JSON examples, dependencies/supersedence explanation, tool path configuration, naming conventions. The sticky "On this page" side navigation works for in-page jumping.
- **Mobile layout (375px)** — content stacks into single column, hamburger menu opens/closes correctly with all nav links present.

### Reset Demo Data
- `POST /api/demo/reset` clears all tables and blob containers, then re-seeds to the baseline: 5 packaging runs across 3 apps, 3 Intune app references. Verified the runs list returns exactly the expected data after reset.

### Run Detail Edge Cases
- **Succeeded run** — all metadata fields populated, View Run Log and Download Artifact links with SAS URLs.
- **Failed run** — error summary row shown ("Installer file not found in release folder"), log link present, "No artifact available".
- **Running run** — "No log available" and "No artifact available" messages, end date shows "—".
- **Non-existent run ID** — shows "Run not found. It may have been deleted." with Try again and Back to Runs links.
- **Missing ID parameter** — shows "No run ID specified. Please navigate from the Runs list."

### Navigation
- All desktop nav links (Dashboard, New Run, Runs, Settings) work correctly across all pages with proper active state highlighting.
- Mobile hamburger menu works on all pages, showing all links plus user name and Logout.

## Bug found and fixed

### Runs list app name filter broken for partial/display names

**Problem:** The filter on the Packaging Runs page only worked when the user typed the exact normalised PartitionKey (e.g. "chemtracker-desktop"). Typing the display name ("ChemTracker Desktop") or a partial match ("ChemTracker") returned "No runs found".

**Root cause:** `StorageService.GetRunsAsync` normalised the user input with `NormalizePartitionKey()` (lowercase + spaces→hyphens) and then used `PartitionKey eq` as an OData filter. Partial input like "ChemTracker" was normalised to "chemtracker", which didn't match the PartitionKey "chemtracker-desktop".

**Fix:** Changed to load all runs from Azure Table Storage and filter in-memory using `entity.AppName.Contains(appName, StringComparison.OrdinalIgnoreCase)`. This supports partial matches and is case-insensitive, matching user expectations. The dataset size (internal tool with bounded run count) makes in-memory filtering appropriate.

**File changed:** `api/Services/StorageService.cs` — `GetRunsAsync` method.
