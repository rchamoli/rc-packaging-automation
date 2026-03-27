# Design System

## Design Tone
Professional, clean, simple, modern. Designed for business users.

## Typography

### Font Pairing
| Role | Font Family | Weights | Rationale |
|------|-------------|---------|-----------|
| Headings | DM Sans | 600, 700 | Modern, clear headings that feel like a mature SaaS dashboard without being overly formal. |
| Body | Inter | 400, 500, 600 | Highly readable for forms, tables, logs, and data-heavy screens used by packagers and engineers. |

## Color System

### Brand Color Mapping
| Token | Hex | Usage |
|-------|-----|-------|
| primary | #00A3E0 | Buttons, links, active states |
| primary-light | #33B5E7 | Hover backgrounds, subtle highlights |
| primary-dark | #003A70 | Emphasis, hover for primary buttons, focus accents |
| accent | #6D6E71 | Badges, secondary highlights, subtle emphasis |
| neutral-50 | #F9FAFB | Page background |
| neutral-100 | #F3F4F6 | Table striping, subtle fills |
| neutral-200 | #E5E7EB | Borders, dividers |
| neutral-700 | #374151 | Body text |
| neutral-900 | #111827 | Headings |

### Color Usage Rules
| Context | Token / Class |
|---------|--------------|
| Page background | `bg-neutral-50` |
| Card background | `bg-white` |
| Primary button | `bg-primary text-white hover:bg-primary-dark focus:ring-primary/40` |
| Secondary button | `border border-neutral-300 text-neutral-700 hover:bg-neutral-100 focus:ring-neutral-200` |
| Links | `text-primary hover:text-primary-dark` |
| Body text | `text-neutral-700` |
| Headings | `text-neutral-900` |
| Muted text | `text-neutral-500` |
| Borders | `border-neutral-200` |
| Inputs background | `bg-white` |
| Error | `text-red-700 bg-red-50 border-red-200` |
| Warning | `text-amber-700 bg-amber-50 border-amber-200` |
| Success | `text-green-700 bg-green-50 border-green-200` |
| Info | `text-blue-700 bg-blue-50 border-blue-200` |
| Active/selected states | `bg-primary/10 text-primary` (nav, tabs, selected rows) |

## Standard Head Block

The exact HTML every page must include:

```html
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<link rel="stylesheet" href="/vendor/fonts.css">
<script src="/vendor/tailwindcss.js"></script>
<script>
tailwind.config = {
  theme: {
    extend: {
      colors: {
        primary: { DEFAULT: '#00A3E0', light: '#33B5E7', dark: '#003A70' },
        accent: '#6D6E71',
        neutral: {
          50: '#F9FAFB',
          100: '#F3F4F6',
          200: '#E5E7EB',
          500: '#6B7280',
          700: '#374151',
          900: '#111827'
        }
      },
      fontFamily: {
        heading: ['"DM Sans"', 'system-ui', 'sans-serif'],
        body: ['Inter', 'system-ui', 'sans-serif'],
      }
    }
  }
}
</script>
```

## Logo

### Placement
| Location | HTML | Notes |
|----------|------|-------|
| Navigation (top-left) | `<img src="/docs/assets/logo.jpg" alt="Nouryon Logo" class="h-8 w-auto">` | Links to home/dashboard |
| Landing page header | `<img src="/docs/assets/logo.jpg" alt="Nouryon Logo" class="h-10 w-auto">` | Prominent in hero/header |

### Logo Path
`/docs/assets/logo.jpg` — sourced from CLIENT.md

## Navigation

### Pattern
Top navigation bar

### Logo in Navigation
```html
<a href="/app/dashboard.html" class="flex items-center gap-3">
  <img src="/docs/assets/logo.jpg" alt="Nouryon Logo" class="h-8 w-auto">
  <span class="hidden sm:inline text-sm font-semibold text-neutral-900 font-heading">Packaging Automation</span>
</a>
```

### Structure
| Section | Route | Icon (optional) |
|---------|-------|-----------------|
| Dashboard | /app/dashboard.html | None (text-only) |
| New Run | /app/new-run.html | None (text-only) |
| Runs | /app/runs.html | None (text-only) |
| Settings | /app/settings.html | None (text-only) |

### Responsive Behavior
- `md` and up: horizontal nav links visible.
- Below `md`: show a “Menu” button that opens a `<dialog>` containing the same links in a vertical list; include a Close button inside the dialog. No hamburger icon library; use text button and optional inline SVG chevron.

## Page Layouts

| Page Type | Layout | Key Classes |
|-----------|--------|-------------|
| Dashboard | Page header + 3 stat cards (responsive grid) + “Recent Runs” table card below | `max-w-7xl mx-auto px-6 py-8 space-y-8` |
| List | Page header + filter bar card (app name filter, status filter) + runs table card + pagination row | `max-w-7xl mx-auto px-6 py-8 space-y-6` |
| Detail | Page header with status + two-column sections on `lg`: left (metadata summary) right (artifact/log/intune links) + log preview card | `max-w-5xl mx-auto px-6 py-8 space-y-6` |
| Form | Page header + single-column form card + inline progress/status area + recent activity hint | `max-w-2xl mx-auto px-6 py-8 space-y-6` |
| Settings | Page header + two-column on `lg`: left sub-nav card (anchors) + right content cards (schema, examples, naming/tool path) | `max-w-7xl mx-auto px-6 py-8 space-y-6` |
| Public landing | Top nav + hero (value prop + primary CTA) + 3 feature cards + footer | `max-w-7xl mx-auto px-6 py-10 space-y-12` |

## Component Patterns

### Card
```html
<div class="bg-white rounded-lg shadow-sm border border-neutral-200 p-6">
  <!-- content -->
</div>
```

