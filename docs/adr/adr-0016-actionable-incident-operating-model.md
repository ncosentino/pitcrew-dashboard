---
title: "ADR-0016: Actionable incident operating model"
status: "Accepted"
date: "2026-09-13"
authors: ["Nick Cosentino"]
tags: ["architecture", "evidence", "fleet", "incidents", "operations", "storage"]
supersedes: ""
superseded_by: ""
---

# Context and scope

ADR-0015 assigns authority, provenance, clocks, coverage, freshness, and
retention to individual evidence claims. It deliberately does not decide how
rule evaluations become persistent conditions or how related conditions become
human-owned work.

The current alert projection combines those concepts. An `AlertCandidate` is a
currently proven diagnosis keyed by rule, node, profile, and subject. SQLite
uses that key to maintain a pending, triggered, acknowledged, or resolved
`AlertIncident`. Repeated evaluations of the same key are restart-safe, but
every independent condition is also an independent operator record. Missing
evidence is represented as a suppression that keeps a triggered record open,
while the public lifecycle still combines condition truth with
acknowledgement. Severity has one mutable value, so the current impact and the
episode's worst confirmed impact are not independently available.

The version 1 fleet trust scenario corpus characterizes the boundary before
grouping changes. It proves that repeated signals for one stable key are one
persistent condition, independent keys remain separate, reporting loss can
coexist with retained manager and workload evidence, and only fresh
authoritative clearing evidence proves resolution. It also preserves 0, 1, 62,
200, and 201-record cases so a lower headline count cannot substitute for exact
retrieval, authoritative totals, or retained source evidence.

This decision defines durable semantics for events, signals, conditions,
condition episodes, and actionable incident episodes. It governs truth,
severity, the ADR-0015 operator state, suppression, escalation, hysteresis,
grouping, resolution, recurrence, versioned interpretation, migration, and
bounded historical links.

It does not select page layout, notification transport, retention durations, a
root-cause engine, automatic remediation, or fleet/support identity
association. It adds no event-stream platform, broker, cache, or datastore.
Bounded projections remain in the existing single-replica SQLite database
behind the incident storage boundary.

# Verified facts and assumptions

## Verified facts

- `AlertRuleEvaluator` produces stable candidate keys from kind, credential-
  derived node identity, profile identity, and a bounded subject hash. Repeated
  evaluations of one key therefore already have a durable condition-like
  identity.
- `SqliteAlertIncidentStore` reconciles candidates atomically. It updates an
  existing open row for the same key, deletes pending rows that disappear,
  resolves triggered or acknowledged rows that disappear without suppression,
  and preserves open rows matched by unavailable-evidence suppression.
- The current public `AlertIncident` has one `Severity` and one `Status`.
  Acknowledgement changes that status even though it does not change evidence.
- The current evaluator stops profile diagnosis after connector reporting loss
  and suppresses dependent profile conditions. Host-pressure and other
  independently observed impact can have different evidence dependencies and
  must not be hidden by that visibility failure.
- Journal discontinuity, dropped events, resets, and deliberate history expiry
  are durable history-quality facts. They may explain confidence limits without
  proving a current operational impact.
- ADR-0005 already uses bounded idempotent event identifiers and receipt-time
  retention for connector health replay. It does not require a general event
  stream.
- ADR-0015 requires missing, partial, stale, retained, malformed, or unavailable
  evidence to remain unable to prove healthy, zero, resolved, remediated, or
  safe to mutate. It also makes incident acknowledgement independent from
  condition truth.
- The current SQLite incident page is newest-first and bounded. It exposes
  truncation but no authoritative total or exact selected-record read.
- The fleet trust corpus preserves source events and independent condition
  decomposition so later grouping can be compared without deleting evidence.

## Assumptions to confirm during implementation

- Existing incident storage can add bounded condition, series, episode,
  membership, operator-state, link, and interpretation metadata through the
  current SQLite connection and repository boundaries.
- Current alert kinds can be assigned to closed rule families, evidence-
  dependency families, investigation classes, and severity policies without
  accepting arbitrary server- or operator-supplied grouping expressions.
- A qualification period can run the new projection beside the legacy
  projection before headline counts and default queues switch.

# Decision drivers

- Preserve the distinction between observed occurrence, evaluated truth,
  persistent machine state, and human work.
- Make missing evidence explicit without letting it resolve or conceal confirmed
  impact.
- Produce a stable number of operator tasks under repeated evaluation,
  retries, restarts, and replay.
- Group only when the grouping is deterministic, tenant-safe, explainable, and
  useful for one bounded investigation.
- Never describe grouping as proof of common cause.
- Preserve current urgency separately from the worst confirmed severity in an
  episode.
- Keep ADR-0015 operator state and suppression independent from machine truth.
- Require fresh authoritative clearing evidence and hysteresis before
  resolution.
- Preserve recurrence and historical traceability instead of reopening or
  rewriting resolved records.
- Permit rule and grouping evolution without silently reinterpreting history.
- Preserve exact source evidence, counts, pagination, and legacy history during
  migration.
- Remain within the existing bounded evidence and single-replica SQLite
  architecture.

# Decision

Dashboard separates six durable concepts.

| Concept | Durable meaning | Identity and retention |
| --- | --- | --- |
| Event | A bounded occurrence reported or accepted by an authoritative source, with source identity, source time, Dashboard receipt time, type, and sanitized payload or reference. An event states that something occurred; it does not by itself state current condition truth. | Uses the source's bounded idempotency identity when available, scoped by tenant and authoritative source. Existing bounded ledgers and retention remain authoritative; this decision does not require copying every event into a new incident event log. |
| Signal | One versioned rule evaluation for one logical condition identity. Its truth is `true`, `false`, or `unknown` and it references the claim provenance, clocks, coverage, freshness, and rule interpretation that produced it. | Idempotent for the same condition identity, rule interpretation version, evaluation input version, and evaluation boundary. Storage may retain the latest signal and bounded truth transitions rather than every repeated evaluation. |
| Condition | The stable rule/target subject being evaluated, independent of whether it is currently true. It names tenant, rule family, authoritative target identity, bounded subject discriminator, and evidence dependency family. | Stable across repeated evaluation. Rule interpretation changes are recorded separately and continue the identity only under an explicit compatibility policy. |
| Condition episode | One continuous evaluation period that begins when a condition enters activation evaluation. A pending attempt may end before activation; after activation, the episode ends only in proven resolution. Monitoring-ended records that observation ceased without ending or resolving the episode. It carries truth history, current and peak severity, debounce and recovery hysteresis, and a link to a prior episode on recurrence. | Repeated `true` or `unknown` signals update the unresolved episode. A new `true` after proven resolution creates a new episode linked to the resolved predecessor; a resolved episode is never reopened. |
| Actionable incident series | The arrival-order-independent identity for one versioned investigation boundary. It is derived only from tenant, grouping policy version, closed incident family, canonical authoritative target scope, investigation class, and evidence-dependency scope. | Stable across retries, input order, and recurrence. It contains no first-member identity, display text, severity, operator state, or timestamps. |
| Actionable incident episode | One operator investigation interval within a stable incident series, containing one or more compatible condition episodes. It has membership reasons, rule interpretation versions, current and peak severity, ADR-0015 operator state, and suppression. It is an operator task, not a claim of shared root cause. | Identified by the stable series identity plus a transactionally allocated monotonic episode ordinal. Repeated or replayed input updates the one unresolved episode, including a monitoring-ended episode that later resumes. A new ordinal is allocated only after the prior episode has proven resolution. |

