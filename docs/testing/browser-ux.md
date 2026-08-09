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
  Profile Overview, Runners, Settings (General, Enrollment), and an unknown route
  (404), each rendered at desktop (1440×900) light/dark, an intermediate viewport
  (768×1024), and strict mobile (390×844) — 54 combinations.
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

## Baseline exceptions (advisory, not hidden)

Issue **#86** resolved the four pre-existing defects that previously formed the
contrast/heading/motion baseline. The allowlist sets
(`KNOWN_BASELINE_COLOR_CONTRAST_NODE_HTML` and
`KNOWN_BASELINE_COLOR_CONTRAST_COLOR_PAIRS` in
[`e2e/support/axe.ts`](../../src/PitCrew.Dashboard.WebApi/ClientApp/e2e/support/axe.ts))
are now empty. Classification still uses the same narrow machinery so that any future
pre-existing defect can be recorded without hiding new regressions.

Remaining baseline exception:

- **Document overflow on Runners (all viewports) and Incidents (intermediate only)**
  (tracked in **#87**): both pages wrap a `min-w-6xl` data table in an
  `overflow-x-auto` container that is a direct grid-item child of
  `AuthenticatedShell`'s `<main>`; CSS grid items default to `min-width: auto`, so the
  table's intrinsic width still escapes to the document instead of staying confined
  to its own scrollbar. See `KNOWN_BASELINE_OVERFLOW_ROUTES` in
  [`e2e/routes.spec.ts`](../../src/PitCrew.Dashboard.WebApi/ClientApp/e2e/routes.spec.ts).

Every allowlist above is narrow, named, and commented at the point of use. Every
route/viewport/rule/node combination _not_ on an allowlist still hard-fails on any
regression. Turning the remaining overflow baseline into a required, zero-tolerance
gate is tracked in issue **#87**; this harness does not decide that timeline.

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