### Button (Primary)
```html
<button class="bg-primary text-white px-4 py-2 rounded-lg font-medium hover:bg-primary-dark focus:outline-none focus:ring-2 focus:ring-primary/40 transition">
  Label
</button>
```

### Button (Secondary)
```html
<button class="border border-neutral-300 text-neutral-700 bg-white px-4 py-2 rounded-lg font-medium hover:bg-neutral-100 focus:outline-none focus:ring-2 focus:ring-neutral-200 transition">
  Label
</button>
```

### Table
```html
<div class="overflow-x-auto">
  <table class="w-full text-left">
    <thead>
      <tr class="border-b border-neutral-200 text-xs font-semibold text-neutral-500 uppercase tracking-wider bg-neutral-50">
        <th class="px-4 py-3">Column</th>
      </tr>
    </thead>
    <tbody class="divide-y divide-neutral-100">
      <tr class="hover:bg-neutral-50">
        <td class="px-4 py-3 text-neutral-700 text-sm">Data</td>
      </tr>
    </tbody>
  </table>
</div>
```

### Form Input
```html
<div>
  <label class="block text-sm font-medium text-neutral-700 mb-1">Label</label>
  <input class="w-full bg-white border border-neutral-300 rounded-lg px-3 py-2 text-neutral-700 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-primary/40 focus:border-primary transition" />
  <p class="mt-1 text-xs text-neutral-500">Helper text</p>
</div>
```

### Empty State
```html
<div class="text-center py-12">
  <div class="mx-auto mb-3 h-10 w-10 rounded-full bg-neutral-100 flex items-center justify-center">
    <svg viewBox="0 0 24 24" class="h-5 w-5 text-neutral-500" fill="none" stroke="currentColor" stroke-width="2">
      <path d="M12 8v4m0 4h.01M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"></path>
    </svg>
  </div>
  <p class="text-neutral-700 text-base font-medium">No items found</p>
  <p class="text-neutral-500 mt-1 text-sm">Try adjusting filters or create a new run.</p>
</div>
```

### Loading Spinner
```html
<div class="flex items-center justify-center py-8">
  <div class="animate-spin rounded-full h-8 w-8 border-2 border-neutral-200 border-t-primary"></div>
</div>
```

### Toast / Notification
```html
<!-- Success -->
<div class="bg-green-50 border border-green-200 text-green-700 px-4 py-3 rounded-lg text-sm">Message</div>
<!-- Error -->
<div class="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded-lg text-sm">Message</div>
<!-- Info -->
<div class="bg-blue-50 border border-blue-200 text-blue-700 px-4 py-3 rounded-lg text-sm">Message</div>
```

### Modal / Dialog
```html
<dialog class="rounded-xl p-0 w-full max-w-lg backdrop:bg-black/50">
  <div class="bg-white rounded-xl shadow-xl border border-neutral-200 p-6">
    <div class="flex items-start justify-between gap-4">
      <h3 class="text-lg font-semibold text-neutral-900 font-heading">Title</h3>
      <button class="text-neutral-500 hover:text-neutral-700" aria-label="Close">
        <svg viewBox="0 0 24 24" class="h-5 w-5" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M6 6l12 12M18 6L6 18"></path>
        </svg>
      </button>
    </div>
    <div class="mt-4 text-sm text-neutral-700">
      <!-- content -->
    </div>
    <div class="mt-6 flex justify-end gap-3">
      <button class="border border-neutral-300 text-neutral-700 bg-white px-4 py-2 rounded-lg font-medium hover:bg-neutral-100 transition">Cancel</button>
      <button class="bg-primary text-white px-4 py-2 rounded-lg font-medium hover:bg-primary-dark transition">Confirm</button>
    </div>
  </div>
</dialog>
```

### Page Header
```html
<div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4 mb-8">
  <div>
    <h1 class="text-2xl font-bold text-neutral-900 font-heading">Page Title</h1>
    <p class="text-neutral-500 mt-1 text-sm">Optional subtitle</p>
  </div>
  <div class="flex items-center gap-3">
    <button class="border border-neutral-300 text-neutral-700 bg-white px-4 py-2 rounded-lg font-medium hover:bg-neutral-100 transition">Secondary</button>
    <button class="bg-primary text-white px-4 py-2 rounded-lg font-medium hover:bg-primary-dark transition">Primary Action</button>
  </div>
</div>
```

### Badge / Status Pill
```html
<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">Succeeded</span>
<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-amber-100 text-amber-800">Running</span>
<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800">Failed</span>
<span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-neutral-100 text-neutral-700">Queued</span>
```

### Sidebar Nav Item
Not used (top navigation pattern selected). For any secondary in-page sub-nav (Settings anchors), use:
```html
<!-- Active -->
<a class="block px-3 py-2 rounded-lg bg-primary/10 text-primary font-medium text-sm">Section</a>
<!-- Inactive -->
<a class="block px-3 py-2 rounded-lg text-neutral-600 hover:bg-neutral-100 transition text-sm">Section</a>
```

## Spacing

| Context | Value |
|---------|-------|
| Page padding (outer) | `px-6 py-8` |
| Card/section padding | `p-6` |
| Section gap | `space-y-8` |
| Form field gap | `space-y-4` |
| Table cell padding | `px-4 py-3` |

## Responsive Breakpoints

| Breakpoint | Usage |
|------------|-------|
| `sm` (640px) | Page header stacks → row; buttons align |
| `md` (768px) | Top nav shows inline links; hide dialog menu button |
| `lg` (1024px) | Two-column detail/settings layouts |
| `xl` (1280px) | Max content width comfort for tables and dashboards |