An event can contribute to several claims. A signal can reference several
claims. A condition episode can contribute to one actionable incident at a
time. The underlying events, signals, claims, and condition episodes remain
independently retrievable even when several conditions are grouped.

## Signal truth and provenance

Signal truth follows ADR-0015 claim authority. A rule declares the exact claim
families and coverage required to prove active and cleared states.

| Available rule evidence | Signal truth | Allowed effect |
| --- | --- | --- |
| Fresh, authoritative, sufficiently complete evidence satisfies the versioned active predicate | `true` | Start or update activation debounce; create or update the open condition episode after debounce; derive current severity from the same evidence. |
| Fresh, authoritative, sufficiently complete evidence satisfies the versioned clearing predicate | `false` | Start or continue recovery hysteresis. Resolution is permitted only after the rule's clearing duration or sample requirement completes without `true` or `unknown`. |
| Required evidence is missing, stale, partial beyond the rule's allowed coverage, unavailable, retained-only, malformed, unsupported, or contradictory | `unknown` | Preserve an open episode as waiting for evidence; pause or reset clearing hysteresis; never resolve, reduce a confirmed fact to zero, or authorize mutation. |

Every signal records or can resolve:

- condition identity and condition episode identity;
- rule family and rule interpretation version;
- `true`, `false`, or `unknown`;
- evaluated time;
- authoritative claim references and their source-observed, Dashboard-received,
  freshness, coverage, retention, and unavailable-reason dimensions;
- active or clearing predicate outcome;
- derived current severity when truth is `true`; and
- a bounded explanation code suitable for API and UI rendering.

Response generation time is never a signal input. A diagnostic result is an
additional claim and does not become a clearing signal unless the versioned rule
explicitly names that diagnostic claim as authoritative for the condition.

## Condition identity and lifecycle

A condition identity is the canonical tuple:

`tenant + rule family + authoritative target identity + bounded subject`.

Display names, route state, response ordering, severity, evidence availability,
and grouping version are excluded. Credential-derived node identity remains
authoritative under ADR-0015. Profile identity is included when the rule is
profile-scoped. Event-specific conditions include a bounded source event
identity in the subject; sustained conditions use the stable measured subject
such as capacity target, subsystem, worker slot, or pressure dimension.

Activation debounce and recovery hysteresis are separate versioned rule
parameters. Activation requires continuous or sample-complete `true` evidence.
Resolution requires continuous or sample-complete `false` evidence. An
`unknown` gap does not count toward either proof and resets any in-progress
clearing proof. Rule-specific policies may restart activation after an unknown
gap when continuity cannot be established.

| Current episode state | Next signal or control fact | Next state | Required behavior |
| --- | --- | --- | --- |
| None | `true` | `pending` or `active` | Create one episode. Apply activation debounce; zero-debounce rules enter active immediately. |
| None | `false` | None | Retain no actionable episode; a bounded evaluation transition may be retained for diagnostics. |
| None | `unknown` | None | Expose unavailable rule evidence where relevant, but do not create an impact condition or operator task solely from silence unless reporting loss is itself a separately proven condition. |
| `pending` | `true` | `pending` or `active` | Continue the same episode and debounce; repeated evaluation is idempotent. |
| `pending` | `unknown` | `pending` | Preserve identity but restart or pause activation according to the rule's declared continuity policy; do not create an incident. |
| `pending` | `false` | None | End the untriggered attempt without a resolved incident episode. |
| `active` | `true` | `active` | Update last-confirmed facts and current severity; preserve episode and incident identities. |
| `active` | `unknown` | `waiting-for-evidence` | Preserve the episode, operator state, acknowledgement metadata, last-confirmed facts, and peak severity. Current truth and current severity become unknown. |
| `waiting-for-evidence` | `true` | `active` | Resume the same episode. Re-evaluate current severity and escalation. |
| `active` or `waiting-for-evidence` | `false` | `recovering` | Begin recovery hysteresis; retain the episode and incident as open until proof completes. |
| `recovering` | `false` | `resolved` | Resolve only after the rule-specific clearing duration or sample count completes with fresh authoritative evidence. |
| `recovering` | `true` | `active` | Cancel clearing without creating another episode or task. |
| `recovering` | `unknown` | `waiting-for-evidence` | Cancel clearing because missing evidence cannot complete proof. |
| Any open state | Authoritative observation contract end | `monitoring-ended` | End observation without claiming recovery. Examples include credential revocation or deliberate target removal when no rule-authoritative clearing observation is possible. |
| `resolved` | Later `true` | New linked episode | Create a new condition episode with `previousEpisodeId`; never reopen the resolved episode. |
| `monitoring-ended` | Monitoring resumes and yields `true` | `active` in the same episode | Resume the unresolved episode because monitoring-ended did not prove clearing. A new episode is allowed only after proven resolution. |

`monitoring-ended` requires an authoritative lifecycle fact that the target or
observation contract ended. A reporting gap, timeout, stale sample, process
restart, or ordinary absence is only `unknown`. If the same authoritative
target identity is later reactivated, its unresolved episode resumes; a newly
assigned authoritative target identity creates a different condition identity.

## Severity and escalation

Severity is a rule-derived impact and urgency claim, not a color, ownership
state, evidence-quality label, or count of historical failures.

- `currentSeverity` is derived only from the newest fresh authoritative `true`
  signal. It is unknown while the condition is waiting for evidence or
  recovering.
- `lastConfirmedSeverity` preserves the most recent confirmed active severity
  for context while current severity is unknown.
- `peakSeverity` is the greatest confirmed severity reached in the episode and
  never decreases.
