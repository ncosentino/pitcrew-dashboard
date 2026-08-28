---
version: 1
slug: "clientapp-src-core-routing-authenticatedshell-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/core/routing/AuthenticatedShell.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/core/routing/ShellNavigation.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/routing/Breadcrumbs.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/routing/SessionBoundary.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/routing/pages.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/routing/guards.tsx"]
---

## Scope and mode

Authenticated application shell, route presentation, primary navigation, breadcrumbs,
tenant context, account controls, and global loading/error/permission surfaces.
Visitor mode: Operate.

## Audience and job

An operator must keep tenant and route context while moving quickly from a fleet
signal to evidence and a safe action. Administrators additionally need predictable
access to enrollment, access, diagnostics, and tenant administration.

## Hierarchy and interaction

- The route owns one human-readable H1; breadcrumbs support orientation without
  repeating the title.
- Display names lead. Stable identifiers appear as selectable, copyable metadata.
- Primary navigation groups authorized destinations by operator intent in fixed
  Monitor, Operate, and Configure order. Feature manifests own each destination's
  group, order, description, and shell icon key.
- The expanded desktop rail teaches with concise descriptions. Its remembered compact
  mode keeps icons, full labels, accessible descriptions, active state, and incident
  attention visible rather than becoming an icon cryptogram.
- Tenant context stays above task navigation and always names the current role.
  Switching tenants lands predictably on the selected fleet overview.
- Narrow screens use the bounded navigation sheet with the same groups, destinations,
  tenant context, theme control, and sign-out. Desktop rail preference never changes
  mobile composition.
- Active incident count retains severity when available and becomes explicitly
  unavailable when the badge request fails rather than disappearing as measured zero.
- Route focus lands on the page heading or named content region without drawing a
  page-sized outline.

## States and constraints

Cover session loading, unauthenticated, session failure/retry, no tenant access,
insufficient role, incident-count unavailable, long tenant and destination labels,
compact rail persistence, not found, lazy-feature failure, and unexpected route
failure. Keep every state branded, keyboard operable, and truthful.

## Direction and anti-goals

Direction: station index. The Pit Wall shell is stable and quiet, but its rail reads
as an intentional sequence from monitoring through operation to configuration rather
than a flat feature dump. The memorable moment is compact mode retaining readable
task names and incident state. Avoid icon-only mystery navigation, duplicate page
headings, raw UUID titles, decorative shell effects, ambiguous active navigation, and
uncontained rail, sheet, or breadcrumb overflow.
