# Frontend architecture

PitCrew Dashboard uses the Genesis React feature-plugin model. The ASP.NET Core
host serves one embedded Vite application and falls back to `index.html` for
non-file browser routes.

## Route model

Authenticated routes are tenant-scoped:

```text
/                                      authenticated landing redirect
/tenants/:tenantId/fleet               fleet overview
/tenants/:tenantId/nodes/:nodeId       node detail
/tenants/:tenantId/nodes/:nodeId/profiles/:profileId
/tenants/:tenantId/runners
/tenants/:tenantId/settings/general
/tenants/:tenantId/settings/access
/tenants/:tenantId/settings/enrollment
/admin/tenants                         system-administrator tenant creation
```

The application shell owns session loading, tenant switching, primary
navigation, breadcrumbs, theme, and account controls. Feature manifests
contribute their own routes, navigation entries, and breadcrumb presentation.

Shell navigation entries declare one operator-intent group (`monitor`, `operate`, or
`configure`), a deterministic order within that group, a concise description, and a
shell-owned icon key. The shell filters those entries by tenant and role, then renders
the same grouped taxonomy on desktop and mobile without importing feature internals.
The desktop rail remembers expanded or compact presentation locally. Compact mode
keeps full labels, accessible descriptions, active state, and incident attention; it
does not collapse into unexplained icons. Tenant switching always opens the selected
tenant's fleet overview.

## Feature ownership

Frontend features live below `ClientApp/src/features/`:

| Feature    | Responsibility                                                  |
| ---------- | --------------------------------------------------------------- |
| `admin`    | System-administrator tenant creation                            |
| `fleet`    | Fleet overview, node detail, profile detail, and pool mutations |
| `runners`  | Cross-fleet read-only runner and slot search                    |
| `settings` | Tenant settings, membership, and connector enrollment           |

Shared session, fleet reads, formatting, routing, and UI primitives live below
`src/core/` or `src/components/`. Features may import shared code but must never
import a sibling feature.

`src/features.registry.ts` is the only production file outside `src/features/`
that may import feature internals. The registry is the composition boundary
consumed by the core router and shell.

## Shared UI primitives

PitCrew-specific operational patterns live in `src/core/ui/` and compose the
generic shadcn/Radix primitives in `src/components/ui/`. Reach for these
instead of re-deriving the same markup in a feature:

| Primitive                               | Use it for                                                                                                                                                         | Prefer proximity/dividers instead when…                                                                                                                                |
| --------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `PageHeader`                            | The route's single H1, breadcrumbs, description, and page-level actions.                                                                                           | Never duplicate it; a route has exactly one (DESIGN.md "The One Page Title Rule").                                                                                     |
| `EntityHeader`                          | An entity's human-readable name, its secondary identifier, and entity-level actions (h2/h3).                                                                       | The name is only decorative metadata with no identifier or actions — a plain heading suffices.                                                                         |
| `SectionNavigation`                     | A horizontal, ARIA-labeled route-strip between an entity's own sub-views.                                                                                          | There is only one sub-view; a tab strip with a single destination adds noise, not orientation.                                                                         |
| `FormField`                             | Any labeled input/select in a filter, settings, or mutation form.                                                                                                  | —                                                                                                                                                                      |
| `FilterToolbar`                         | A responsive grid of `FormField`s that filter or sort a collection.                                                                                                | Only one filter control exists; inline it beside the collection instead of a bordered panel.                                                                           |
| `StateBanner`                           | A named positive/caution/critical condition the operator must notice (stale data, recovered fault).                                                                | The condition is evidence already legible on a value (a status badge, a "last known" caption) — an additional banner would restate it.                                 |
| `EmptyState`                            | A collection is legitimately empty and the absence is itself a state worth announcing.                                                                             | A single missing optional field; omit it or explain it inline instead of a dedicated card.                                                                             |
| `LoadingState`                          | An in-progress fetch with nothing to show yet.                                                                                                                     | Content is already rendered and refreshing; prefer a subtle inline indicator over replacing the view.                                                                  |
| `OperationalTable` + `ScrollableRegion` | Any dense tabular collection that can exceed the viewport width.                                                                                                   | A handful of rows fit comfortably; a plain list or `dl` avoids table semantics for non-tabular data.                                                                   |
| `ConfirmationSummary`                   | The identity, effects, prohibited effects, evidence fences, and acknowledgement of a consequential mutation, composed into `ConfirmActionDialog`'s `details` slot. | The action is reversible and low-consequence; a plain description is enough (DESIGN.md "The Confirm Consequence Rule" only requires this for consequential mutations). |

**Reach for another Card only when a new grouping is a materially distinct
task or evidence set.** DESIGN.md's "Cards group one coherent task or
evidence set. They are not the default page layout" and "The Not a Card Wall
Rule" (don't turn unrelated metrics or sections into an equal-weight card
field) both argue for a heading, spacing, or a one-pixel divider between
closely related content inside one card before reaching for a second card.

