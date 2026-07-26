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

## Data flow

`FleetProvider` owns one five-second polling loop for the active tenant and is
mounted only around fleet-consuming routes. Fleet, node, profile, and runner
pages share its latest projection. Settings and administration routes do not
poll fleet state.

Feature-local mutations call the existing typed APIs and then request an
immediate shared refresh. The provider aborts obsolete requests on tenant or
route changes and rejects stale responses.

The current fleet endpoint still returns the complete nested tenant projection.
Route decomposition is an information-architecture boundary, not yet a
route-specific backend API boundary.

## Adding a feature

1. Create `src/features/<feature-id>/manifest.tsx`.
2. Define lazy route entrypoints, navigation, and breadcrumb presentation.
3. Add the manifest to `src/features.registry.ts`.
4. Keep all feature-local pages, services, and tests inside that feature.
5. Move genuinely shared contracts or data ownership into `src/core/`.
6. Add route, authorization, loading, error, empty, and accessibility tests.

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