- An actionable incident's current severity is the greatest known current
  severity among all active members. Suppression may change queue presentation
  but never the severity fact. If no member has current confirmed impact,
  incident current severity is unknown rather than copied from peak or
  last-confirmed severity.
- An actionable incident's peak severity is the greatest member peak reached
  while attached to that incident episode.
- `critical` means a rule has fresh evidence of current severe impact or urgent
  intervention. Missing history, reporting loss, stale evidence, retained
  values, acknowledgement, and generic unavailable state do not become
  critical unless a separate rule proves current critical impact.

Escalation occurs when current severity increases, confirmed affected scope
materially expands, a new independently actionable condition joins, or a
versioned rule emits another declared material escalation. Escalation records
the prior and new facts and transitions the current operator state to
`unowned`, while preserving prior acknowledgement metadata in audit history.
Acknowledgement therefore never suppresses material escalation. De-escalation
requires the rule's severity hysteresis; unknown evidence never proves
de-escalation.

## Operator state

ADR-0015's incident ownership taxonomy remains authoritative. This record does
not add a separate assignee, owner, or review-state dimension.

| Operator state | Metadata and semantics |
| --- | --- |
| `unowned` | No acknowledgement applies to the current incident revision. New incidents, unacknowledged incidents, and materially escalated incidents are unowned. Prior acknowledgement history may remain for audit. |
| `acknowledged` | Stores the acknowledging actor, acknowledgement time, and acknowledged incident revision. It means an operator accepted awareness of that revision. It does not prove assignment, suppression, remediation, current truth, or resolution. |

Condition truth remains independently derived from member condition episodes:
active, waiting for evidence, recovering, monitoring ended, or resolved.
Acknowledgement changes only operator state. Unacknowledgement returns the same
revision to `unowned`. A material escalation creates a new incident revision
and returns that revision to `unowned`; a later acknowledgement explicitly
accepts the escalated revision.

## Suppression

Suppression is a typed human or policy decision about presentation and
notification. It never changes signal truth, condition lifecycle, severity,
membership, peak severity, evidence retention, or resolution.

A suppression records tenant, scope, closed reason, actor or policy identity,
policy version, creation time, optional expiry, and the incident revision it
covered. Default actionable views may exclude a currently suppressed incident,
but exact retrieval, totals by lifecycle, audit history, and underlying
conditions remain available.

Suppression is bypassed and the incident returns to `unowned` when:

- current severity increases to a level not covered by the suppression;
- confirmed affected scope expands beyond the suppressed scope;
- an independently actionable condition joins; or
- the suppression expires or its versioned policy no longer applies.

Waiting for evidence is not suppression. Monitoring ended is not suppression.
Acknowledgement is not suppression.

## Deterministic explainable grouping

Grouping operates only on condition episodes that have completed activation
debounce. It uses a closed, versioned grouping policy and stable authoritative
identities. It never consumes display-name similarity, free-form evidence text,
route context, mutable list position, or a model-generated root-cause guess.

Every membership stores:

- grouping policy version;
- incident episode and condition episode identities;
- one or more closed membership reason codes;
- canonical scope and investigation class used by the decision;
- evidence-dependency family;
- membership start and end times.

Allowed reason codes describe operational correlation rather than causality,
such as `same-reporting-boundary`, `same-authoritative-target`,
`same-investigation-class`, `shared-unavailable-evidence`, and
`overlapping-episode-window`. UI and API language uses "grouped because" or
"investigate together", never "caused by" or "root cause".

### Grouping matrix

| Relationship between open condition episodes | Grouping result | Explanation |
| --- | --- | --- |
| Different tenant | Never group | Tenant isolation is absolute. |
| Same stable condition identity and open episode | Same condition and existing incident membership | Repeated evaluation updates one episode and cannot create another operator task. |
| Same authoritative target or bounded parent scope, same investigation class, overlapping episode window, and compatible evidence dependencies | May group under the declared policy | One safe investigation can evaluate the conditions together. The stored reasons state exactly which predicates matched. |
| Several dependent conditions become `unknown` because one reporting or observation boundary is unavailable | May group into one visibility investigation | The group represents shared missing evidence, not shared failure cause. Last-confirmed facts remain attached to each condition. |
| Reporting loss plus a condition with fresh authoritative impact evidence independent of that reporting path | Never hide the impact condition in the reporting-loss group | The independent impact remains a separate incident, or remains separately prominent in a compatible non-visibility incident, because its current truth does not depend on the missing reporting evidence. |
| Same node but different investigation class or incompatible safe next action | Keep separate | Shared location alone is insufficient. |
| Similar title, reason text, severity, or observation time without an allowlisted relationship | Keep separate | Coincidence and presentation similarity do not prove one investigation boundary. |
| Same canonical grouping dimensions arriving in different batches, orders, or replays | Same series and open episode | Arrival order and first-member identity are excluded from series and episode identity. |
| Resolved incident and later recurrence | Never reopen the resolved incident | Allocate the next episode ordinal in the same stable series and retain explicit predecessor links. |

The grouping algorithm is deterministic:

1. derive a canonical series tuple solely from tenant, grouping policy version,
   closed incident family, canonical authoritative target scope, investigation
   class, and evidence-dependency scope;
2. encode and hash that tuple into a stable series identity using one versioned
   canonicalization contract;
3. apply the grouping policy's compatibility predicates and the independent-
   impact exclusion before membership;
4. in one SQLite write transaction, insert-or-select the series by its unique
   canonical identity and select its single unresolved episode, including
   monitoring-ended state;
5. if an unresolved episode exists, idempotently attach compatible condition
   episodes to it and resume it when monitoring restarts; if none exists,
   allocate `max(episodeOrdinal) + 1` under the locked series and create the
   episode only when the previous episode has proven resolution or no previous
   episode exists; normal episode ordinals begin at 1 and ordinal 0 is reserved
   exclusively for the unverified legacy-resolution mapping; and
6. persist conditions, the series, episode, memberships, severity, operator
   state, suppression effects, and links atomically.

Unique constraints enforce one series for each canonical tuple, one episode
ordinal per series, one unresolved episode per series, and one active incident
membership per condition episode. If two transactions race to open the same
series or episode, the uniqueness conflict causes the loser to re-read and join
the committed canonical episode rather than allocate another identity.

If legacy data or an interrupted earlier migration exposes two separately
unresolved episodes for the same canonical series, reconciliation atomically selects the
lowest episode ordinal and then canonical episode identity as survivor, moves
the union of compatible memberships, recomputes current and peak severity,
returns operator state to `unowned` when the merge adds unacknowledged material
facts, re-evaluates suppression, and replaces the losing episode with a bounded
`merged-into` link projection. No source condition or acknowledgement audit
record is deleted by the merge.

