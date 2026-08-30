---
title: "ADR-0014: Frozen image rollout campaigns over typed profile operations"
status: "Accepted"
date: "2026-08-29"
authors: ["Nick Cosentino"]
tags: ["architecture", "campaigns", "images", "operations", "security"]
supersedes: ""
superseded_by: ""
---

# Context and scope

ADR-0009 gives Dashboard tenant-owned immutable image candidates. ADR-0013
gives it one locally constrained, at-most-once profile-image rollout command.
Operating a fleet still requires an administrator to discover compatible
profiles, repeat the same approval, monitor every command independently, and
remember which profiles were excluded or remain on a prior image.

A fleet campaign is not another host operation. It is a Dashboard-owned
business process that plans and sequences existing ADR-0013 commands. The
decision must preserve the outbound-only connector boundary, local recipe and
registry authority, exact profile fences, shared operation exclusion, and the
rule that a started command is never automatically repeated.

This decision governs tenant-scoped campaign planning, frozen target authority,
canary and wave approval, bounded dispatch, pause and cancellation boundaries,
per-target reconciliation, terminal campaign state, explicit rollback
campaigns, bounded persistence, and operator evidence. It does not authorize a
generic connector command, protocol-level campaign envelope, dynamic target
widening, label-based authorization, automatic wave approval, automatic retry
after an indeterminate command, automatic rollback, or job cancellation.

# Verified facts and assumptions

Verified facts:

- Protocol version 11 already carries one complete profile-image command,
  progress report, and terminal outcome without campaign metadata.
- The connector derives registry and filesystem authority locally and has no
  reason to know that a command belongs to a campaign.
- `IImageRolloutCommandStore` validates tenant ownership, capability freshness,
  recipe policy, architecture, exact fences, cooldown, and the shared
  `profile_active_operations` slot before it creates a command.
- Exact idempotency replay returns the same durable command even after candidate
  retention, which permits a restart-safe dispatcher to recover a queue/link
  interruption.
- `IFleetStore.GetFleetAsync` returns tenant-scoped node identity, online state,
  and current profile inventory, while the rollout command store returns the
  current capability and fences for profiles that advertise rollout.
- Successful rollout commands retain prior candidate, recipe, digest, and
  worker-revision authority only when Dashboard can prove the currently applied
  image from an earlier success.
- SQLite remains the single Dashboard persistence boundary and supports
  transactional leases, monotonic state triggers, and bounded retention.

Assumptions to confirm during implementation:

- One campaign target inventory remains within the configured hard target
  ceiling. Campaign creation fails explicitly rather than truncating authority
  when the ceiling is exceeded.
- A profile whose command succeeds with stale workers will eventually publish
  capability evidence that proves either full convergence or a continuing
  rolling/degraded state.
- An explicit rollback is possible only for targets whose prior candidate,
  recipe, digest, and worker revision were retained as authoritative evidence.

# Decision drivers

- Keep connector communication outbound-only and protocol version 11 unchanged.
- Freeze exactly what an administrator reviewed; never add newly discovered
  nodes or profiles after campaign creation.
- Keep excluded targets visible with bounded machine-readable reasons.
- Require a deliberate canary and a separate approval before every later wave.
- Reuse the existing single-profile queue, fences, at-most-once recovery, and
  shared profile-operation exclusion instead of duplicating host authority.
- Survive Dashboard restarts and crashes between target claim, command queue,
  and campaign linkage without creating duplicate commands.
- Bound campaign concurrency, per-node concurrency, target count, query size,
  retained history, and diagnostic text.
- Preserve honest rolling, failed, blocked, indeterminate, paused, cancelled,
  complete, and partial evidence.
- Make rollback another reviewed campaign rather than an automatic inverse.

# Decision

Dashboard will add a durable image-rollout campaign layer above the existing
protocol version 11 profile command. Connectors and the wire protocol remain
unchanged.

## Frozen draft plan

Creating a forward campaign requires one tenant-owned ready registry candidate.
Dashboard takes one tenant-scoped fleet snapshot and one rollout-capability
snapshot at a common Dashboard time. It records every current node/profile pair
within the configured hard ceiling as either:

- `eligible`, with the candidate authority and every current image, worker,
  static, preserved-configuration, routing, generation, and desired-state
  fence required by ADR-0013; or
- `excluded`, with one closed reason such as offline, capability unavailable,
  stale evidence, unsupported schema or topology, recipe or registry policy,
  architecture mismatch, already current, conflicting operation, or
  insufficient evidence.

Labels may filter presentation but never determine eligibility. The complete
eligible and excluded inventory, candidate identity, target authority, requester,
observation time, and a deterministic SHA-256 target-set hash are immutable
after creation. New or changed fleet members never join the campaign.

