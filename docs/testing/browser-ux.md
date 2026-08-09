# Browser UX evidence harness

This harness implements [ADR-0007](../adr/adr-0007-browser-ux-evidence-and-design-authority.md):
a repository-owned Playwright + axe-core suite that serves the actual dashboard SPA
against sanitized, schema-validated API fixtures and asserts the deterministic UX
boundary (document overflow, heading structure, serious/critical accessibility
findings, accessible names, route focus, dialog keyboard behavior, and reduced
motion).

## One local command

```powershell
cd src/PitCrew.Dashboard.WebApi/ClientApp
npm run test:browser:install   # one-time: installs the Chromium browser binary
npm run test:browser           # runs the full harness
npm run test:browser:report    # opens the last HTML report
```

`npm run test:browser` builds nothing extra: it starts the Vite dev server itself
(`playwright.config.ts`'s `webServer`), serves every route against
[`e2e/mocks/fixtures.ts`](../../src/PitCrew.Dashboard.WebApi/ClientApp/e2e/mocks/fixtures.ts)
fixtures, and tears the server down afterward. No live connector, database, or
GitHub OAuth credential is required or contacted.

## What it covers

- **Route matrix**: Fleet Overview, Incidents, Node Overview, Node Administration,
  Profile Overview, Runners, Settings (General, Access, Enrollment, Diagnostics), and an unknown route
  (404), each rendered in light/dark at desktop (1440×900), wide (1280×800),
  intermediate (768×1024), strict mobile (390×844), and narrow (320×568) sizes —
  110 combinations.
- **Settings roles and states** (`e2e/settings.spec.ts`): owner, administrator,
  viewer, system administrator, tenantless access, branded loading, active primary
  and section navigation, copyable IDs, and membership/credential confirmations.
- **Responsive foundation** (`e2e/responsive.spec.ts`): zero document overflow at
  320, 390, 768, 1280, and 1440 pixels; prioritized Fleet, Incidents, Active Jobs,
  Runners, and Profile Workers summaries; keyboard-operable table regions; 44-pixel
  touch targets; 200% zoom-equivalent reflow; long identifiers; and 40% text
  expansion.
- **Named states** (`e2e/states.spec.ts`): healthy, active incident, offline/stale
  hardware, unavailable (network failure), empty (no enrolled nodes),
  permission-limited (viewer denied an owner-only route), and failed mutation
  (enrollment code creation error).
- **Interaction** (`e2e/interaction.spec.ts`): dialog Escape/Cancel keyboard behavior
  and focus return, route-change focus management, and a reduced-motion functional
  check.
- **Login and error states** (`e2e/login-and-errors.spec.ts`): the unauthenticated
  login route and a session-bootstrap-failure route (`GET /api/session` 500 → the
  `role="alert"` retry surface).
- **Axe classification proof** (`e2e/axe-classification.spec.ts`): pure-function tests
  (no browser page) proving the axe baseline allowlist is scoped to exact known
  violation nodes, not whole rule IDs — a synthetic second `color-contrast` node is
  asserted to be classified `unexpected`, never silently tolerated.
- **Fixture schema validation** (`e2e/fixture-validation.spec.ts`): proves the fixture
  builders actually enforce the production Zod schemas (a malformed override is
  rejected, not silently cast through).

Every scenario is built from sanitized fixture builders in
[`e2e/mocks/fixtures.ts`](../../src/PitCrew.Dashboard.WebApi/ClientApp/e2e/mocks/fixtures.ts)
that are validated with `.parse()` against the same Zod schemas the production API
clients use — the mock layer cannot silently drift from the real contract. All
identifiers, hostnames, and credentials in fixtures are synthetic.

## Baseline exceptions

Issues **#86** and **#87** resolved the pre-existing contrast, heading, motion, and
document-overflow defects that formed the initial ADR-0007 baseline. The allowlist sets
(`KNOWN_BASELINE_COLOR_CONTRAST_NODE_HTML` and
`KNOWN_BASELINE_COLOR_CONTRAST_COLOR_PAIRS` in
[`e2e/support/axe.ts`](../../src/PitCrew.Dashboard.WebApi/ClientApp/e2e/support/axe.ts))
are now empty. Classification still uses the same narrow machinery so that any future
pre-existing defect can be recorded without hiding new regressions.

There are currently no accepted browser baseline exceptions. Every route, viewport,
rule, and node hard-fails on serious/critical axe findings, a non-sequential heading
outline, or document-level horizontal overflow. Issue **#91** owns promoting this
zero-tolerance evidence from advisory to required CI after the remaining hardening
work is complete.

## CI

[`.github/workflows/browser-ux.yml`](../../.github/workflows/browser-ux.yml) runs the
same `npm run test:browser` command on pull requests that touch the dashboard SPA, and
uploads screenshots, metrics, axe reports, and the Playwright HTML report as build
artifacts. It also runs the Impeccable detector and uploads its JSON output; detector
_findings_ are advisory, but the workflow fails if the detector itself cannot execute.
Artifacts are retained for seven days. The HTML report is the canonical browser report;
the workflow does not also generate or upload Playwright's duplicate JSON reporter.

This workflow is intentionally advisory: it is not listed in
`.github/genesis-delivery.json`'s `requiredChecks`, so it cannot block merges. Issue
#91 owns promoting the deterministic browser evidence to required CI after the
remaining baseline defects and hardening work are complete,
independently of this workflow's name or job structure.