New evaluations, retries, connector replay, process restart, and identical
inputs therefore converge on the same series, open episode, memberships, and
history. Batched, staggered, and replayed A-then-B and B-then-A inputs produce
the same result. Adding a compatible condition updates membership and incident
revision; it does not create a parallel task.

Grouping is not allowed to discard source records. Counts distinguish events,
signals, open condition episodes, actionable incident episodes, suppressed
incidents, resolved history, and monitoring-ended history.

## Actionable incident lifecycle

An actionable incident stays actively open while any member condition is
active, waiting for evidence, or recovering. Monitoring-ended is an unresolved,
resumable projection rather than proof that a new episode may begin. Member
resolution removes current contribution but preserves historical membership.

| Member summary | Incident truth summary | Queue behavior |
| --- | --- | --- |
| At least one member has fresh confirmed `true` impact | `active` | Actionable using current severity and current safe next action; suppression affects presentation, not truth. |
| No member has current confirmed impact and at least one is `waiting-for-evidence` | `waiting-for-evidence` | Remains open with operator state and last-confirmed context; next action is evidence restoration or an explicit external prerequisite. |
| No member is active or unknown and at least one is completing clearing hysteresis | `recovering` | Remains open; do not announce resolution until every required clearing proof completes. |
| All members are proven resolved | `resolved` | Close the incident episode with fresh clearing provenance for every resolved member. |
| No member remains observable and unresolved members ended monitoring | `monitoring-ended` | End active monitoring without claiming recovery; retain operator state, acknowledgement metadata, last-confirmed and peak severity, and reason. The series episode remains unresolved and resumes if monitoring resumes. |
| Mix of resolved and monitoring-ended members, with no open member | `monitoring-ended` | The incident cannot claim complete resolution because at least one member lacks clearing proof. |

An incident resolution time is the evaluation that confirms all members'
recovery hysteresis completed. A monitoring-ended time is the evaluation that
confirms no member remains observable and at least one member lacks resolution
proof. Neither is inferred from absence in a query result.

Recurrence after proven resolution creates a new condition episode linked by
`previousEpisodeId`. If the stable incident series still has an unresolved
episode because another member remains active, waiting, recovering, or
monitoring-ended, the recurrent condition episode joins that existing incident
episode. If the prior incident episode itself has proven resolution, the series
allocates its next monotonic episode ordinal and links it by
`previousIncidentEpisodeId`; the resolved task is never reopened.
Monitoring-ended state does not allocate a new incident episode: later evidence
resumes the same unresolved episode. A new incident episode exists only after
the prior incident episode has proven resolution.

The migration exception for an unverified legacy `resolved` row does not weaken
that rule. Its reserved ordinal-0 predecessor is not a normal incident episode
and is not proven resolved. It permits creation of the first normal episode only
when fresh authoritative evidence produces a `true` signal under the current
rule. It does not permit an `unknown`, waiting-for-evidence, monitoring-ended,
or otherwise unresolved normal episode to open a successor.

## Rule and grouping interpretation versions

Rule interpretation and grouping policy are independently versioned.

- `ruleFamily` names the stable condition intent.
- `ruleInterpretationVersion` owns active and clearing predicates, required
  claim families, coverage, debounce, hysteresis, severity, escalation, and
  monitoring-end policy.
- `groupingPolicyVersion` owns investigation classes, canonical scopes,
  compatibility predicates, exclusion rules, membership reasons, and
  deterministic tie-breaks.
- Every signal, episode transition, incident membership, and migration records
  the applicable versions.

A new version never rewrites historical signals or memberships in place.
Deployment activates an explicit interpretation policy:

| Version change policy | Effect |
| --- | --- |
| `compatible-continue` | Continue the open condition episode only when the new rule declares the same logical truth boundary and maps old state without inventing evidence. Record the version transition. |
| `re-evaluate-open` | Evaluate current claims under the new version. `true` continues or creates an episode, `unknown` waits for evidence, and `false` starts new-version recovery hysteresis; the version change itself cannot resolve. |
| `end-and-fork` | End old monitoring with a version-change reason and create a distinct condition identity or episode under the new interpretation. Use when the truth boundary or target meaning materially changes. |
| `preserve-old-grouping` | Existing open incident memberships remain under their recorded grouping policy until closure; new incidents use the new policy. |
| `explicit-regroup` | A migration may move open condition episodes only with a versioned deterministic mapping, successor/predecessor links, preserved audit history, and before/after counts. It never mutates resolved history. |

Unknown or unsupported versions fail closed. They preserve readable last-known
history but cannot prove truth, clearing, grouping compatibility, or operation
authorization.

## Compatibility and migration

Storage changes are additive and remain in the existing Dashboard SQLite
database. Domain-specific repositories own condition, incident, membership,
and migration access. No feature writes another feature's tables directly.

Migration is explicit, versioned, restart-safe, and auditable:

1. Preserve the legacy row and identifier as source history.
2. Record one migration run identity, source schema/projection version,
   destination rule and grouping versions, policy, and outcome.
3. Map each legacy alert key to a stable condition identity. An unresolved
   legacy row becomes one condition episode and one canonical singleton series
   under a `legacy-v1-singleton` grouping policy unless an explicit regroup
   policy is selected.
4. Map legacy `triggered` to active with operator state `unowned`. Map legacy
   `acknowledged` to active with operator state `acknowledged` and preserve its
   actor and time metadata. No separate assignee or owner is inferred.
5. Preserve legacy severity as last-confirmed and peak severity. It becomes
   current severity only when current rule evidence independently confirms it.
6. A legacy `resolved` row with fresh authoritative clearing provenance follows
   the normal proven-resolution mapping. A legacy `resolved` row without that
   provenance uses the reserved unverified-resolution mapping below and never
   becomes a proven resolved episode.
7. Preserve exact series, episode ordinal, predecessor, successor, legacy-ID,
   condition-membership, and count reconciliation links so deep links and
   audits remain traceable.

### Unverified legacy resolution mapping

For every legacy `resolved` row that lacks clearing provenance, migration:

1. derives the same canonical incident series identity that current grouping
   would derive from tenant, grouping policy version, closed incident family,
   canonical authoritative target scope, investigation class, and evidence-
   dependency scope;
2. upserts a reserved ordinal-0 predecessor in that series with status and
   provenance `legacy-resolution-unverified`, the legacy identifier, available
   historical times, legacy severity as historical-only context, and no
   clearing claim;
