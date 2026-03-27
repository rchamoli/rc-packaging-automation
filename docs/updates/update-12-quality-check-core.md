# Update 12 — Quality Check: Core Features

## What Happened

This update validates the most critical user workflows end-to-end using browser automation against seeded demo data. The goal was to confirm that the core feature set delivered across Issues #1–#11 works together as an integrated whole — seed data flows into the UI correctly, navigation works, and detail pages render real content with working links.

## What Was Tested

### 1. Landing Page → Seed Demo Data

Opened the public homepage at `/`, clicked the **Seed Demo Data** button in the navigation bar. The button:
- Shows "Seeding…" while the request is in flight
- Calls `POST /api/demo/seed` (the unified endpoint from Issue #11)
- Displays "✓ Seeded!" on success
- Returns `{ sampleSeeded: 4, runsSeeded: 5, appRefsSeeded: 3 }`

### 2. Login → Dashboard

Authenticated as **Kai Patel** (packager persona) and navigated to `/app/dashboard.html`. The dashboard:
- Shows "Welcome, Kai" as the page heading
- Displays "Kai Patel" in the navigation bar (desktop)
- All navigation links work: Dashboard, New Run, Runs, Settings, Logout
- Quick action tiles link to New Run and Runs pages

### 3. Runs List

Navigated to `/app/runs.html`. The seeded data renders correctly:
- 5 runs displayed in a table with status badges, app names, versions, source types, and timestamps
- **Running** run (ChemTracker Desktop 1.5.3) shows amber badge, no end time
- **Succeeded** runs (Nouryon Safety Suite 2.1.0/2.0.0, ChemTracker Desktop 1.5.2) show green badges
- **Failed** run (Lab Inventory Manager 3.0.1) shows red badge with inline error sub-row: "Installer file not found in release folder"
- Each run has a "View →" link to its detail page

### 4. Run Detail — Succeeded

Opened the Nouryon Safety Suite 2.1.0 run detail (`/app/run-detail.html?id=a1b2c3d4e5f6`):
- All fields populated: App Name, Version, Status, Source Type, Started, Ended, Source Location, Metadata File
- Intune App status shows "Created" with the app ID
- **View Run Log** link renders with a working Azurite SAS URL
- **Download Artifact** link renders with a working Azurite SAS URL
- Breadcrumb navigation back to Runs works

### 5. Run Detail — Failed

Opened the Lab Inventory Manager 3.0.1 run detail (`/app/run-detail.html?id=d4e5f6a1b2c3`):
- Error summary displayed: "Installer file not found in release folder"
- View Run Log link available (log exists even for failed runs)
- "No artifact available" shown correctly (failed run has no artifact)

### 6. Run Detail — Running

Opened the ChemTracker Desktop 1.5.3 run detail (`/app/run-detail.html?id=c3d4e5f6a1b2`):
- Running status badge shown
- End time shows "—" (not yet completed)
- "No log available" and "No artifact available" shown correctly
- Intune App status shows "Requested"

## Bugs Found

**None.** All critical workflows work end-to-end without blocking bugs. No JavaScript console errors were observed.

## Observations (Non-Blocking)

1. **Dashboard stat cards** — The Total Runs, Succeeded, and Failed counters on the dashboard show static placeholder dashes ("—"). These are static HTML, not wired to the API. The "Recent Runs" section also shows a static empty state. This is a known future enhancement, not a regression.

2. **Run detail load time** — Detail pages take ~2–3 seconds to fully render due to the `app-shell-ready` event pattern where the authentication check must complete before page-level scripts initialise. This is the expected design pattern and not a performance issue.

## Files Changed

| File | Change |
|------|--------|
| `docs/BUILD_LOG.md` | Added Update 12 section with testing results |
| `docs/updates/update-12-quality-check-core.md` | This file — testing summary |
