# Standardized Desktop App Packaging Automation (Intune Win32)

## Problem
Packaging desktop applications into Intune Win32 format and configuring them in Intune is manual and inconsistent. Teams start from vendor installers (EXE/MSI/ZIP) or shared project folders, then run the Win32 Content Prep Tool to produce *.intunewin. The most time-consuming and error-prone part is manually creating/maintaining install, uninstall, and release-specific detection logic and then configuring these in Intune (detection, dependencies, supersedence), plus creating the Win32 app in a UAT state.

## MVP Scope
Create a standardized, repeatable workflow that:
- Accepts defined inputs from (a) vendor-provided installer bundles (EXE/MSI/ZIP) or (b) shared project folders
- Requires a small per-release metadata file placed alongside the installer/project folder to provide packaging + Intune configuration logic
- Produces an Intune Win32 output artifact (*.intunewin) using the Microsoft Win32 Content Prep Tool
- Writes outputs next to the source release folder:
  - *.intunewin
  - Run logs (and optionally a run summary)
- Automates Intune Win32 app creation/configuration into a UAT state by:
  - Creating Windows App (Win32)
  - Uploading *.intunewin
  - Configuring install command + uninstall command (from metadata)
  - Configuring detection rules (primary: registry key/value; release-specific; secondary: MSI product code only for true MSI)
  - Configuring dependencies and supersedence (from metadata; can be “none”), referencing targets by **App Name + Version**
  - Assigning the created app to a **UAT group provided in metadata** (this defines UAT state)
- Runs on-demand with clear pass/fail status and logs

## Target Users
| User | Primary Need |
|------|--------------|
| Build/Release Engineer / Packager | Run packaging + Intune setup reliably with repeatable outputs and fewer manual steps |
| Application Owner | Provide required install/uninstall/detection info per release and know packaging + Intune setup succeeded |
| QA/Tester | Verify the created UAT app is correct and the artifact/version matches expectations |

## Core Features (v1)
### Feature 1: Standard packaging job to *.intunewin
**User Story**: As a packager, I want to run a packaging job for an application version so that I consistently get an *.intunewin artifact.
**Acceptance Criteria**:
- Job accepts source type: vendor installer bundle (EXE/MSI/ZIP) or project folder
- Job accepts a source location (release folder)
- Job reads a per-release metadata file from the source location
- Job produces an *.intunewin artifact next to the source release folder
- Job outputs a human-readable log next to the source release folder
- Job outputs clear success/failure status

### Feature 2: Per-release metadata file to drive packaging + Intune configuration
**User Story**: As a packager, I want install/uninstall/detection/dependency/supersedence/UAT inputs captured alongside the release inputs so that deployments are consistent and repeatable.
**Acceptance Criteria**:
- Each release includes a small metadata file placed alongside the installer/project folder
- Metadata required fields (v1 minimum):
  - **Application Name** (required)
  - **AN Number** (required)
  - **Release Version** (required)
  - **Installer Type**: EXE or MSI (required)
  - **Install Command** (required)
  - **Uninstall Command** (required)
  - **Detection Type** (required)
  - For **registry-based detection** (primary; release-specific; changes per release):
    - Registry Hive (required)
    - Registry Path (required)
    - Registry Architecture Context (required)
    - Registry Rule Type: Key or Value (required)
    - Registry Value Name + Expected Value (required only if value-based)
  - For **MSI detection** (secondary; only when true MSI):
    - MSI Product Code (required if MSI detection used)
  - **UAT Group** (required; used to assign the app and define UAT state)
  - **Dependencies** (required field; can be “none”; references targets by App Name + Version)
  - **Supersedence** (required field; can be “none”; references targets by App Name + Version)

### Feature 3: Automated Intune Win32 app creation/configuration (UAT)
**User Story**: As a packager, I want the Win32 app created and configured in Intune automatically so that UAT apps are set up correctly every time.
**Acceptance Criteria**:
- System creates a new Windows App (Win32) entry in Intune for the given app/version (naming convention TBD)
- System uploads the generated *.intunewin
- System configures install and uninstall commands from metadata
- System configures detection rules based on metadata:
  - Registry key/value (primary; release-specific values)
  - MSI product code (secondary; only for true MSI)
- System configures dependencies and supersedence per provided inputs (from metadata; can be “none”); targets identified by App Name + Version
- System assigns the app to the UAT group specified in metadata (this defines “UAT state”)
- System returns a link/identifier for the created Intune app plus a success/failure status and logs

## Key Workflows
### Run packaging + Intune setup for a release (UAT)
1. User provides/chooses a source release folder (vendor installer bundle or shared project folder)
2. System loads per-release metadata file from the source location (identity + installer type + install/uninstall + detection inputs + UAT group + dependencies + supersedence)
3. System runs Microsoft Win32 Content Prep Tool to produce *.intunewin (output next to source release folder)
4. System creates/configures Win32 app in Intune including upload, install/uninstall commands, detection, dependencies, supersedence
5. System assigns the app to the UAT group (from metadata)
6. System writes logs (next to source release folder) and returns created app identifier/link + status

## Future Considerations (post-MVP)
- AI-assisted suggestions for install/uninstall/detection logic
- Auto-generation of rules from installer analysis
- Approval workflow and audit trail
- Notifications and dashboarding
- Signing/checksums
- Promotion workflow from UAT to Prod (beyond initial UAT creation)
- More robust dependency/supersedence resolution (e.g., stable IDs) if naming proves insufficient

---
<details>
<summary>Technical Notes</summary>

### Data Entities
| Entity | Key Attributes |
|--------|---------------|
| Application | App name, AN number |
| Release Metadata | App name, AN number, release version, installer type, install command, uninstall command, detection type, registry detection fields (hive/path/arch/rule/value) OR MSI product code; UAT group; dependencies; supersedence |
| Packaging Run | App, version/build, source type, source location, start/end time, status, log path, output artifact path, metadata file reference, Intune app ID/link |

### Integrations
- Microsoft Win32 Content Prep Tool (create *.intunewin)
- Endpoint Manager / Intune (create/configure Win32 app)

### Constraints
- Must support inputs from vendor installers (EXE/MSI/ZIP) and shared project folders
- Primary detection is **registry-based** and is **release-specific**
- MSI product code detection only when installer is true MSI (secondary option)
- UAT state is represented by assignment to a UAT group provided in metadata
- Output artifact and logs are written next to the source release folder
- Dependency and supersedence targets are identified by App Name + Version (naming convention must be consistent; exact convention TBD)
- Security/access controls for source locations and Intune permissions (TBD)

</details>