3. records the mapping under unique keys for migration policy plus legacy
   identifier and for series plus reserved ordinal 0; compatible legacy rows
   mapping to the same series add idempotent source mappings to that one
   predecessor rather than creating more ordinal-0 records; and
4. leaves the series without a normal open episode until current evidence is
   evaluated.

The reserved predecessor is a migration/tombstone record, not an actionable,
resolved, waiting, or monitoring-ended episode. Re-running migration selects
the existing series, ordinal-0 predecessor, and legacy mapping and produces no
new episode, link, or count.

If later evidence is `unknown` or absent, migration creates no normal episode.
Fresh `false` evidence may describe current cleared truth but does not
retroactively verify the legacy resolution and creates no operator task. When
fresh authoritative evidence first produces `true`, one SQLite write
transaction creates the first normal episode at ordinal 1, links it to ordinal
0 with transition `reopened-after-unverified-legacy-resolution`, sets current
condition truth to true and operator state to `unowned`, and derives current and
peak severity solely from the fresh signal. It is not labeled recurrence,
relapse, or reopening after recovery because recovery was never proven.

This is the only non-proven predecessor allowed before a new normal episode.
Ordinary unknown, waiting-for-evidence, recovering, monitoring-ended, and
unverified normal episodes retain or resume their existing episode and cannot
allocate a successor.

Historical quality facts migrate to evidence notices only through a closed
migration policy. The policy identifies eligible legacy kinds, destination
notice kind, wording, severity treatment, retention, and count reporting.
Journal discontinuity, dropped-event, reset, or history-expiry facts remain
immutable evidence. Reclassification may remove them from the actionable task
queue only by creating a traceable evidence notice and preserving the original
record and count reconciliation. They are never deleted, marked resolved, or
rewritten merely to lower incident counts.

If no explicit policy covers a legacy record, it remains readable as legacy
history with unknown interpretation. Migration does not infer root cause,
source-observed time, clearing evidence, acknowledgement, grouping reason, or
current severity.

## Bounded history links and retention

Detailed evidence, summaries, event payloads, signal inputs, and condition
membership history remain subject to existing age and count retention. Pruning
those details must not create dangling recurrence, merge, or migration links.

The existing SQLite database adds a bounded incident-history link projection;
it is not a new datastore. When detailed episode history is pruned while a link
or supported deep link still requires identity, a minimal tombstone retains:

- tenant-scoped episode identity and stable series identity;
- episode ordinal, grouping policy version, closed incident family, and
  canonical target-scope digest;
- terminal or merged status and start, resolved, monitoring-ended, merged, and
  expiry times when applicable;
- predecessor, successor, and `merged-into` episode identities;
- legacy incident identifier mappings;
- rule/group interpretation summary; and
- a bounded provenance summary: `resolution-proven`,
  `legacy-resolution-unverified`,
  `reopened-after-unverified-legacy-resolution`,
  `resolution-provenance-unavailable`, `monitoring-ended`, `merged`, or
  `history-expired`, plus the clearing source family and verified/evaluated time
  when retained.

Tombstones contain no event payload, diagnostic content, free-form evidence,
credentials, private host details, or invented source times. A referenced
tombstone remains while any retained successor, merge target, legacy mapping,
or open episode depends on its concrete identity.

Age and per-tenant count pruning operate transactionally, oldest terminal
history first. They never prune open episodes. A concrete tombstone may be
removed only when no retained row references it. If a count ceiling requires
compacting an older linked prefix, the transaction first replaces the oldest
retained successor's concrete predecessor with a typed `history-expired`
boundary containing only the pruned count and oldest/newest known terminal
times, and rewrites concrete legacy mappings for the pruned prefix to bounded
expiry locators. It then removes the now-unreferenced prefix and retains those
episode and legacy-identifier locators under a separate age/count ceiling.

An ordinal-0 `legacy-resolution-unverified` tombstone remains while its retained
ordinal-1 successor has a concrete predecessor dependency. If age or count
compaction removes ordinal 0, the same transaction replaces ordinal 1's
concrete predecessor with a `legacy-resolution-history-expired` boundary while
preserving the transition
`reopened-after-unverified-legacy-resolution`. The legacy deep link returns
`history-expired` while its locator remains and `history-unavailable` after the
locator expires. The retained ordinal-1 episode never invents the legacy
severity, clearing evidence, time, or identifier after those details expire.

If fresh true evidence arrives after ordinal 0 was compacted but while its
expiry locator still retains the tenant, canonical series identity, and
`legacy-resolution-unverified` category, ordinal 1 uses the expired-history
boundary and the same `reopened-after-unverified-legacy-resolution` transition
without reconstructing legacy detail. If both the tombstone and locator expired
before any successor existed, no retained fact can establish the relationship:
fresh true evidence opens the ordinary first normal ordinal-1 episode with no
legacy predecessor or recurrence label. The migration relationship is never
reconstructed from timestamps, titles, severity, or display identity.

An exact tenant-authorized deep link returns:

- the retained detailed episode when available;
- the minimal tombstone with `history-pruned` when details expired;
- `history-expired` while an expiry locator remains; or
- `history-unavailable` after the locator itself expires or when no authorized
  retained identity can be established.

| Retained representation | Minimum available history | Link behavior | Permitted pruning transition |
| --- | --- | --- | --- |
| Detailed episode | Full retained incident, condition membership, bounded evidence, and links | Exact detail response | May become a tombstone after detail age/count expiry. |
| Link tombstone | Minimal identity, series/group/version, status/times, links, legacy mapping, and provenance summary | `history-pruned` response with no fabricated detail | Remains while concretely referenced; may be compacted only after references are replaced by an expiry boundary or locator. |
| Reserved legacy predecessor | Series ordinal 0, legacy mapping, `legacy-resolution-unverified`, available historical times, and no clearing claim | `history-pruned` or retained legacy detail response; never a proven-resolution response | Remains while ordinal 1 concretely depends on it; compaction replaces that link with `legacy-resolution-history-expired`. |
| Expiry locator | Tenant-scoped episode or legacy identifier, canonical series identity when previously known, expiry time, and a closed expiry category, with no severity, payload, or clearing facts | `history-expired` response | May be removed after its independent age/count limit. |
| No retained identity | No authorized fact remains | `history-unavailable` response | No fields, links, or prior state are inferred. |

These responses never fabricate episode fields or expose whether an identifier
belongs to another tenant. UI links render the explicit unavailable/expired
state rather than a dangling detail page. Retention settings must bound detail
rows, link tombstones, and expiry locators independently, and migration records
must state the effective policy versions.

