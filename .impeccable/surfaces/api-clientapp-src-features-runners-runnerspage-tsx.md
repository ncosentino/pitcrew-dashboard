---
version: 1
slug: "api-clientapp-src-features-runners-runnerspage-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/runners/RunnersPage.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/runners/RunnerDetail.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/runners/RunnersRoute.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/runners/runnerRows.ts","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/runners/manifest.tsx"]
---

## Scope and mode

Cross-fleet runner inventory, URL-backed filters, current GitHub job correlation,
runner lifecycle and resource evidence, and links into profile-owned history. Visitor
mode: Operate.

## Audience and job

The operator locates current work or a material runner exception, selects one
node/profile/slot tuple, determines whether GitHub job identity is known, and follows
the owning job or profile evidence without inferring workload from resource activity.

## Hierarchy and interaction

- Lead with runner readiness: latest observation, explicit current jobs, slots needing
  review, and reported inventory.
- Order the default inventory by current job, busy-but-unattributed activity, offline
  state, lifecycle or registration exceptions, transitional state, then ordinary idle
  slots.
- Keep filters and result counts URL-backed. Preserve the selected runner while
  clearing filters.
- Open one selected runner as a URL-stable dispatch sheet with current job,
  assignment timing, runner lifecycle, copyable identifiers, resource evidence, and
  direct GitHub/profile/history links.
- A missing selected tuple produces an explicit recovery state and never substitutes
  another runner.
- Keep the wide comparison table and narrow summaries as equivalent views of the
  same attention-ordered inventory.

## Responsive behavior and states

At narrow widths, keep primary filters usable, place the selected dispatch sheet
before the card inventory, focus its compact title after selection, and pin an
in-scope selection when it falls beyond the first 50 cards. Contain long job,
repository, node, profile, slot, hash, and image identities. Cover loading, empty fleet,
filtered-empty, current job, explicit no-job, job unavailable on older contracts,
busy, draining, idle, offline, stale, registration missing, exit evidence, missing
resources, missing deep link, and large-result states in both themes.

## Direction and anti-goals

Direction: dispatch manifest, grounded surface candidate 4, seed `f1e6836c`. The
memorable moment is correlation without guessing: a current job becomes the manifest
headline, while an unattributed busy runner is equally explicit about what is not
known. Avoid inventory-first equal weighting, resource-based workload inference,
duplicated profile/history evidence, hidden selection, mobile table scaling, and
filter state that destroys investigation context.
