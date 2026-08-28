---
version: 1
slug: "clientapp-src-features-fleet-fleetoverviewpage-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/FleetOverviewPage.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/IncidentsPage.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/IncidentDetail.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/IncidentRow.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ActiveIncidentSummary.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/manifest.tsx"]
---

## Scope and mode

Fleet readiness, active exception triage, incident lifecycle/history, fleet filters,
hardware comparison, and node inventory. Visitor mode: Operate.

## Audience and job

The operator first decides whether the fleet needs attention, then follows one
exception to its owning node, profile, or retained evidence. Inventory and hardware
comparison remain available after the operational signal is clear.

## Hierarchy and interaction

- Lead with one compact readiness band for observation freshness, online nodes,
  attention-bearing nodes, and active incidents.
- Rank the default node inventory by explicit incidents, profile lifecycle and
  rollout exceptions, degraded connector evidence, and offline state before ordinary
  online or revoked records; preserve deliberate name, status, and last-observed
  sorting.
- Present incidents as one attention-ordered queue and one selected case file.
  Selection is URL-stable through the `incident` query parameter; a missing retained
  record produces an explicit recovery state and never substitutes another incident.
- Keep severity, lifecycle, reason, evidence, node/profile identity, timeline,
  incident-specific connector recovery or independent connector context, and the
  owning-route link in the selected case file.
- Acknowledgement records reversible operator ownership. It never implies resolution
  or healthy state.
- Keep filter state and results visible, keep filter controls immediately reachable,
  and keep hardware comparison secondary to exception triage.

## Responsive behavior and states

At narrow widths, compact filter and queue disclosures precede the selected case file
in DOM and reading order. Selecting a row collapses the queue and focuses the case;
opening the queue reveals the same full-width operational rows used on desktop. Fleet
inventory retains compact mobile summaries and desktop comparison tables. Cover
healthy, warning, critical, acknowledged, resolved, truncated, missing deep link,
no-match, empty, stale, connector evidence present, enrichment pending, and
enrichment unavailable states. Missing or pending evidence remains explicit and never
becomes zero or healthy state.

## Direction and anti-goals

Direction: exception-led signal ledger, grounded surface candidate 6, seed
`d0d29f53`. The memorable moment is exception-to-evidence clarity: readiness,
attention order, and the selected trustworthy case file read as one continuous
operator decision. Avoid inventory-first composition, duplicate mobile/desktop case
content, row-level action clutter, clipped controls, color-only severity, and
acknowledgement language that resembles resolution.
