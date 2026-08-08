---
target: current main dashboard UI
total_score: 24
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 5
timestamp: 2026-08-08T17-34-03Z
slug: src-pitcrew-dashboard-webapi-clientapp-src
---
# PitCrew Dashboard UI/UX Audit - Current Main

Method: dual-agent (A: `main-design-audit`; B: `main-technical-audit`) plus parent browser completion

Revision: `main` / `0469fd513134fea3cd4119f82bdff0d0bdb1bd7b`

## Evidence and limitations

- Actual React, routing, CSS, components, and production build from current `main`.
- Current-contract API data was mocked from repository schemas and tests because no live connector
  state root was available.
- Browser evidence used headless Chrome at 1440x900-1100 and strict 390x844 viewports.
- Light and dark themes were inspected.
- Representative routes: Fleet, Incidents, Node overview, Profile overview, Workers, Runners,
  Access settings, Diagnostic credentials, login, and session error.
- No real screen reader, physical phone, or production-size fleet was available.

Rendered artifacts and metrics were captured as session-only evidence and are not committed.

## Implementation integrity verdict

**Pass for product specificity; fail for professional-finish readiness.**

The interface now expresses a coherent PitCrew-specific operational model. Active incidents,
connector outage evidence, host pressure, workload attribution, fenced pause/recovery behavior,
hardware state, and explicit unavailable/last-known evidence are not interchangeable SaaS filler.

The remaining gap is system quality: several core routes break the page viewport, AA contrast
fails in shared tokens, dense card titles are not semantic headings, action safety is inconsistent,
and hierarchy still promotes raw identifiers and repeated headings over operator tasks.

## Audit Health Score

| # | Dimension | Score | Key finding |
|---|---|---:|---|
| 1 | Accessibility | 2/4 | AA contrast failures and weak semantic heading coverage |
| 2 | Performance | 3/4 | Good lazy split and bundle size; large unvirtualized tables and overlapping polling remain risks |
| 3 | Responsive design | 1/4 | Node, Runners, and Workers create document-level horizontal overflow |
| 4 | Theming | 2/4 | Strong token/dark-mode foundation, but shared teal/orange roles fail AA |
| 5 | Implementation integrity | 3/4 | Product-specific and truthful, with several systemic presentation defects |
| **Total** |  | **11/20** | **Acceptable - significant work needed** |

## Design Health Score

| # | Nielsen heuristic | Score | Key issue |
|---|---|---:|---|
| 1 | Visibility of system status | 3/4 | Strong incident/stale/last-known feedback; loading remains plain |
| 2 | Match system / real world | 3/4 | Accurate operator language, but raw IDs and internal terms dominate |
| 3 | User control and freedom | 2/4 | Some shared-state and credential actions have no undo or confirmation |
| 4 | Consistency and standards | 3/4 | Strong shared status system; settings selection and form presentation drift |
| 5 | Error prevention | 2/4 | Fenced host actions are good; diagnostic rotate/revoke bypass that standard |
| 6 | Recognition rather than recall | 2/4 | Slash-separated metrics and dense status combinations require parsing |
| 7 | Flexibility and efficiency | 3/4 | Density, filters, direct routes, hardware compare, and diagnostics help experts |
| 8 | Aesthetic and minimalist design | 2/4 | Better route focus, but repeated cards/headings and scrollbar clutter remain |
| 9 | Error recognition and recovery | 3/4 | Stale evidence is preserved; some failures lack a direct recovery action |
| 10 | Help and documentation | 1/4 | Little contextual help for operational terms or credential scope |
| **Total** |  | **24/40** | **Acceptable - major UX pass required** |

## Detector evidence

### CLI detector

One finding:

- `border-accent-on-rounded`
- `EntitySectionNavigation.tsx:32`
- snippet: `border-b-2`

This is a false positive. The element is a standard tab underline and has no rounded card border.

### Browser detector

Mutable injection succeeded on Fleet, Incidents, and Profile in a fresh headless browser.
No user-visible Human tab was presented.

- `low contrast text`: true positive; mapped to the 12px teal Dashboard brand label.
- `cramped padding`: false positive; mapped to table cells with adequate 8px/16px padding and tall
  multi-line rows.
- `all-caps body text`: false positive; mapped to table column headers.
- `nested cards`: advisory visual-density signal; mapped to retained connector evidence inside an
  incident cell and the inset worker metrics panel.
- `overused font`: false positive for Operate mode; a single UI family is appropriate.

## Issue counts

