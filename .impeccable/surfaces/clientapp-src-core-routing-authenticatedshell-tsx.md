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
- Primary navigation exposes authorized destinations and active incident count without
  replacing severity or state text.
- Desktop uses the stable rail; narrow screens use the bounded navigation sheet with
  the same destinations and tenant/account controls.
- Route focus lands on the page heading or named content region without drawing a
  page-sized outline.

## States and constraints

Cover session loading, unauthenticated, session failure/retry, no tenant access,
insufficient role, not found, lazy-feature failure, and unexpected route failure.
Keep every state branded, keyboard operable, and truthful.

## Direction and anti-goals

The Pit Wall shell is stable and quiet so operational evidence leads. Avoid duplicate
page headings, raw UUID titles, decorative shell effects, ambiguous active navigation,
and uncontained sheet or breadcrumb overflow.