`CardTitle` accepts an `as` prop (`'h1'`–`'h6'` or `'p'`, default `'div'`) so a
card can declare the correct heading level for its position in the page
outline. It defaults to a non-heading `div` to preserve every pre-existing
call site's behavior: adding an implicit `h3` would have silently changed 28
existing cards' outline position, creating skipped or duplicate heading
levels. New call sites that need a real heading must pass `as` explicitly.
`EmptyState` is the one shared primitive that always renders a real heading —
its own `headingLevel` prop (`'h2' | 'h3'`, default `'h3'`) sets `CardTitle`'s
`as` for its title regardless of `CardTitle`'s own default, so an empty
collection's title still participates correctly in the page outline.

`FormField` associates its visible `<label>` with the control via `htmlFor`/
`id` (generating a stable ID when the control doesn't already have one)
rather than by wrapping the control, so hint and error text can sit beside
the control without being folded into its accessible _name_ — the HTML
label-name algorithm includes all of a wrapping label's text, which would
otherwise announce the hint/error as part of the field's name instead of its
description. Hint and error text get their own generated IDs, are wired to
the control through a merged `aria-describedby` (preserving any
`aria-describedby` the caller already set on the control), and an error
additionally forces `aria-invalid="true"` on the control.

`ScrollableRegion` requires a `label` naming what it contains (e.g. "Fleet
nodes for the active tenant") and renders `role="region"` plus
`tabIndex={0}` so the region is independently reachable and scrollable by
keyboard, not just a pointer. `min-w-0 max-w-full` keep the region itself
from stretching a flex/grid ancestor to the width of its wider content — the
failure mode that otherwise still lets an overflow wrapper widen the
document — while `overflow-x-auto`/`overscroll-x-contain` scope scrolling to
the region. `OperationalTable` reuses its own `caption` (a required `string`,
rendered as the table's visible `<caption>`) as the wrapping region's
`label`, so the table and its region are named consistently from one prop.

`SectionNavigation` hides its scrollbar (`scrollbar-width: none` /
`::-webkit-scrollbar { display: none }`) while native touch, wheel, and
keyboard scrolling (via the tabbable route links) keep working, and adds
`overflow-y-hidden`/`overscroll-x-contain` so the route strip never opens a
vertical scrollbar or bubbles an at-the-edge scroll to the page.

`FilterToolbar` renders a plain, unlabeled `div` by default — an unlabeled
`<section>` is an unannounced landmark. Pass its optional `label` prop only
when the toolbar's purpose isn't already conveyed by a preceding heading or
`PageHeader`/`EntityHeader`; supplying it renders a labeled `<section>`
landmark instead.

## Data flow

`FleetProvider` owns one five-second polling loop for the active tenant and is
mounted only around fleet-consuming routes. Fleet, node, profile, and runner
pages share its latest projection. Settings and administration routes do not
poll fleet state.

Feature-local mutations call the existing typed APIs and then request an
immediate shared refresh. Profile-detail capacity and manager-recovery controls
are mutually exclusive: an active command of either kind disables the other, and
recovery progress, evidence, and audit history are read from the same shared
projection rather than a second polling loop. The provider aborts obsolete requests on tenant or
route changes and rejects stale responses.

The current fleet endpoint still returns the complete nested tenant projection.
Route decomposition is an information-architecture boundary, not yet a
route-specific backend API boundary.

## Adding a feature

1. Create `src/features/<feature-id>/manifest.tsx`.
2. Define lazy route entrypoints, navigation, and breadcrumb presentation.
3. Assign every primary navigation entry a group, order, description, and icon key.
4. Add the manifest to `src/features.registry.ts`.
5. Keep all feature-local pages, services, and tests inside that feature.
6. Move genuinely shared contracts or data ownership into `src/core/`.
7. Add route, authorization, loading, error, empty, and accessibility tests.

Do not bypass the registry or import another feature directly. The
`check-feature-boundaries.mjs` fitness function parses static and dynamic
imports and fails CI on either violation.

## Error and accessibility contracts

- Session failures provide an in-place retry.
- Unknown routes render a distinct not-found page.
- Unexpected route errors are contained by the router error surface.
- Lazy feature render failures remain inside their feature boundary.
- Tenant and role guards explain denied access while server APIs remain the
  final authorization authority.
- Route navigation focuses the main content heading region.
- Desktop and mobile navigation expose the same authorized destinations.
- Destructive or consequential actions use accessible confirmation dialogs.
- Data tables provide captions, scoped column headers, and textual status.

## Validation

Run the frontend quality gate from `ClientApp`:

```powershell
npm ci
npm run build
npm test
```

`npm test` includes linting, formatting, Genesis boundary tests, the boundary
fitness check, and Vitest. Changes to SPA hosting or authentication return paths
also require the affected ASP.NET integration tests and a production publish.
