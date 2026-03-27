# Update 8 — Intune Setup from Metadata

**Date:** 2026-03-19
**Issue:** #8

## What Changed

This update adds automated Intune Win32 app creation from completed packaging runs. When a packaging run succeeds and `createIntuneApp` is true, the system creates a Win32 app in Intune with detection rules, dependencies, supersedence, and UAT assignment — all derived from the release metadata.

### Intune App Creation

- **IntuneGraphService** uses Microsoft Graph SDK with client credentials (GRAPH_TENANT_ID, GRAPH_CLIENT_ID, GRAPH_CLIENT_SECRET) to create Win32LobApp entries in Intune.
- The service builds the app with display name, install/uninstall commands, and detection rules from metadata.
- After creation, it uploads the `.intunewin` content via the Graph content version flow (content version → content file → poll for Azure Storage URI → block upload → commit).
- The created app's ID and Intune portal link are stored on the PackagingRunEntity and in a dedicated IntuneAppRefs table.

### Detection Rules

- **Registry detection** (primary): Built from metadata fields `registryPath`, `registryValueName`, `registryArchitecture`, `registryRuleType`, and `registryExpectedValue`. Added when `detectionType` is "registry".
- **MSI product code** (secondary): Added only when `installerType` is "MSI" and `msiProductCode` is provided.
- A fallback exists-check rule is added if no specific rules were generated.

### Dependencies and Supersedence

- Dependencies and supersedence are parsed from metadata in "AppName|Version,AppName|Version" format.
- Each target is resolved by looking up the IntuneAppRefs table by app name and version.
- Relationships are applied via raw HTTP calls to the Graph API's `updateRelationships` action.
- "none" values are treated as no relationships to configure.

### UAT Assignment

- The UAT group from metadata is resolved (GUID or display name lookup) and the app is assigned with `Available` intent.
- Group display names are resolved to Azure AD group IDs via the Graph Groups API.

### Reference Mapping

- **IntuneAppRefEntity** stores the mapping between app name/version and Intune app ID in the `IntuneAppRefs` table.
- PartitionKey = normalized app name, RowKey = version.
- Used for dependency/supersedence resolution across packaging runs.

### API Endpoints

- **POST /api/intune/create-from-run/{runId}** — Creates an Intune Win32 app from a completed packaging run. Validates the run succeeded, reads metadata, creates the app, and returns `{ intuneAppId, intuneAppLink, runId, appName, version }`.
- **GET /api/intune/appref/resolve?appName=...&version=...** — Resolves an Intune app reference by app name and version from the IntuneAppRefs table.

### Automatic Intune Creation

- **PackagingService** now calls `IntuneGraphService.CreateFromRunAsync` after packaging succeeds when `createIntuneApp` is true.
- Intune creation failures are logged but do not fail the packaging run.
- The `intuneAppId` is returned in the POST /api/packaging/run response.

## Files Changed

| File | Change |
|------|--------|
| `api/Models/IntuneAppRefEntity.cs` | **NEW** — Table Storage entity for Intune app reference mapping |
| `api/Services/IntuneGraphService.cs` | **NEW** — Graph API integration for Win32 app creation, content upload, detection rules, relationships, and UAT assignment |
| `api/Functions/IntuneAppFunctions.cs` | **NEW** — POST create-from-run and GET appref/resolve endpoints |
| `api/Utilities/TableNames.cs` | Added `IntuneAppRefs` table constant |
| `api/Services/StorageService.cs` | Added `GetIntuneAppRefsTableAsync`, `UpsertIntuneAppRefAsync`, and `GetIntuneAppRefAsync` methods |
| `api/Services/PackagingService.cs` | Added IntuneGraphService dependency; calls Intune creation after successful packaging |
| `api/Functions/PackagingFunctions.cs` | Added `intuneAppId` to StartRun response |
| `api/Program.cs` | Registered IntuneGraphService in DI container |
| `api/api.csproj` | Added Microsoft.Graph 5.74.0 and Azure.Identity 1.17.0 NuGet packages |
| `docs/BUILD_LOG.md` | Added Update 8 section |