The campaign begins as `draft` when at least one target is eligible and as
`blocked` when none are eligible. A target ceiling overflow rejects creation
without persisting a partial campaign.

## Canary and waves

Configuring a draft chooses one exact eligible target as the canary and a
bounded wave size. A campaign with exactly one eligible target uses that target
as its implicit canary. Configuration assigns immutable deterministic wave
numbers: wave zero contains only the canary, and the remaining eligible targets
are sorted by node and profile identity before being partitioned.

Configuration transitions the campaign to `awaiting-approval`. Every wave has
one immutable approval identity, actor, time, idempotency key, and campaign
revision. Approval requires the current campaign revision and frozen target-set
hash. Wave zero must complete successfully before wave one can be approved.
Every later wave requires its own explicit approval; Dashboard never approves
or starts a later wave automatically.

An adverse canary result blocks the campaign. An adverse later wave terminalizes
the campaign as `partial` when an earlier target completed, otherwise `blocked`.
The operator creates another campaign to continue after reviewing the evidence.

## Restart-safe target dispatch

A bounded Fleet-owned background worker reconciles campaigns and leases due
targets in deterministic campaign, wave, node, and profile order. It enforces
configured campaign-wide concurrency and at most one active campaign dispatch
per node.

Each target has a stable idempotency key and signature derived from its campaign,
target authority, approving actor, candidate authority, and exact fences. The
worker leases a target before queueing the existing ADR-0013 command. After
queueing it links the returned command identifier to the target.

If Dashboard stops after command creation but before linkage, the expired target
lease is reclaimed and the same queue request resolves to the existing durable
command. A campaign retry therefore cannot create a second host operation. A
queue rejection becomes bounded target evidence and is not retried
automatically with weakened fences.

The dispatcher does not write a campaign identifier, wave number, label,
registry repository, path, executable, or command text to the connector
protocol. Host execution remains exactly the version 11 single-profile
operation.

## Reconciliation and terminal state

Campaign target state is projected from the linked rollout command and current
capability:

- an unlinked approved target is `queued`;
- command `queued`, `claimed`, and `started` become `queued`, `claimed`, and
  `applying`;
- a successful command with remaining stale workers is `rolling`;
- exact current digest and worker revision with zero stale workers is
  `complete`;
- rejected or expired commands are `blocked`;
- failed commands are `failed`;
- indeterminate commands remain `indeterminate`.

Missing or stale capability never becomes complete. Current and stale worker
counts remain nullable when unavailable.

The campaign is:

- `awaiting-approval` when the previous wave completed and another wave remains;
- `running` while an approved wave has dispatchable or active targets;
- `paused` when an administrator stops future dispatch;
- `complete` when every eligible target is complete;
- `partial` when at least one target completed and another terminal target did
  not;
- `blocked` when no target completed and the campaign cannot safely progress;
- `cancelled` when future dispatch was explicitly cancelled.

## Pause and cancellation

Pause and cancellation stop only targets that do not yet have a durable
profile-image command. Existing queued, claimed, started, succeeded, failed, or
indeterminate ADR-0013 commands are never withdrawn, reversed, or repeated.
Their evidence continues to reconcile after the campaign is paused or
cancelled.

Resume is explicit. It restores the campaign to `running` when an approved wave
still has undispatched targets or to `awaiting-approval` when the next wave
requires approval.

## Explicit rollback campaigns

Rollback creates a new `rollback` campaign from one prior campaign. It never
mutates or reverses the source campaign.

For each source target, Dashboard re-reads current capability and requires proof
that the source target digest and worker revision are still current. It uses the
linked command's retained prior candidate, recipe, digest, and worker revision
as the new per-target authority. Targets without complete prior authority are
persisted as excluded with `rollback-authority-unavailable`.

Because prior images can differ by target, rollback campaign targets carry
their own candidate, recipe, digest, and platform. The rollback campaign uses
the same frozen-plan, canary, wave, approval, dispatch, fencing, and terminal
rules as a forward campaign. It is never started automatically.

## Persistence, API, and retention

SQLite migration 30 adds campaign, target, wave, and idempotency records behind
domain-specific kernel interfaces. Campaign and target authority is immutable;
state transitions, leases, approvals, command linkage, and terminal evidence
are constrained transactionally.

Mutations are tenant-administrator-only, antiforgery-protected, rate-limited,
and idempotent. Viewer reads are tenant-scoped and bounded. The API exposes
campaign list and detail, create draft, configure, approve wave, pause, resume,
cancel, and create rollback operations with stable machine-readable outcomes.

