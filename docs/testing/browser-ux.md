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

Five pre-existing, empirically confirmed defects are recorded as ADR-0007 baseline
evidence rather than silently skipped or hard-failed. Each is scoped to the _exact_
known finding, not a whole rule or route class, so a new/different instance of the
same rule ID still fails the suite:

- **`color-contrast` on the brand "Dashboard" teal label** (serious axe finding,
  tracked in **#86**): the sidebar/hero brand-teal-on-background pairing
  (`#13989f` on `#fafafa`, ratio 3.34:1) fails WCAG AA. Classification is scoped to
  the two exact known node `outerHTML` strings in
  `KNOWN_BASELINE_COLOR_CONTRAST_NODE_HTML` in
  [`e2e/support/axe.ts`](../../src/PitCrew.Dashboard.WebApi/ClientApp/e2e/support/axe.ts)
  — any other `color-contrast` violation node, anywhere, is `unexpected`. Proven by
  `e2e/axe-classification.spec.ts`.
- **`color-contrast` on the dark-theme destructive token** (serious axe finding,
  tracked in **#86**): `--destructive` resolves to `#ff6467` with white text, a
  2.88:1 ratio below the WCAG AA 4.5:1 threshold. Because the token can recur on
  actions with different labels, classification matches the exact reported
  `(bgColor, fgColor)` hex pair via
  `KNOWN_BASELINE_COLOR_CONTRAST_COLOR_PAIRS` in `e2e/support/axe.ts` — still narrow
  and exact, so any other pair remains `unexpected`. Proven by
  `e2e/axe-classification.spec.ts`.
- **Missing `<h1>` on the session-error route** (moderate axe finding, tracked in
  **#86**): the route renders before `AuthenticatedShell` mounts and still uses
  `CardTitle`'s non-heading default. The login route now selects `as="h1"` and is
  asserted as compliant. See `e2e/login-and-errors.spec.ts`.
- **No reduced-motion-specific behavior on the confirmation dialog** (tracked in
  **#86**): the dialog's `animate-in`/`fade-in-0`/`zoom-in-95` classes have no
  registered Tailwind utility behind them in this project (no
  `tailwindcss-animate`/`tw-animate-css` dependency), so the computed style shows no
  animation at all — with or without `prefers-reduced-motion`. Measured directly in
  `e2e/interaction.spec.ts` rather than assumed.
- **Document overflow on Runners (all viewports) and Incidents (intermediate only)**
  (tracked in **#87**): both pages wrap a `min-w-6xl` data table in an
  `overflow-x-auto` container that is a direct grid-item child of
  `AuthenticatedShell`'s `<main>`; CSS grid items default to `min-width: auto`, so the
  table's intrinsic width still escapes to the document instead of staying confined
  to its own scrollbar. See `KNOWN_BASELINE_OVERFLOW_ROUTES` in
  [`e2e/routes.spec.ts`](../../src/PitCrew.Dashboard.WebApi/ClientApp/e2e/routes.spec.ts).

Every allowlist above is narrow, named, and commented at the point of use. Every
route/viewport/rule/node combination _not_ on an allowlist still hard-fails on any
regression. Representative route assertions also require each color-contrast baseline
to remain observable, so fixing a defect fails the stale assertion and forces its
allowance to be removed. Turning these into required, zero-tolerance gates is tracked
in issues **#86** (contrast, heading, motion) and **#87** (overflow); this harness does
not decide that timeline.

## CI

[`.github/workflows/browser-ux.yml`](../../.github/workflows/browser-ux.yml) runs the
same `npm run test:browser` command on pull requests that touch the dashboard SPA, and
uploads screenshots, metrics, axe reports, and the Playwright HTML report as build
artifacts. It also runs the Impeccable detector and uploads its JSON output; detector
_findings_ are advisory, but the workflow fails if the detector itself cannot execute.

This workflow is intentionally advisory: it is not listed in
`.github/genesis-delivery.json`'s `requiredChecks`, so it cannot block merges. Issue
#91 owns promoting the deterministic browser evidence to required CI after the
remaining baseline defects and hardening work are complete,
independently of this workflow's name or job structure.
