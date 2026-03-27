<p align="center">
  <img src="../docs/assets/logo.jpg" alt="Nouryon Logo" height="80">
</p>

# Nouryon

**Website:** [https://nouryon.com](https://nouryon.com)

## About Us

Nouryon is a global specialty chemicals company that develops and supplies essential chemical products and solutions. Its portfolio includes bleaching and oxidizing chemicals (e.g., hydrogen peroxide, sodium chlorate), specialty polymers, surfactants and chelating agents, polymer production/processing chemicals (e.g., organic peroxides, additives), and specialty intermediates (e.g., colloidal silica, chromatography media). Nouryon serves industrial and consumer-facing sectors such as agriculture and food, home and personal care, paints and coatings, building and infrastructure, polymer specialties, oil/gas/mining, pulp/paper/packaging, transportation, and other markets like batteries, electronics, pharma, and water treatment. The company emphasizes sustainability, safety, innovation, and responsible sourcing, and offers customer tools/portals plus documentation and SDS access.

## Brand Colors

Use these colors to maintain brand consistency throughout the application.

| Preview | Hex Code | CSS Variable Suggestion |
|---------|----------|------------------------|
| ![#00A3E0](https://img.shields.io/badge/-00A3E0-00A3E0?style=flat-square) | `#00A3E0` | `--brand-primary` |
| ![#003A70](https://img.shields.io/badge/-003A70-003A70?style=flat-square) | `#003A70` | `--brand-secondary` |
| ![#FFFFFF](https://img.shields.io/badge/-FFFFFF-FFFFFF?style=flat-square) | `#FFFFFF` | `--brand-accent` |
| ![#6D6E71](https://img.shields.io/badge/-6D6E71-6D6E71?style=flat-square) | `#6D6E71` | `--brand-highlight` |

### CSS Variables

```css
:root {
  --brand-primary: #00A3E0;
  --brand-secondary: #003A70;
  --brand-accent: #FFFFFF;
  --brand-highlight: #6D6E71;
}
```

### Tailwind CDN Inline Config

Paste this `<script>` tag immediately after loading `/vendor/tailwindcss.js` on every HTML page.
This maps brand colors to Tailwind utility classes like `bg-primary`, `text-accent`, etc.

```html
<script>
  tailwind.config = {
    theme: {
      extend: {
        colors: {
          'primary': '#00A3E0',
          'secondary': '#003A70',
          'accent': '#FFFFFF',
          'highlight': '#6D6E71'
        }
      }
    }
  }
</script>
```

## Logo

![Nouryon Logo](../docs/assets/logo.jpg)

**Logo Path:** `docs/assets/logo.jpg`

---

*This document was auto-generated from the client's website. Please verify and update as needed.*
