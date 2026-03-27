#!/usr/bin/env node
/**
 * generate-dev-config.js — Generates staticwebapp.config.dev.json (local dev)
 * from staticwebapp.config.json (production / source of truth).
 *
 * Transformations applied:
 *   1. Remove the `auth` block (SWA CLI uses its built-in auth emulator)
 *   2. Add dev-only CSP origins (build.rapidcircle.com)
 *
 * The dev config is written to staticwebapp.config.dev.json so the
 * production config (staticwebapp.config.json) stays intact in git.
 * Point SWA CLI at the dev config:  swa start --swa-config-location .
 *
 * Usage:
 *   node scripts/generate-dev-config.js
 */
'use strict';

// Skip in CI — the production config is deployed as-is.
if (process.env.CI) {
  console.log('⏭️  Skipping generate-dev-config.js in CI');
  process.exit(0);
}

const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '..');
const SOURCE = path.join(ROOT, 'staticwebapp.config.json');
const OUTPUT = path.join(ROOT, 'staticwebapp.config.dev.json');

// ── Read source of truth ────────────────────────────────────────────
const config = JSON.parse(fs.readFileSync(SOURCE, 'utf8'));

// ── 1. Remove auth block (SWA CLI handles auth via built-in emulator) ──
delete config.auth;

// ── 2. Add dev-only CSP origins ─────────────────────────────────────
const DEV_ORIGINS = ['https://build.rapidcircle.com'];

if (config.globalHeaders?.['Content-Security-Policy']) {
  let csp = config.globalHeaders['Content-Security-Policy'];
  for (const origin of DEV_ORIGINS) {
    // Add to script-src
    csp = csp.replace(
      /script-src ([^;]+)/,
      (_, sources) => sources.includes(origin) ? `script-src ${sources}` : `script-src ${sources} ${origin}`
    );
    // Add to connect-src
    csp = csp.replace(
      /connect-src ([^;]+)/,
      (_, sources) => sources.includes(origin) ? `connect-src ${sources}` : `connect-src ${sources} ${origin}`
    );
  }
  config.globalHeaders['Content-Security-Policy'] = csp;
}

// ── Write output ────────────────────────────────────────────────────
fs.writeFileSync(OUTPUT, JSON.stringify(config, null, 2) + '\n', 'utf8');
console.log('✅ Generated staticwebapp.config.dev.json (local dev) from staticwebapp.config.json');