- P0: 0
- P1: 5
- P2: 7
- P3: 2

## Priority findings

### P1 - Core operational routes break the page viewport

Measured document overflow:

| Route | Viewport | Document overflow |
|---|---:|---:|
| Node overview | 390px | 220px |
| Runners | 1440px | 229px |
| Runners | 390px | 991px |
| Profile Workers | 1440px | 571px |
| Profile Workers | 390px | 1333px |

Locations:

- `features/runners/RunnersPage.tsx:254,444-445`
- `features/fleet/components/ProfileSlotsTable.tsx:131-138`
- `features/fleet/components/NodePressureCommandCenter.tsx:179,313`

The inner scroll regions exist, but ancestor grid items retain their min-content width. This
widens the entire document, separates tables from the shell, and makes mobile triage difficult.

Fix every containment boundary with `min-w-0`, ensure the scroll owner itself fits its container,
and provide narrow-screen row/card summaries for the highest-value fields.

Suggested command: `/impeccable adapt`

### P1 - Page hierarchy promotes identifiers and repeats titles

Rendered examples:

- `Node <UUID> overview` is the H1 while `Build host alpha` is H2.
- Fleet renders H1 `Runner fleet` and H2 `Fleet status`.
- Incidents renders `Operational incidents` as both H1 and H2.
- Runners renders H1 `Runners` and H2 `Runners and slots`.

Locations:

- `features/fleet/manifest.tsx:108`
- `core/routing/AuthenticatedShell.tsx:264-266`
- `features/fleet/pages/NodePages.tsx:203-204`
- `features/fleet/FleetOverviewPage.tsx:197`
- `features/fleet/IncidentsPage.tsx:195`
- `features/runners/RunnersPage.tsx:256`

Use the human display name as the node title, move stable IDs to copyable metadata, and keep one
visible page heading with one purpose statement.

Suggested commands: `/impeccable layout`, `/impeccable clarify`

### P1 - Dense sections are visually titled but not semantic headings

`CardTitle` renders a `div` (`components/ui/card.tsx:31-35`). There are 31 CardTitle uses but only
25 explicit semantic headings across production TSX. On Node and Profile pages, screen-reader
heading navigation cannot jump directly to Node identity, Host hardware, Connector outage
evidence, Operational health, Workers and recovery, Worker policy, or Workers.

Login and session-error pages have no semantic heading at all.

Add an explicit heading level or `asChild` contract to CardTitle and define page-specific heading
outlines.

WCAG: 1.3.1 Info and Relationships.

Suggested command: `/impeccable harden`

### P1 - Shared color roles fail WCAG AA

Measured contrast:

- Brand teal `#13989f` on white: **3.49:1**
- Brand teal on light background `#f7fafb`: **3.33:1**
- White text on dark-theme primary orange `#f33919`: **3.88:1**
- Orange text on dark card `#0d2435`: **4.10:1**

Locations:

- `styles/globals.css:20,66-67`
- `core/branding/PitCrewBrand.tsx:15,38`

These are normal-size text roles and require 4.5:1. Preserve the original colors for artwork and
large accents, but introduce accessible semantic text/action variants.

WCAG: 1.4.3 Contrast (Minimum).

Suggested command: `/impeccable colorize`

### P1 - Consequential action safety is inconsistent

Node revoke, profile pause, capacity, and recovery use confirmation/fences. Diagnostic credential
rotation and revocation execute directly:

- `features/settings/DiagnosticCredentials.tsx:231-255`

Rotation invalidates the old credential and exposes a new raw value once. Revocation is immediate.
Both need the same identity/consequence confirmation standard as other consequential operations.

Incident acknowledgement (`IncidentsPage.tsx:86-94`) is lower risk and should not automatically
inherit a blocking modal. Prefer immediate acknowledgement with a short undo/unacknowledge path;
use confirmation only if the domain intentionally makes acknowledgement irreversible.

Suggested command: `/impeccable harden`

### P1 - Runners prioritizes configuration over the operator result

Nine filter/sort controls are always visible before the table. At 390px, operators scroll through
the full control stack before seeing a runner. At desktop width, the table still widens the entire
document.

Keep search and the highest-frequency filters visible, disclose advanced filters, show active
filter chips and result/health counts, and constrain the table to its own region.

Suggested commands: `/impeccable distill`, `/impeccable adapt`

## Secondary findings

### P2 - Settings navigation has no visible selected state

All routes use the same outline-button appearance (`SettingsPages.tsx:37-50`). NavLink supplies
route semantics, but General, Access, Enrollment, and Diagnostics look equally active.

