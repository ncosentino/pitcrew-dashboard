---
version: 1
status: "shipped"
slug: "features-fleet-components-profileimagerollout-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ProfileImageRollout.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/pages/ProfileImageRolloutPage.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ProfileImageRolloutAuthorization.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/ProfileImageRolloutEvidence.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/ProfileImageCandidateList.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ProfileWorkerUpdateSummary.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/imageRolloutApi.ts","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/profileRolloutView.ts","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ProfileImageRollout.test.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/e2e/images.spec.ts"]
---

## Scope and mode

One approved ready registry candidate applied to one existing profile through the
typed capability owned by issue #151. The profile route keeps its existing readiness,
identity, incidents, and task navigation. Fleet campaigns, automatic widening,
automatic rollback, job cancellation, arbitrary registries, and generic host commands
remain outside this surface. Visitor mode: Operate.

## Audience and job

An operator or administrator needs to prove that one exact candidate is compatible
with one exact profile, understand what will and will not change, authorize the
operation once, and follow applying, rolling, complete, failed, rejected, or
indeterminate evidence across reloads. Viewers need the same evidence without mutation
controls.

## Hierarchy and interaction

- Keep the route-level Profile readiness summary above this task; do not repeat the
  profile header or turn rollout into a detached image administration page.
- Lead with one compact rollout readiness strip naming connector support, observation
  freshness, local recipe policy, architecture compatibility, and active-operation
  exclusion.
- Make the primary composition a changeover lane: current image/revision on the left,
  selected ready candidate on the right, and the exact immutable transition between
  them. Selection is URL-stable and a missing candidate never substitutes another.
- Put the latest active or adverse rollout immediately below readiness. Persisted
  progress and terminal evidence outrank the candidate inventory.
- Keep preserved invariants in one scan-friendly ledger beside the changeover:
  routing/scope, labels and group/prefix, capacity and autoscaling, admission, network,
  volumes, resources/runtime, and protected credential reuse.
- Confirmation keeps profile and candidate identity, all static/routing/generation
  fences, effects, preserved invariants, prohibited effects, idempotency identity, and
  acknowledgement together.
- After acceptance, show command lifecycle and worker convergence separately. A
  successful apply may still have stale busy workers; indeterminate is never rendered
  as failed or safe to retry.
- Link candidate qualification to its Runner images owner and retained profile history
  to its Fleet owner instead of duplicating either full record.

## Responsive behavior and states

At wide widths, current and target evidence form two balanced columns with a narrow
transition spine and a bounded invariant ledger. At narrow widths and 200% zoom, the
lane becomes current, then target, then invariants, then action without horizontal
document overflow. Cover disabled/read-only connectors, offline or stale evidence,
unsupported profile state, unallowlisted recipe, incompatible architecture, missing
candidate, already-current target, shared-operation conflict, applying, rolling,
complete, failed, rejected, expired, and indeterminate states. Cover viewer and
administrator authority, retained refresh failure, long digests/profile names, both
themes, keyboard confirmation and Escape focus return, forced colors, reduced motion,
CJK, emoji, and RTL.

## Direction and anti-goals

Direction: controlled changeover lane. The memorable moment is seeing exactly one
immutable digest cross from qualified candidate evidence into one fenced profile while
the unchanged operating contract remains visible in the same frame. Worker convergence
reads like a handoff, not a progress fantasy: current and stale counts stay explicit
until the manager proves completion. Avoid a generic deployment wizard, equal-weight
metric cards, a desktop table squeezed onto mobile, hidden local-policy failures,
mutable image tags, command/path fields, automatic retry, automatic rollback, and any
suggestion that command success means every busy worker already changed image.
