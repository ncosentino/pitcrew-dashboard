---
version: 1
slug: "clientapp-src-features-fleet-fleetoverviewpage-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/FleetOverviewPage.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/IncidentsPage.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ActiveIncidentSummary.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/manifest.tsx"]
---

## Scope and mode

Fleet overview, active incident summary, incident lifecycle/history, fleet filters,
hardware comparison, and node inventory. Visitor mode: Operate.

## Audience and job

The operator first detects what needs attention, then opens the affected node,
profile, job, or retained evidence. Inventory and comparison support investigation
after material exceptions are clear.

## Hierarchy and interaction

- Lead with freshness and the highest-severity active exception.
- Show incident severity, lifecycle, evidence source, timeline, and direct evidence
  link before complete inventory.
- Capacity values use explicit labels rather than positional slash strings.
- Search and common filters remain visible; secondary comparison controls do not
  compete with active incidents.
- Incident acknowledgement is immediate and reversible through a short undo or
  unacknowledge path.

## Responsive behavior and states

At narrow widths, show severity/state, human identity, freshness, and the evidence
link before secondary columns. Full tables remain available inside labeled,
keyboard-operable scroll regions. Cover healthy, warning, critical, acknowledged,
resolved, truncated, no-match, empty, stale, connector evidence present, and reason
unavailable states.

## Direction and anti-goals

The memorable moment is exception-to-evidence clarity: the operator sees the material
problem and can open its trustworthy source immediately. Avoid inventory-first
composition, clipped actions, color-only severity, and dense rows with no task order.
