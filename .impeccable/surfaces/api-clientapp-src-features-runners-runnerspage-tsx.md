---
version: 1
slug: "api-clientapp-src-features-runners-runnerspage-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/runners/RunnersPage.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/runners/RunnersRoute.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/runners/runnerRows.ts"]
---

## Scope and mode

Cross-fleet runner and slot search, filters, sorting, status, resource evidence, image
identity, exit evidence, and links into focused profile investigation. Visitor mode:
Operate.

## Audience and job

The operator locates an affected runner, slot, repository, profile, lifecycle state,
or registration problem and determines where to investigate next.

## Hierarchy and interaction

- Keep repository search and the two highest-frequency filters visible.
- Put secondary filters and sort detail behind progressive disclosure.
- Show active filter chips, result count, health summary, and clear-all.
- Preserve URL-backed filters for bookmarking and expert workflows.
- A row leads with human host/profile/slot identity, current GitHub job or repository,
  registration/lifecycle state, and freshness before secondary resource columns.

## Responsive behavior and states

On narrow screens, use a compact runner summary with state, identity, job/repository,
freshness, primary resource or failure evidence, and the profile link. Keep the full
table available inside its own region. Cover loading, stale, unavailable, offline,
empty tenant, no match, long repository, unknown registration, exit evidence, and
large result sets.

## Direction and anti-goals

The memorable moment is fast recognition: the operator sees the affected workload and
next investigation path without traversing a filter wall. Avoid document overflow,
all filters permanently expanded, hidden result counts, and desktop-table scaling as
the mobile strategy.