During qualification, legacy and new projections run from the same bounded
evidence. Comparison reports source conditions, actionable incidents,
membership, lifecycle, current/peak severity, unknown states, suppression, and
counts. Lower incident count alone is not success evidence. The default queue
switches only after deterministic parity and intended differences are
explained.

# Invariants

1. Missing, stale, partial, retained-only, malformed, unsupported, or
   unavailable evidence never produces a `false` signal and never completes
   resolution hysteresis.
2. A fresh response, connector receipt, acknowledgement, diagnostic result, or
   grouping update never refreshes another claim's source evidence.
3. Repeated evaluation of the same logical condition and episode cannot create
   another condition episode or operator task.
4. Event, signal, condition, and incident identities are tenant-scoped and use
   authoritative target identity rather than display names or route state.
   Incident series identity depends only on versioned canonical grouping
   dimensions and never on arrival order or first-member identity.
5. Independent fresh confirmed impact cannot be hidden inside a reporting-loss
   or shared-unavailable-evidence group.
6. Grouping records explainable allowlisted relationships and never asserts
   shared root cause.
7. Current severity is derived from current confirmed impact; peak severity is
   historical and monotonic within an episode. Unknown evidence does not become
   critical and does not prove de-escalation.
8. ADR-0015 operator state and suppression never change condition truth,
   severity evidence, or resolution. Operator state is exactly `unowned` or
   `acknowledged` with actor/time/revision metadata; there is no inferred
   assignee state.
9. Material escalation returns the current incident revision to `unowned` even
   when a prior revision was acknowledged or suppressed.
10. Proven resolution requires rule-specific fresh authoritative clearing
    evidence and completed recovery hysteresis for every resolved member.
11. Monitoring ended is explicit non-resolution and requires authoritative
    evidence that observation permanently ended.
12. Recurrence never reopens a resolved condition or incident episode; it
    creates linked history under the stable series with a transactionally
    monotonic episode ordinal. Monitoring-ended evidence does not create a new
    episode because it did not prove resolution.
13. Reserved ordinal 0 is used only for migrated legacy resolved history whose
    clearing provenance is unavailable. Fresh true evidence may open ordinal 1
    as `reopened-after-unverified-legacy-resolution`; it is not recurrence after
    recovery. Unknown, waiting, monitoring-ended, or other unproven normal
    episodes cannot use this exception to create successors. After both the
    ordinal-0 tombstone and locator expire, no legacy relationship is inferred.
14. Rule and grouping version changes never reinterpret historical facts in
    place.
15. Historical quality facts change queue classification only through an
    explicit versioned migration policy with preserved source history and count
    reconciliation.
16. Batched, staggered, replayed, and raced permutations of compatible
    conditions converge to the same series, open episode, memberships, and
    history. Atomic uniqueness conflict handling merges rather than duplicates.
17. Grouping, migration, acknowledgement, suppression, retention compaction,
    and reconciliation are idempotent and atomic through existing SQLite
    storage boundaries.
18. Referenced episode tombstones remain while retained successors depend on
    them. Pruning replaces concrete links with explicit history-expired
    boundaries before deletion; deep links return retained, pruned, expired, or
    unavailable state and never dangle or fabricate history.
19. Exact tenant-scoped retrieval, authoritative totals, pagination, and source
    evidence remain available independently from the grouped default queue.
20. No implementation introduces an event-stream platform, second datastore,
    inbound connector path, generic remote command, inferred workload truth, or
    automatic remediation.

# Alternatives considered

| Option | Advantages | Drawbacks | Decision |
| --- | --- | --- | --- |
| Separate events, tri-state signals, stable conditions, linked episodes, and versioned actionable incidents | Preserves evidence authority; makes uncertainty and recurrence explicit; supports deterministic grouping, idempotency, and human workflow without claiming cause | Adds domain types, SQLite migrations, reconciliation logic, version metadata, API fields, and broader lifecycle tests | Selected |
| Keep one alert row as both condition and operator task | Smallest schema and closest to current behavior; stable candidate keys already prevent some duplicates | Cannot group related work, combines acknowledgement with truth, loses current-versus-peak severity, and makes recurrence and migration ambiguous | Rejected |
| Group every condition by node and overlapping time | Easy to explain and sharply reduces counts | Shared location and time do not imply one investigation; confirmed pressure or worker impact can be hidden by reporting loss; next actions may conflict | Rejected |
| Group all dependent diagnoses under reporting loss | Produces one visibility task and avoids a flood when profile evidence disappears | Conceals independently confirmed impact and can mislead operators into treating reporting restoration as remediation | Rejected; only conditions whose truth actually depends on the unavailable boundary may join the visibility investigation |
| Derive incident identity from the first arriving condition | Simple creation path and naturally unique first task | A/B arrival order, batch boundaries, replay, and races produce different identities and histories for the same grouping facts | Rejected |
| Derive a stable series from canonical grouping dimensions and allocate monotonic episode ordinals | Arrival-order independent; recurrence and retries converge; exact grouping version remains visible | Requires canonical encoding, unique constraints, write-transaction ordinal allocation, and merge handling for legacy duplicates | Selected |
| Use probabilistic or model-generated root-cause grouping | Could discover relationships beyond static policy and reduce manual rule maintenance | Non-deterministic, difficult to migrate or audit, can expose free-form evidence, and would present correlation as cause without authoritative proof | Rejected |
| Treat unknown as false after a timeout | Automatically closes stale work and keeps queues small | Violates ADR-0015, manufactures recovery from silence, loses acknowledgement context, and hides reporting failures | Rejected |
| Reopen the same incident after recurrence | Keeps one long-lived identifier and a compact history | Blurs distinct recovery intervals, operator-state revisions, duration, and release-quality measurements | Rejected |
| Preserve resolved incidents but start linked episodes on recurrence | Keeps immutable history and makes relapse rate, prior recovery, and renewed acknowledgement explicit | Creates more episode records and requires predecessor navigation | Selected |
| Treat every legacy resolved row as proven recovery | Simplifies migration and lets later true evidence use ordinary recurrence | Fabricates clearing provenance, overstates historical recovery, and violates ADR-0015 | Rejected |
| Ignore an unverified legacy resolved row when later true evidence arrives | Avoids claiming recovery and minimizes migration state | Breaks legacy traceability and makes the new task appear unrelated to its historical record | Rejected |
| Reserve ordinal 0 as an unverified legacy predecessor and open ordinal 1 only from fresh true evidence | Preserves deterministic series history without claiming recovery; migration and replay are idempotent | Adds one explicit exceptional transition and retention treatment | Selected |
| Store every event and evaluation in a new event-stream platform | Enables arbitrary replay and temporal queries | Adds a datastore, ordering, replay, partitioning, retention, and operating model without measured need; conflicts with the single-replica SQLite constraint | Rejected |
| Rewrite legacy incidents directly into the new grouped shape | Fast migration and immediately lower task counts | Destroys historical interpretation, invents grouping and clearing facts, and prevents count reconciliation | Rejected |
| Delete episode rows and links when detailed evidence expires | Simplest retention implementation and smallest database | Leaves recurrence, migration, merge, and deep links dangling or encourages fabricated predecessor data | Rejected |
| Retain all episode history forever | Preserves every link and deep link without compaction | Violates bounded storage and retention requirements and grows without an operational ceiling | Rejected |
| Retain bounded link tombstones and explicit expiry boundaries in SQLite | Preserves coherent recurrence and migration links while allowing payload and old-chain pruning; deep links degrade explicitly | Adds tombstone, locator, compaction, dependency, and count-policy logic | Selected |
| Keep existing SQLite and add bounded versioned projections and migration records | Reuses established transactions and operations; enough for current fleet scale and deterministic replay from bounded evidence | SQLite schema and repository logic become richer; single-replica limits remain | Selected |

