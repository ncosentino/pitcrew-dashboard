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

`npm run test:browser` builds the production SPA and starts Vite preview through
`playwright.config.ts`'s `webServer`, serves every route against
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
- **Fleet and incident interpretation states** (`e2e/incidents.spec.ts`):
  critical/warning mixes, connector evidence present and unavailable, acknowledged,
  resolved, truncated history, severity-aware primary navigation, and labeled
  capacity evidence.
- **Node, Profile, and Runners investigation states**
  (`e2e/node-profile-runners.spec.ts`): healthy, degraded, pressure, active job,
  rolling image, recovery, read-only, and unavailable evidence in light/dark desktop
  and mobile modes, plus entity headings, copyable IDs, advanced filter disclosure,
  active filter chips, result counts, and clear-all.
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
- **Production hardening** (`e2e/hardening.spec.ts`): keyboard-only primary tasks,
  200% zoom-equivalent reflow, forced colors, reduced motion, long and expanded text,
  CJK/emoji/RTL content, one-item and large datasets, slow responses, and aborted
  requests.
- **Performance budgets** (`e2e/performance-budget.spec.ts`): navigation timing,
  production JavaScript transfer size, and the enforced 100-node pagination threshold.

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
outline, or document-level horizontal overflow.

## CI

[`.github/workflows/browser-ux.yml`](../../.github/workflows/browser-ux.yml) owns the
required `Browser UX` check. Runtime and UI pull requests run the same
`npm run test:browser` command and upload screenshots, metrics, axe reports, and the
Playwright HTML report. Guidance-only pull requests run the shared path resolver and
return the required check without installing browsers or generating artifacts.

The Impeccable detector runs in the required workflow, but its findings remain advisory
judgment evidence; detector execution failure still fails the check. Artifacts are
retained for seven days. The HTML report is canonical, and the duplicate Playwright
JSON reporter is not generated.
