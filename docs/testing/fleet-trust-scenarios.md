# Fleet trust scenario corpus

The version 1 fleet trust corpus is sanitized, deterministic executable evidence
for reporting, freshness, incident-cardinality, condition-decomposition, and
support-diagnostic failures. It is the shared evidence vocabulary for API,
storage, and browser tests. Production incident integrity consumes its clocks
and clearing scenarios without changing the corpus's grouping vocabulary.

The portable source is
`test-assets/fleet-trust/fleet-trust-scenarios.v1.json`. The
`PitCrew.Dashboard.Trust.Scenarios` project embeds that exact document and
exposes typed records plus deterministic incident expansion. .NET tests should
reference the project. Browser tests should import the JSON source so both
stacks execute the same scenario identities and clocks.

The loader rejects unknown or missing members and validates the closed version 1
state, freshness, authority, mode, outcome, failure-stage, and return-context
vocabularies. It also rejects duplicate identities, invalid clock ordering,
unavailable claims with invented values, cardinality drift, decomposition drift,
and inconsistent support result state. Malformed JSON and semantic failures are
reported through one bounded `InvalidOperationException` boundary.

## Characterized current behavior

- Connector reporting uses the time Dashboard accepted synchronization.
  Manager and workload freshness continue to use their source observation time;
  a new response timestamp does not refresh either claim.
- Reporting loss produces the connector-offline diagnosis and suppresses
  profile diagnoses. Retained manager and workload evidence remains evidence,
  but it does not prove current workload state or true resolution.
- Fresh connector contact can coexist with stale manager evidence.
- Manager contract 21 source-family provenance is projected without replacing
  source observation time with connector receipt or response generation time.
  Legacy manager records retain their values with unsupported provenance
  explicitly unavailable.
- Connector protocol 12 distinguishes complete, partial, and unavailable
  profile inventory. Partial or unavailable acquisition preserves omitted
  profiles as last-known evidence; only a complete inventory, including a
  measured empty inventory, may remove them.
- Partial or unavailable inventory cannot authorize capacity, recovery, or
  image-rollout work. A complete inventory also cannot refresh an older
  operation capability; protocol-12 capability receipt must be at least as
  recent as the accepted inventory receipt.
- Credential-derived connector identity is authoritative. A disagreeing payload
  identity does not replace it.
- One stable candidate key remains one persistent condition episode. Activated
  conditions project into deterministic actionable incident series using the
  grouping policy version, closed incident family, canonical authoritative
  target scope, investigation class, and evidence dependency. Compatible
  visibility conditions group without deleting source conditions; independent
  investigation classes remain separate.
- Incident reads are bounded to 200 by default and expose authoritative tenant-
  scoped totals, current-severity counts, attention-ranked stable continuation
  cursors, and explicit truncation. Critical unowned incidents precede less
  urgent records even when newer noncritical history exceeds the page bound.
  Exact tenant-and-incident reads support deep links beyond the first page
  without widening the page bound.
- Candidate absence is not clearing evidence. An active incident changes to
  resolved only when the same rule and stable condition key produce a fresh
  clearance. Missing, stale, disconnected, suppressed, or removed evidence
  projects `waiting-for-evidence`; revoked observation contracts project
  `monitoring-ended`. Both preserve the unresolved episode, last-confirmed
  severity, peak severity, and source facts without presenting them as current.
- Connector disconnection suppresses profile evaluation without discarding
  node-scoped conditions. The retained incident remains operator-visible while
  current profile evidence is unavailable.
- Condition truth, current severity, and operator ownership are independent.
  Acknowledgement and undo bind to the current incident revision and append
  immutable actor, action, revision, and occurrence-time audit records in the
  same transaction. Material warning-to-critical escalation advances the
  revision and returns ownership to unowned without deleting prior audit events.
- Incident contracts distinguish source observation, Dashboard receipt, rule
  evaluation, and response generation. Existing resolved rows without
  verifiable clearing provenance become reserved ordinal-0 history. Fresh
  authoritative true evidence opens ordinal 1 with
  `reopened-after-unverified-legacy-resolution`; unknown and false evidence do
  not allocate a successor or invent recovery.
- Completed support results are durably stored and exact tenant-scoped session
  reads return the same verified result after leaving and returning. Result
  source completion, Dashboard receipt, verification, lifecycle completion,
  and response generation remain separate clocks; legacy rows do not receive
  invented receipt or verification times.
- Omitted support profiles are accepted by the current report validator when the
  broker returns a valid local profile. The corpus labels that result ambiguous
  rather than guessing which profile the operator intended.
- Missing, rejected, timed-out, partial, stale, and retained evidence does not
  prove healthy, zero, or resolved. Only the `true-resolution` scenario carries
  fresh authoritative clearing evidence.

## Scenario coverage

The corpus includes:

- current connector reporting with current manager evidence;
- reporting loss with retained manager and workload evidence;
- fresh connector contact with stale manager observations;
- credential identity mismatch and fresh proven resolution;
- incident cardinalities 0, 1, 62, 200, and 201;
- repeated signals for one persistent condition and three independent
  conditions;
- explicit-profile success, omitted-profile ambiguity, policy rejection,
  timeout, partial evidence, invalid diagnostic scope, and exact result
  retrieval after navigation.

Every fleet and support scenario carries separate `sourceObservedAt`,
`dashboardReceivedAt`, `evaluatedAt`, and `responseGeneratedAt` values.
Claim-level source and receipt timestamps preserve subsystem differences inside
the same scenario. Claim `state` describes availability and authority, while
`freshness` independently distinguishes current, stale, retained, partial, and
unavailable evidence.

`SqliteAlertIncidentStoreTests` persists 201 independently keyed incidents,
queries the production store with the 200-record limit, verifies the
authoritative total and continuation boundary, and reads an exact tenant-scoped
incident outside the first page. Integrity scenario tests also cover explicit
clock persistence, waiting and monitoring-ended projections, stale replay,
restart/resume, node-scoped disconnect suppression, revisioned escalation,
acknowledgement audit history, and duplicate-free attention pagination.
Actionable projection tests additionally cover repeated corpus signals,
order-independent grouping and display selection, tenant-isolated regrouping,
independent investigations, replay-safe recovery hysteresis, concurrent open
convergence, suppression expiry and bypass on material escalation, recurrence
ordinals, exact pruned-history reads, expiry locators, and legacy ordinal-0
compatibility.
`SqliteSupportStoreTests`
computes the expected result digest before persistence, creates a fresh store
context, reads the exact tenant/session record again, and verifies both returned
values against that independent digest.

Changes to the corpus require updating its typed contract tests. Projection,
freshness, pagination, and grouping changes should consume these scenarios and
change expectations only in their owning issues rather than deleting original
signals or conditions.