### P2 - Diagnostic credential inputs lack persistent visible labels

The form has accessible names through `aria-label`, but sighted users see values such as
`Performance diagnostics` and `24` without visible field meaning
(`DiagnosticCredentials.tsx:118-145`).

### P2 - Secondary route navigation exposes raw browser scrollbars

Profile and Node tabs show horizontal and vertical scrollbar controls, including arrow buttons,
even on desktop. `EntitySectionNavigation.tsx:23` needs `overflow-y-hidden` and a more intentional
overflow affordance.

### P2 - Programmatic route focus creates a page-sized focus outline

The shell focuses `main` after every route change and the global base style gives it a 3px outline:

- `AuthenticatedShell.tsx:165,258-260`
- `styles/globals.css:132-133`

Focus the page heading or suppress the non-interactive container outline while retaining skip-link
and interactive focus visibility.

### P2 - Loading and failure surfaces do not share the product finish

Most initial states are plain text (`Loading fleet status...`, `Loading runners...`), and the
session-error screen is an unbranded generic card without a semantic heading. Preserve the
excellent truthful copy, but use consistent branded state shells and structured loading placeholders.

### P2 - Contextual help is too weak for the terminology density

Terms such as GitHub eligible, connector replay, PSI, admission ceiling, current/stale rollout, and
diagnostic scope have no inline definition or task-oriented help.

### P2 - Nested bordered surfaces flatten hierarchy

Browser evidence identified the bordered retained-evidence note inside the incident table and the
inset worker-metrics panel inside a card. Both are legitimate information, but repeated border
treatments make supporting details compete with primary state.

### P3 - Reduced-motion handling is absent

Sheet and dialog animations run unconditionally. These are short and do not block work, so this is
not an AA blocker, but an intentional reduced-motion alternative is still required for a finished
system.

### P3 - Controls meet the 24px AA floor but not the 44px ergonomic target

Buttons are generally 32-36px tall and the mobile menu is 36px. This passes WCAG 2.2 target-size
minimum, but consequential touch actions should use larger targets.

## Performance findings

- Production build completed in 587ms.
- Main bundle: 206.84 kB / 64.46 kB gzip.
- Shared utils: 201.11 kB / 61.49 kB gzip.
- CSS: 39.68 kB / 8.40 kB gzip.
- Fleet, Incidents, Node, Profile, Runners, and Settings are split into lazy chunks.

This is a good baseline. Remaining risks:

- Runners and worker tables render all rows without pagination or virtualization.
- Active incidents are polled by the shell every 30s while fleet routes also receive incidents in
  the 5s fleet projection.
- Incident and pressure pages add their own 30s requests.

No runtime frame or memory profiling was performed.

## Positive findings

- Fleet is now exception-first: active incident severity appears above inventory and in navigation.
- Node/Profile routes are decomposed into focused sections rather than one evidence wall.
- The host-pressure command center ties pressure to actual workloads and keeps cancellation in GitHub.
- Missing, stale, last-known, and measured-zero evidence remain distinct throughout the UI.
- Destructive host operations use typed confirmation and operational fences.
- Tables use captions/scoped headers, charts have table equivalents, and many async states use live
  regions.
- Light/dark theming, lazy features, tenant switching, breadcrumbs, skip links, and mobile
  navigation provide strong architectural foundations.

## Persona findings

### Alex - experienced operator

Benefits from incident-first Fleet, hardware compare, density persistence, direct subroutes, and
workload links. Loses time to duplicate headings, slash-separated capacity values, the nine-control
Runners filter wall, and document-level table overflow.

### Sam - keyboard and screen-reader operator

Benefits from native controls, table semantics, skip link, route focus, and textual statuses.
Encounters AA contrast failures, sparse heading navigation inside dense pages, and horizontal page
scrolling outside the intended table regions.

### Casey - narrow-screen/on-call operator

Fleet and Incidents remain contained, but Node, Runners, and Workers widen the document. Secondary
tabs show raw scrollbar controls, and full desktop tables remain the primary mobile presentation.

## Questions for implementation planning

1. Should node routes use the display name as the H1 and expose the UUID only as copyable metadata?
2. On narrow screens, which three runner/worker fields are mandatory for first-pass triage?
3. Should incident acknowledgement remain one-click with undo, or is irreversible acknowledgement
   an intentional policy requiring confirmation?
4. Is the single-family restrained visual tone intentional, with differentiation coming from
   hierarchy and state rather than additional decorative styling?
