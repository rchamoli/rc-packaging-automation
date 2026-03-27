# Update 9 — Settings and Metadata Help

**Date:** 2026-03-19
**Issue:** #9

## What Changed

This update adds a Settings & Help page that serves as a reference guide for metadata configuration and tool setup. The page is static (no TypeScript module needed) and follows the two-column Settings layout from DESIGN.md.

### Metadata Schema

- A table documents every field in `release-metadata.json` with its name, whether it is required or conditional, and a plain-language description.
- Required fields are marked with green badges; conditional fields (registry/MSI-specific) are marked with neutral badges.

### JSON Examples

- **Registry detection example** — a complete `release-metadata.json` for an EXE installer that uses registry key/value detection, with supersedence of a prior version.
- **MSI product code example** — a complete `release-metadata.json` for an MSI installer that uses product code detection, with a dependency on another app and multiple supersedence entries.

### Dependencies & Supersedence

- Explains the `AppName|Version` pipe-separated format for declaring dependencies and supersedence relationships.
- Shows single, multiple, and "none" examples.
- Describes how resolution works at Intune app creation time (lookup in IntuneAppRefs table by normalized app name + version).

### Tool Path Configuration

- Documents the `WIN32_PREP_TOOL_PATH` and `WIN32_PREP_TOOL_TIMEOUT_SECONDS` environment variables.
- Shows the exact command-line arguments the tool is invoked with.
- Lists common error messages (missing tool path, timeout, non-zero exit code) with explanations and remediation steps.

### Naming Conventions

- Explains how application names are normalized (lowercase, spaces → hyphens).
- Documents storage path patterns for run logs, artifacts, table partition keys, and row keys.

### Navigation

- All existing pages already had a "Settings" link in both desktop and mobile nav pointing to `/app/settings.html`. The new page marks "Settings" as the active link.

## Files Changed

| File | Change |
|------|--------|
| `app/settings.html` | **NEW** — Settings & Help page with metadata schema, JSON examples, tool path guidance, naming conventions |
| `docs/screenshots/09-settings.png` | **NEW** — Screenshot of the Settings page at 1280×720 |
| `docs/BUILD_LOG.md` | Added Update 9 section |
