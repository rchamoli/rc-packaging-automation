# Update 11 — Sample Data for Testing

## What happened

Added unified demo seed and reset endpoints that populate the system with realistic sample data for testing.

## What was added

### New demo endpoints
- **POST /api/demo/seed** — Seeds all demo data in one call: sample entities, packaging runs, Intune app references, and blob storage (logs + artifacts). Replaces the previous two-endpoint approach.
- **POST /api/demo/reset** — Clears all tables (PackagingRuns, IntuneAppRefs, SampleData) and blob containers (packaging-logs, artifacts), then re-seeds everything from scratch.

### Application data (AppDataSeeder service)
Five packaging runs across three applications:
1. **Nouryon Safety Suite v2.1.0** — Succeeded, 2 hours ago, with artifact and Intune app ref
2. **Nouryon Safety Suite v2.0.0** — Succeeded, 3 days ago, with Intune app ref
3. **ChemTracker Desktop v1.5.3** — Running, started 1 hour ago
4. **ChemTracker Desktop v1.5.2** — Succeeded, 7 days ago, with artifact and Intune app ref
5. **Lab Inventory Manager v3.0.1** — Failed, 1 day ago, with error summary

Three Intune app references for dependency/supersedence demos:
- Nouryon Safety Suite v2.1.0 and v2.0.0 (supersedence demo)
- ChemTracker Desktop v1.5.2 (dependency demo)

### Demo personas
Three customised personas (defined in `data/demo-users.csv`, `users.json`, and `mock-oidc-provider/users.json`):
- **Kai Patel** (user-packager) — IT packaging engineer
- **Lisa van der Berg** (user-appowner) — Application owner with manager role
- **Sam Okoye** (user-qa) — QA tester

### UI changes
The "Seed Demo Data" button on the home page now calls the unified `/api/demo/seed` endpoint and shows inline success or error feedback.

## Files changed
| File | Change |
|------|--------|
| `api/Services/AppDataSeeder.cs` | **NEW** — Service that seeds packaging runs, Intune app refs, and blobs |
| `api/Functions/DemoAdminFunctions.cs` | **NEW** — POST /api/demo/seed and POST /api/demo/reset |
| `data/demo-users.csv` | **NEW** — Three application-specific personas |
| `users.json` | Updated with Nouryon-specific personas |
| `mock-oidc-provider/users.json` | Updated to match solution-root users.json |
| `api/Program.cs` | Registered AppDataSeeder singleton |
| `api/Services/StorageService.cs` | Added ClearTableAsync and ClearBlobContainerAsync helpers |
| `staticwebapp.config.swa.json` | Added anonymous routes for /api/demo/seed and /api/demo/reset |
| `index.html` | Seed button calls unified /api/demo/seed endpoint |
| `docs/BUILD_LOG.md` | Added Update 11 section |