# Consequences

## Positive

- Operators receive one stable task for one investigation while retaining every
  underlying condition and evidence boundary.
- Reporting loss can consolidate dependent uncertainty without hiding fresh
  independently confirmed impact.
- Missing evidence truthfully moves work to waiting for evidence instead of
  resolving it.
- Current urgency can fall or become unknown without erasing peak impact.
- Acknowledgement survives evidence gaps, while material escalation returns the
  current revision to unowned without erasing acknowledgement audit history.
- Activation debounce, recovery hysteresis, monitoring ended, suppression,
  resolution, and recurrence become independently testable.
- Rule and grouping evolution is auditable and does not rewrite history.
- Historical quality notices can leave the primary action queue only through an
  explicit traceable policy rather than deletion.
- Retry, replay, restart, and repeated evaluation converge without duplicate
  tasks.
- Stable series identity makes batch order and race timing irrelevant.
- Bounded tombstones preserve recurrence, migration, and deep-link semantics
  after detailed evidence is pruned.
- Fresh impact after an unverified legacy resolution remains linked and
  actionable without being mislabeled as recurrence after recovery.

## Negative

- Incident storage gains condition, series, episode, membership, operator-state,
  suppression, version, migration, tombstone, expiry-locator, total, pagination,
  and exact-read concerns.
- APIs and browser fixtures must render several orthogonal state dimensions
  instead of one status and one severity.
- Rule authors must define active and clearing predicates, evidence
  dependencies, continuity, hysteresis, severity, escalation, monitoring-end,
  and version compatibility.
- Grouping policies require maintained closed vocabularies and deterministic
  canonicalization, race, and migration tests.
- Parallel qualification temporarily operates two projections and requires
  count reconciliation rather than a simple before/after total.
- Some incidents remain open as waiting for evidence or monitoring ended,
  increasing historical complexity compared with timeout-based closure.
- Linked recurrence chains can retain small tombstones after detailed evidence
  expires, and count compaction must explicitly replace removed links with an
  expiry boundary.
- Migration and UI contracts must distinguish proven recurrence from the
  exceptional `reopened-after-unverified-legacy-resolution` transition.

## Neutral constraints

- Grouping reduces human tasks, not source evidence or condition count.
- An incident can be acknowledged, suppressed, and waiting for evidence at the
  same time because operator state, presentation policy, and truth answer
  different questions.
- Diagnostic success can add evidence and suggest a next action but does not
  itself prove remediation.
- The default queue may omit suppressed or non-actionable history, but exact
  retrieval and authoritative categorized totals remain required.
- Existing bounded retention still applies. This decision does not select
  durations, but it requires independent bounds for detail rows, link
  tombstones, and expiry locators.

# Executable downstream acceptance cases

This documentation-only decision defines the stable cases below. Implementing
issues must capture their own pre-change failure and post-change pass; this ADR
does not fabricate red evidence.

| Owner | Executable acceptance cases |
| --- | --- |
| #278 | Storage and domain tests execute `true`, `false`, and `unknown` signal matrices with claim provenance; repeated identical evaluations, retries, restart, and replay create one condition episode and one incident; activation debounce and clearing hysteresis are independent; unknown during clearing returns to waiting and cannot resolve; connector loss groups only dependent unavailable conditions into one explained visibility investigation; simultaneous fresh host pressure or worker impact remains separately visible; A/B conditions supplied batched, staggered, replayed, A-then-B, and B-then-A produce the identical canonical series, episode ordinal, open episode, membership set, and history; concurrent open attempts converge through unique constraints and atomic re-read/merge; acknowledgement survives unknown state; material escalation returns operator state to unowned and bypasses inadequate suppression; all-member fresh clearing resolves; a condition recurrence joins an already unresolved compatible incident episode, while recurrence after the incident episode's proven resolution allocates the next linked ordinal and monitoring-ended resume does not; exact reads, authoritative totals, stable pagination, and source condition counts survive grouping. |
| #279 | API and browser workflow tests preserve the exact incident series and episode, condition membership, grouping explanation, current/last-confirmed/peak severity, operator state and acknowledgement actor/time/revision, suppression, and return context across Fleet, Node/Profile, Incident, and Support routes; explicit support target selection never uses display-name grouping; a verified or partial diagnostic adds only its authoritative claims; rejected, expired, corrected, slow, leave/return, and exact-session cases do not resolve the incident without a fresh rule-specific clearing signal; route state and grouping membership grant no operation authority; pruned or expired incident links render explicit history state rather than losing investigation context silently. |
| #280 | Browser fixtures render event/evidence details, tri-state condition truth, incident truth summary, ADR-0015 operator state, suppression, current severity, peak severity, escalation, monitoring ended, resolution, and recurrence as distinct dimensions; reporting loss and history-quality notices do not imitate current critical impact; grouped cards state allowlisted "grouped because" reasons and never claim root cause; independent confirmed impact remains prominent beside visibility loss; actionable/unowned is the default while acknowledged, waiting, suppressed, recovering, resolved, monitoring-ended, notices, history-pruned, history-expired, history-unavailable, and legacy unknown-provenance records remain discoverable; 0, 1, 62, 200, and 201-source-record narrow/wide/theme/forced-color/zoom/loading/error cases preserve truthful totals and bounded rendering. |
| #281 | Qualification pins rule, grouping, canonicalization, schema, retention, corpus, PitCrew, and Dashboard versions; real SQLite tests cover disconnect, restart, replay, staggered and concurrent grouping, stale samples, target removal, credential revocation, acknowledgement/unacknowledgement, suppression expiry, escalation, clearing hysteresis, resolution, monitoring-ended resume, recurrence, and restart-safe migration; legacy mapping proves triggered becomes unowned and acknowledged preserves acknowledged actor/time without an assignee; an exact legacy-resolution fixture migrates one provenance-less resolved row into its canonical series as reserved ordinal 0 with `legacy-resolution-unverified`, proves repeated migration is idempotent, proves unknown and fresh false create no normal episode, then applies fresh true evidence and asserts exactly one ordinal-1 episode linked as `reopened-after-unverified-legacy-resolution`, true/unowned state, and current/peak severity derived only from fresh evidence; the same fixture proves arbitrary waiting or monitoring-ended normal episodes cannot open successors; before retention expiry the ordinal-0 tombstone and legacy deep link remain exact, after tombstone compaction ordinal 1 retains `legacy-resolution-history-expired` and the legacy link returns `history-expired`, and after locator expiry the legacy link returns `history-unavailable` without changing ordinal-1 truth, severity, or transition; a separate branch expires both ordinal 0 and its locator before fresh true evidence and proves the ordinary first ordinal-1 episode has no legacy predecessor, recurrence label, or reconstructed metadata; migration fixtures also prove predecessor/successor/merge traceability and explicit history-quality notice policy without deletion or count loss; retention fixtures exercise detail-to-tombstone pruning, referenced tombstone preservation, age and count compaction, expiry-boundary creation, locator expiry, recurrence before and after each boundary, and tenant-safe deep-link responses of retained, history-pruned, history-expired, or history-unavailable; old/new readers fail safely on unknown versions; legacy and new projections reconcile source condition counts while intended incident-count differences are explained; bounded telemetry uses only source family, condition family, lifecycle, outcome, grouping reason, and suppression reason; no release gate treats lower incident count as success or adds an event stream, datastore, N+1 relay read, identity guess, or weakened operational fence. |

