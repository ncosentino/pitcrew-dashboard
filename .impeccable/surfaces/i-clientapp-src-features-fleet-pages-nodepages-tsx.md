---
version: 1
slug: "i-clientapp-src-features-fleet-pages-nodepages-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/pages/NodePages.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/pages/ProfilePages.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/NodePressureCommandCenter.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/EntitySectionNavigation.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ProfileCapacitySummary.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ProfileManagerRecovery.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ProfileSlotsTable.tsx"]
---

## Scope and mode

Node and profile identity, overview, host pressure, active workloads, capacity,
workers, diagnostics, history, recovery, and administration. Visitor mode: Operate.

## Audience and job

The operator investigates why one host or profile is unhealthy, identifies affected
work, and chooses the narrow safe action. Administrators may pause admission, change
capacity, recover a manager, rotate credentials, or revoke enrollment within existing
authorization and fencing.

## Hierarchy and interaction

- Use the human display name or profile name as H1; expose stable IDs as copyable
  monospaced metadata.
- Overview answers: what is wrong, what is affected, what is running, and what can be
  done safely.
- Focused subroutes keep Capacity, Workers, Diagnostics, History, Recovery, and
  Administration from becoming one evidence wall.
- Material incidents and unavailable/stale evidence precede ordinary inventory.
- Current GitHub jobs link to GitHub for inspection or cancellation.
- Consequential operations state identity, fences, expected and prohibited effects,
  and require explicit confirmation.

## Responsive behavior and states

At narrow widths, prioritize status/severity, entity identity, current job or
repository, freshness, primary capacity/resource evidence, and the next safe action.
Full evidence remains available in contained detail regions. Cover online, offline,
revoked, stale manager, pressure, active job, rolling image, read-only, unavailable,
mutation busy/error, and recovery lifecycle states.

## Direction and anti-goals

The memorable moment is evidence-to-safe-action continuity. Avoid UUID-led hierarchy,
equal-weight card walls, raw scrollbars, document overflow, inferred workload truth,
and actions detached from the evidence that justifies them.
