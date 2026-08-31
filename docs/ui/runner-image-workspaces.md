# Runner image workspace contract

This document owns the cross-cutting UX contract for runner-image candidate,
single-profile rollout, and fleet campaign surfaces. It does not authorize a
capability that its owning implementation issue has not delivered.

## Capability ownership

| Workflow                                                                     | Capability owner                                                   | UX responsibility                                                                                               |
| ---------------------------------------------------------------------------- | ------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------- |
| Trusted workflow registration, build requests, candidates, and qualification | [#150](https://github.com/ncosentino/pitcrew-dashboard/issues/150) | Candidate readiness, request queue, focused evidence, and lower-frequency recipe administration                 |
| One approved candidate applied to one existing profile                       | [#151](https://github.com/ncosentino/pitcrew-dashboard/issues/151) | Exact profile/candidate confirmation, fences, progress, convergence, and terminal outcome                       |
| Frozen multi-profile rollout campaign                                        | [#152](https://github.com/ncosentino/pitcrew-dashboard/issues/152) | Eligibility planning, exclusions, canary and wave approval, per-target progress, and explicit rollback planning |

The implementation issues own APIs, protocol, persistence, authorization, local host
policy, and operation semantics. UI work consumes only shipped typed contracts. It
must not invent placeholder mutations, infer capability, or widen an operation to
make a planned composition work.

## Shared workspace grammar

Runner images belong in the shell's **Operate** group. The feature contributes one
stable parent destination; task navigation owns candidate, campaign, and recipe
subroutes.

Every image workflow follows the same order:

1. **Readiness** states whether the task can proceed and names unavailable or stale
   evidence.
2. **Task navigation** preserves stable deep links without changing order by role or
   state.
3. **Attention-ordered records** put blocked, failed, indeterminate, or active work
   before ordinary completed history.
4. **One focused detail** owns the selected request, candidate, profile rollout, or
   campaign.
5. **Consequential confirmation** keeps identity, fences, effects, prohibited
   effects, and acknowledgement together.
6. **Related evidence links** hand off to GitHub, profile, runner, or history owners
   instead of duplicating their complete records.

Use `ReadinessSummary`, `TaskWorkspace`, `OperationalList`, `OperationalRow`,
`DetailPanel`, `ConfirmationSummary`, and `CopyableId` when their existing contracts
fit. A comparison table may supplement the scan-first list when operators genuinely
compare many targets; it never replaces the narrow drill-in.

## Candidate and build workspace

### Readiness

Lead with:

- trusted GitHub image integration availability;
- enabled recipe registrations;
- active build or qualification requests;
- ready candidates;
- requests requiring operator attention.

Temporary GitHub unavailability remains unavailable or retrying. It never becomes a
workflow failure. A missing candidate report, identity mismatch, or policy rejection
remains blocked or failed according to the authoritative server contract.

### Records and detail

Default request order:

1. blocked or failed;
2. qualifying, building, dispatching, or requested;
3. ready;
4. older terminal history.

One request row leads with recipe, source identity, lifecycle state, request time,
requester, and exact GitHub run link when available. The focused detail contains:

- immutable request, registration version, repository, workflow, ref, source commit,
  and run identity;
- current lifecycle and bounded retry or failure evidence;
- candidate digest, platform, output mode, and report/artifact identity when present;
- qualification rows with explicit passed, failed, blocked, or unavailable state;
- observation and terminal timestamps;
- links to the exact GitHub run and owning recipe registration.

Raw workflow logs, artifact archives, tokens, registry credentials, and unbounded
error text never enter the workspace.

### Build request

Requesting a build is a consequential external dispatch. Confirmation names:

- **identity:** exact recipe registration version, repository, workflow, source ref,
  and source commit;
- **effects:** one durable request is created and the frozen workflow is dispatched
  with only declared inputs;
- **prohibited effects:** no host rollout, Docker access, arbitrary workflow/ref/input,
  or automatic retry after an indeterminate dispatch;
- **acknowledgement:** the operator verified the immutable source and reviewed recipe.

Recipe registration and disablement remain administrator tasks behind secondary
disclosure. They do not compete visually with active candidate work.

## Single-profile rollout

The profile route keeps profile readiness above the rollout task. The rollout
composer consumes one ready candidate and one authoritative capability observation.

Before confirmation, show:

- node and profile display identity;
- current immutable image digest and revision;
- target candidate digest, recipe, architecture, and qualification status;
- connector freshness, host-operation availability, local allowlist state, and
  active-operation exclusion;
- exact desired generation/hash and topology fingerprints used as fences;
- busy/current workers that may remain on the prior image while draining.

Confirmation states:

- **effects:** apply only the approved immutable image to this profile and report
  applying, rolling, current-worker, stale-worker, and terminal evidence;
- **preserved invariants:** scope, labels, runner group/prefix, capacity, admission,
  network, volumes, resource policy, and protected credential remain unchanged;
- **prohibited effects:** no job cancellation, capacity/routing change, arbitrary
  registry/path/command, overlapping operation, automatic retry after started, or
  automatic rollback.

Progress and terminal outcome survive reload. Lost started outcomes remain
**indeterminate** and require a new explicit request; the UI never reshapes them into
failed or safe-to-retry.

## Fleet rollout campaigns

The campaign planner consumes one ready candidate and computes one frozen target set.
It separates:

- **eligible** targets with complete current capability and matching fences;
- **excluded** targets with bounded reasons, including offline, stale, incompatible,
  unallowlisted, topology mismatch, conflicting operation, or insufficient evidence.

Excluded targets remain visible. Labels may filter or explain inventory; they never
authorize rollout.

The focused campaign detail preserves:

- candidate identity and qualification link;
- frozen eligible and excluded target sets;
- canary, wave size, concurrency, requester, approval, and timestamps;
- per-target state: queued, claimed, applying, rolling, complete, failed, blocked, or
  indeterminate;
- current/stale worker convergence and bounded failure evidence;
- pause/cancel availability only before affected commands are claimed.

One target is the explicit canary unless the campaign has exactly one eligible target.
Later waves require explicit approval and remain deep-linkable. Newly discovered
targets never join automatically.

Rollback is a new campaign built from each target's recorded prior digest. It requires
the same eligibility calculation and approval. The product never promises or performs
automatic rollback.

## Browser evidence matrix

Implementation pull requests extend the repository Browser UX harness with sanitized,
schema-validated fixtures.

| Dimension           | Required evidence                                                                                                                                                                      |
| ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Authorization       | viewer read surfaces; administrator build/recipe operations; unauthorized routes and controls absent while APIs remain authoritative                                                   |
| Candidate lifecycle | requested, dispatching, building, qualifying, ready, blocked, failed, retryable GitHub unavailable, missing deep link, and no recipes/candidates                                       |
| Candidate scale     | one item, realistic mixed queue, more than 100 records with pagination or virtualization, and selected records beyond the first narrow window                                          |
| Profile rollout     | unsupported/read-only connector, offline/stale evidence, incompatible architecture, allowlist rejection, stale fence, conflict, applying, rolling, complete, failed, and indeterminate |
| Campaign lifecycle  | draft, awaiting approval, running canary, awaiting wave, complete, partial, blocked, and cancelled                                                                                     |
| Target lifecycle    | eligible, excluded, queued, claimed, applying, rolling, complete, failed, blocked, and indeterminate in one mixed campaign                                                             |
| Confirmation        | keyboard-opened dialog, exact identity/fences, effects, prohibited effects, acknowledgement, cancel/Escape focus return, and failed mutation recovery                                  |
| Persistence         | selected IDs, filters, approvals, and progress survive navigation/reload without substituting another record                                                                           |
| Resilience          | 320, 390, 768, 1280, and 1440 CSS pixels; 200% reflow; light/dark; forced colors; reduced motion; long digests/refs/names; CJK, emoji, and RTL                                         |
| Deterministic gates | no document overflow, sequential headings, serious/critical axe findings, broken focus handoff, or schema-invalid fixtures                                                             |

## Review criteria

Review blocks delivery when the implementation:

- presents stale, missing, retrying, excluded, or indeterminate evidence as current,
  zero, failed, eligible, or safe to retry;
- hides excluded targets or substitutes a different selected record;
- relies on labels as authorization or widens a frozen target set;
- omits exact identity, fences, effects, prohibited effects, or acknowledgement from
  consequential confirmation;
- duplicates raw GitHub/profile/history evidence instead of linking to its owner;
- introduces arbitrary workflows, inputs, registries, commands, paths, automatic
  rollback, or job cancellation;
- loses request, candidate, target, or approval attribution across reload;
- ships an unbounded list, desktop table scaled onto mobile, inaccessible state, or
  document-level overflow.

The finish review judges the complete workflow against this contract, its focused
surface brief, PRODUCT.md, DESIGN.md, and the rendered Browser UX evidence.