# Confirmation

The decision remains confirmed when:

- public and stored contracts keep event occurrence, tri-state signal
  interpretation, condition identity, condition episode, and actionable
  incident episode independently representable;
- every signal can identify its claim provenance and rule interpretation;
- SQLite tests prove atomic idempotent reconciliation and migration under
  retries and restart;
- missing evidence always yields unknown or waiting behavior and never clearing;
- deterministic grouping produces the same canonical series, episode ordinal,
  membership, and explanations regardless of input order, batch boundary,
  replay, or transaction race;
- independent current impact remains visible outside reporting-loss grouping;
- current, last-confirmed, and peak severity remain distinct;
- ADR-0015 operator state remains exactly unowned or acknowledged and changes
  independently from suppression, truth, and severity;
- resolution, monitoring ended, and recurrence preserve their distinct evidence
  requirements and links;
- unverified legacy resolution maps idempotently to reserved ordinal 0, and
  fresh true evidence opens ordinal 1 with an explicit non-recurrence
  transition while unknown states cannot allocate successors;
- exact retrieval, authoritative categorized totals, stable pagination, and
  source evidence remain truthful above the bounded first page;
- historical quality reclassification requires an explicit migration policy and
  preserves source rows and count reconciliation;
- bounded tombstone and expiry-locator tests preserve coherent links and
  explicit deep-link degradation before and after age/count retention; and
- implementation remains on the existing bounded SQLite and outbound evidence
  architecture.

# Relationship to existing decisions

- ADR-0003 remains authoritative for manager-owned workload attribution,
  credential-free evidence, and refusal to infer workload from resource
  activity. This decision prevents grouped incidents from weakening that
  boundary.
- ADR-0005 remains authoritative for outbound-only bounded connector-health
  replay and node-scoped event idempotency. This decision reuses those
  properties without generalizing them into an event platform.
- ADR-0011 remains authoritative that support relay activity is transport
  evidence only and is independent from connector, runner, host, and diagnostic
  health.
- ADR-0012 remains authoritative for support session lifecycle observers and
  Dashboard verification. This decision permits those claims to inform an
  investigation without treating diagnostic completion as remediation.
- ADR-0015 remains authoritative for claim-level authority, clocks, freshness,
  coverage, retention, unavailable state, and incident clearing. This record
  defines the condition and human-work semantics built on those claims.

None of these records is superseded.

# References

- [ADR-0003](adr-0003-manager-owned-workload-attribution.md) demonstrates the
  bounded manager-owned workload and pressure evidence that grouping must not
  infer or conceal.
- [ADR-0005](adr-0005-retrospective-connector-health-replay.md) demonstrates a
  bounded idempotent event ledger within the existing SQLite and outbound-sync
  architecture.
- [ADR-0011](adr-0011-bounded-relay-activity-projection.md) demonstrates that
  support activity is an independent transport claim, not fleet health.
- [ADR-0012](adr-0012-bounded-relay-owned-support-session-lifecycle.md)
  demonstrates observer-owned support lifecycle and verified-result boundaries.
- [ADR-0015](adr-0015-claim-level-evidence-authority-and-freshness.md) defines
  the claim truth and clearing evidence this incident model consumes.
- `src/PitCrew.Dashboard.Features.Fleet/AlertRuleEvaluator.cs` demonstrates the
  current stable condition-like keys, debounce inputs, evidence suppressions,
  and independent node/profile rule families.
- `src/PitCrew.Dashboard.Features.Fleet.Abstractions/AlertOperations.cs`
  demonstrates the current candidate, suppression, incident, acknowledgement,
  and bounded-page contracts that combine several dimensions this decision
  separates.
- `src/Adapters/PitCrew.Dashboard.Adapters.Sqlite/SqliteAlertIncidentStore.cs`
  demonstrates restart-safe reconciliation, current status coupling,
  suppression-preserved open rows, resolved-history bounds, and the existing
  SQLite storage boundary.
- `test-assets/fleet-trust/fleet-trust-scenarios.v1.json` and
  `docs/testing/fleet-trust-scenarios.md` provide the sanitized executable
  baseline for repeated signals, independent conditions, reporting loss,
  retained evidence, true resolution, and cardinality.
- [Issue #272](https://github.com/ncosentino/pitcrew-dashboard/issues/272)
  established the scenario corpus without changing grouping semantics.
- [Issue #277](https://github.com/ncosentino/pitcrew-dashboard/issues/277) owns
  this decision.
