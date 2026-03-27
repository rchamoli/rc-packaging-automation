# Update 1 — Homepage and App Layout

This is the first update to the Packaging Automation app. It replaces the starter template with a real landing page and sets up the basic structure that every future page will use.

## What changed

**New landing page** — The public homepage now describes what the app does: it automates the packaging of desktop applications into Intune Win32 format. There is a step-by-step "How It Works" section and three feature cards explaining standardized packaging, metadata-driven configuration, and automated Intune setup. A Login button takes users to the authenticated area.

**Dashboard shell** — Behind the login, there is now a dashboard page at `/app/dashboard.html`. It shows three stat cards (Total Runs, Succeeded, Failed) and a "Recent Runs" section. All values are placeholder for now — they will be wired up when the backend is built. The nav bar includes links to Dashboard, New Run, Runs, and Settings.

**Navigation** — Every page uses the same top navigation bar with the Nouryon logo on the left and links on the right. On small screens the links collapse into a "Menu" button that opens a dialog overlay.

**Design system** — All pages use the color palette, fonts, and component patterns defined in `docs/DESIGN.md`. The primary color is Nouryon blue (#00A3E0), headings use DM Sans, and body text uses Inter.

**README** — Updated to describe the project, list prerequisites, and explain how to run locally.

## What was removed

- The template landing page that described "Agentic Software Development Template"
- Template-specific Quick Actions and Getting Started sections on the old dashboard
- References to `.NET 9.0` (the project uses .NET 8)

## What is next

Future issues will add the backend API, the New Run form, the Runs list, and the Settings page.
