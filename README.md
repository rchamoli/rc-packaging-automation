<p align="center">
  <img src="docs/assets/logo.jpg" alt="Nouryon Logo" height="60">
</p>

# Packaging Automation

Standardized, repeatable packaging of desktop applications into Intune Win32 format with automated Intune configuration.

**Client:** [Nouryon](docs/CLIENT.md)

## Project Status

🚧 **In Development** — This project is actively being built.

## Documentation

- [Specification](docs/SPECIFICATION.md) — Requirements and features
- [Client Information](docs/CLIENT.md) — Company details and branding
- [Design System](docs/DESIGN.md) — Tailwind config, components, and layouts
- [Build Log](docs/BUILD_LOG.md) — Development progress

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) (see `global.json`)
- [Node.js](https://nodejs.org/) (for frontend tooling)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli)
- [SWA CLI](https://azure.github.io/static-web-apps-cli/)

## Local Development

1. Clone this repository
2. Open in the VS Code Dev Container (recommended) or run `./tools/init-local-settings.sh` to set up local configuration
3. Start the dev environment — the VS Code **Start All** task launches the frontend watcher, mock OIDC provider, and SWA CLI together
4. Open `http://127.0.0.1:4280` to view the app

### Environment Variables

| Variable | Purpose |
|----------|---------|
| `STORAGE` | Azure Table / Blob Storage connection string (Azurite in dev) |
| `MOCKOIDC_CLIENT_ID` | Client ID for the mock OIDC provider (local dev only) |
| `MOCKOIDC_CLIENT_SECRET` | Client secret for the mock OIDC provider (local dev only) |

> **Note:** `local.settings.json` is gitignored. Use `local.settings.template.json` as a reference.

### Metadata File

Each packaging run is driven by a small per-release metadata file placed alongside the installer. See the [Specification](docs/SPECIFICATION.md) for the full schema.

## Architecture

- **Frontend**: TypeScript (compiled to JS), HTML, CSS — no frameworks
- **Backend**: .NET 8 Azure Functions (isolated worker, HTTP triggers only)
- **Storage**: Azure Table Storage + Blob Storage via Azurite locally
- **Auth**: Custom OIDC provider via Azure Static Web Apps

## Issues & Feedback

Please [create an issue](https://github.com/RapidCircle/rc-build-manager-desktop-app-packaging-automation/issues/new) for bugs, feature requests, or questions.

---

Built with ❤️ by [RapidCircle](https://rapidcircle.com)

