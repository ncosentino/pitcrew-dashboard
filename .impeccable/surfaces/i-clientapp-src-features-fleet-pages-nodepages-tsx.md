---
version: 1
slug: "i-clientapp-src-features-fleet-pages-nodepages-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/pages/NodePages.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/pages/ProfilePages.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/profileWorkspace.ts","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/ui/TaskWorkspace.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/ui/TaskNavigation.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/ui/ReadinessSummary.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/ui/OperationalList.tsx"]
---

## Scope and mode

Node and profile identity, readiness, host evidence, profile inventory, capacity,
workers, diagnostics, history, recovery, and administration. Visitor mode: Operate.

## Audience and job

An operator investigates one runner host or manager profile under routine or incident
conditions. They need persistent evidence freshness, the highest-priority exception,
an attention-ranked profile inventory, and one focused task without losing node or
profile context. Administrators may use only the existing typed, fenced operations.

## Direction

Use a dispatch-board workspace inside the established Pit Wall system. Node readiness
remains visible above Overview, Profiles, History, and Administration tasks. Profile
readiness remains visible above Overview, Capacity, Workers, Diagnostics, History, and
Recovery tasks. Profile inventory is a fixed-order field of full-width scan lines:
explicit incidents and reported degraded lifecycle state rank before ordinary
inventory, while a persisted table remains available for desktop comparison.
Constrained layouts keep each profile row compact before drill-in.

Concept provenance: surface seed `5de25d23`; the assigned third grounded structure was
the dispatch-board workspace implemented here. The existing Pit Wall visual world
remains authoritative.

## States and constraints

Cover initial loading, missing node/profile, online, offline, revoked, stale manager,
active incident, active job, degraded autoscaling, partial or unavailable telemetry,
rolling image, read-only authorization, mutation progress/failure, empty profiles,
unknown worker activity or job counts, and recovery lifecycle. Preserve exact fleet
projections, unavailable-versus-zero semantics, GitHub-owned job actions, tenant
authorization, antiforgery, and all typed operation confirmations.

## Memorable moment

One glance shows entity readiness and which profile row needs attention; one task
selection then replaces the evidence area without removing that context.