Terminal campaigns are retained by age and per-tenant count. Active, paused,
draft, and awaiting-approval campaigns are never removed by retention. Lists
use a server-enforced maximum and explicit truncation.

# Alternatives considered

## Add a protocol version 12 campaign command

A campaign envelope could send multiple targets or wave metadata to one
connector. It would make the connector understand Dashboard business process,
expand the host trust surface, complicate partial delivery, and duplicate the
existing single-profile fences and at-most-once ledger. Campaign orchestration
remains entirely in Dashboard and reuses protocol version 11.

## Recompute eligible targets when each wave starts

Dynamic planning would automatically include new profiles and silently remove
changed ones. The operator would no longer approve an exact target set, and
labels or fleet churn could widen authority after approval. Every target is
frozen once and later drift becomes explicit blocked evidence.

## Automatically approve later waves after a healthy canary

Automatic progression reduces clicks but turns one approval into authority over
targets the operator has not re-reviewed after observing canary evidence.
Every wave remains separately approved.

## Queue every campaign target in one transaction

Bulk queueing would reserve many profile-operation slots immediately, exceed
bounded concurrency, reduce the opportunity to pause, and couple campaign
persistence tightly to the command table. Leased idempotent dispatch queues only
the approved bounded work that can proceed.

## Cancel or delete queued profile commands

Withdrawing an existing command would weaken immutable audit and race with an
outbound connector claiming the command. Pause and cancellation stop future
campaign dispatch only; existing commands complete under ADR-0013.

## Roll back automatically on failure

Automatic rollback may reverse a profile after new workers accepted work and
cannot be proven safe for indeterminate outcomes. Rollback remains a new
explicit campaign with current fences and separate approval.

# Consequences

Operators gain one restart-safe fleet workflow with exact target and exclusion
evidence, deliberate canary/wave gates, bounded parallelism, and explicit
rollback planning. Existing connectors remain compatible and campaign-unaware.

Dashboard persistence and orchestration become more complex. Campaign state
must reconcile two clocks and two durable lifecycles: Dashboard dispatch and
connector-owned profile execution. A temporarily conflicting or drifted target
can block a campaign and requires a new reviewed campaign rather than an
automatic retry.

Frozen target sets intentionally become stale as the fleet changes. That cost
is accepted because stale authority is visible and bounded, while dynamic
authority would be unsafe.

Rollback is unavailable for unmanaged prior images and any target whose prior
candidate authority cannot be proven. The UI must present that limitation
directly rather than implying universal reversibility.

The change adds tables and one bounded worker but no database, broker, cache,
connector port, credential boundary, protocol version, host executable, or
generic command surface.

# Confirmation

Architecture compliance is confirmed by tests that prove:

- protocols and connector code remain unchanged and receive only version 11
  single-profile commands;
- campaign creation freezes every eligible and excluded target deterministically
  and rejects target-count overflow without truncation;
- labels do not authorize or change the target set;
- candidate, target, fence, requester, canary, wave size, and target-set hash
  remain immutable after approval;
- one target is the explicit or implicit canary and every later wave requires a
  separate approval;
- lease expiry and queue replay recover a stop between command creation and
  campaign linkage without duplicate commands;
- campaign and per-node concurrency remain bounded;
- capacity, recovery, direct rollout, and campaign dispatch cannot overlap on
  one profile;
- pause, resume, and cancellation never withdraw an existing profile command;
- rolling targets require current digest, revision, and zero stale workers
  before becoming complete;
- failed, blocked, indeterminate, partial, cancelled, and unavailable evidence
  remain distinct;
- rollback campaigns include only targets with proven prior authority and never
  start automatically;
- tenant isolation, role authorization, antiforgery, rate limits, idempotency,
  retention, and bounded error text are enforced;
- Browser UX covers draft, awaiting approval, running canary, awaiting wave,
  paused, complete, partial, blocked, cancelled, mixed target outcomes,
  confirmation, long content, and responsive containment.

# References

- ADR-0009 demonstrates why one immutable ready candidate is trusted input for a
  forward campaign while GitHub workflow execution remains a separate boundary.
- ADR-0013 establishes the typed version 11 command, local host authority,
  exact fences, shared profile-operation exclusion, prior-image evidence, and
  at-most-once semantics reused by every campaign target.
- `docs/ui/runner-image-workspaces.md` defines the campaign planner, exclusion,
  approval, progress, rollback, accessibility, and Browser UX contract.
- `IFleetStore.GetFleetAsync` demonstrates the tenant-scoped node/profile and
  online-state inventory used for planning.
- `IImageRolloutCommandStore` demonstrates that campaign dispatch can reuse
  existing candidate, capability, fence, idempotency, cooldown, and operation
  exclusion enforcement without changing the connector protocol.
