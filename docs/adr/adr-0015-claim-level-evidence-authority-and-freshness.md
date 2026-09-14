---
title: "ADR-0015: Claim-level evidence authority and freshness"
status: "Accepted"
date: "2026-09-13"
authors: ["Nick Cosentino"]
tags: ["architecture", "evidence", "fleet", "incidents", "support", "compatibility"]
supersedes: ""
superseded_by: ""
---

# Context and scope

Fleet, Node, Incidents, and Support currently expose facts produced by different
observers on different clocks. A connector synchronization can be current while
the manager document it carries is stale. A retained workload value can remain
useful after reporting is lost without proving current work. A support session
can be dispatched by the relay without proving that diagnostics ran, and a
verified diagnostic result proves collected evidence without proving
remediation.

The existing contracts preserve several parts of this distinction, but they do
not share one durable model:

- connector reporting is derived from Dashboard's accepted synchronization
  time;
- manager and workload projections carry source observation times;
- API responses carry a later generation time;
- incident evaluation records evaluation-derived times;
- support lifecycle records request, dispatch, expiry, and verified-result
  evidence;
- an incomplete local read can produce an empty profile list, which the current
  SQLite synchronization path treats as authoritative deletion.

The version 1 fleet trust scenario corpus characterizes the resulting boundary.
It proves that fresh connector contact can coexist with stale manager evidence,
reporting loss can coexist with retained manager and workload evidence,
credential identity wins over a disagreeing payload, measured zero differs from
missing evidence, and only fresh authoritative clearing evidence proves
resolution. It also characterizes partial, rejected, timed-out, forbidden, and
exactly retrieved support results.

This decision defines the shared semantic contract for claim authority, source
identity, clocks, freshness, coverage, retention, public state taxonomies,
precedence, compatibility, and incomplete local observation. It governs
additive manager, connector, storage, API, incident, and browser projections.
It does not choose page composition, incident grouping, pagination design,
retention duration, or a particular protocol version number.

It introduces no datastore, generalized health score, credential boundary,
inbound connector path, generic command channel, or automatic remediation.

# Verified facts and assumptions

## Verified facts

- `ManagerObservedState` has a top-level `ObservedAt` and nested source times,
  while `ConnectorSyncRequest` separately has `SentAt` and the response has
  Dashboard `AcceptedAt`. These clocks already represent different events and
  cannot truthfully substitute for one another.
- `SyncConnectorUnitOfWork` resolves the node by the hash of the bearer
  credential before accepting payload evidence. The authenticated node identity
  is therefore independent of display names and payload identity fields.
- `FleetNode.LastSeenAt` is the latest accepted synchronization and
  `FleetResponse.GeneratedAt` is response generation time. The latter does not
  observe either connector or manager state.
- `AlertRuleEvaluator` applies separate connector and manager freshness
  boundaries. It currently suppresses profile diagnoses after connector loss,
  demonstrating that one subsystem's unavailable evidence must not be treated
  as another subsystem's clearing evidence.
- `AlertIncident` separates condition lifecycle from acknowledgement metadata,
  although its current serialized status combines active ownership into
  `triggered` and `acknowledged`.
- ADR-0012 makes relay dispatch authoritative for transport progress, the agent
  authoritative for a bounded rejection, the bounded local broker/node path
  authoritative for diagnostic content, and Dashboard authoritative for
  cryptographic verification and acceptance of that content.
- `ObservedStateReader` retains readable cached profiles when some local files
  fail, but that cache is process-local. A missing or unreadable state root
  returns an incomplete read with an empty list, and a connector restart has no
  durable last-good profile guarantee.
- `SqliteFleetStore.ApplySyncAsync` deletes every retained profile when the
  accepted synchronization contains an empty profile list. The current wire
  shape cannot distinguish a complete measured-empty inventory from failed
  local acquisition.
- The current connector sends one configured protocol version and the current
  synchronization response does not negotiate an alternate evidence
  capability. There is no verified silent-downgrade or durable-cache mechanism
  to preserve incomplete profile semantics with an older receiver.
- Protocol versions 1 through 11 remain accepted, and existing ADRs require
  additive mixed-version behavior with explicit unavailable state.

## Assumptions to confirm during implementation

- The producer change in `ncosentino/pitcrew#195` can add bounded source-family
  coverage and provenance without exposing credentials, workload payloads, raw
  errors, or private host details.
- A connector protocol revision can add observation-envelope fields while
  preserving older request handling and explicit version validation.
- Existing SQLite stores can retain the additive metadata through their current
  domain-specific interfaces; no second database or event broker is required.

# Decision drivers

- Make every operational claim attributable to the observer allowed to assert
  it.
- Prevent transport, evaluation, or response activity from refreshing older
  source evidence.
- Preserve useful retained values without presenting them as current.
- Preserve explicit measured zero while refusing to infer zero from silence,
  omission, or partial coverage.
- Keep incident resolution and operation authorization dependent on fresh,
  appropriate evidence.
- Preserve credential-derived connector identity and independent support
  identity.
- Allow manager, connector, Dashboard, and browser releases to roll
  independently without inventing provenance for legacy data.
- Preserve profiles during incomplete local observation while still allowing a
  complete authoritative inventory to prove removal.
- Reconcile the existing outbound-only, credential-free, typed-operation,
  verified-support, and unavailable-state safeguards.

# Decision

Evidence authority attaches to one claim, not to an entire record, response,
node, profile, incident, or diagnostic session. Every projected claim uses the
following independent dimensions:

| Dimension | Contract |
| --- | --- |
| Claim | A stable, bounded fact such as connector contact, manager lifecycle, workload activity, incident condition, diagnostic dispatch, or diagnostic result. |
| Authority | The source family allowed to assert that claim. Authority for one claim grants no authority over adjacent claims. |
| Source identity | The stable identity of the authoritative observer, when the source family has one. It is never inferred from a display name. |
| Source-observed time | When the authority observed or produced the underlying fact. It may be absent only when no usable source observation exists or a legacy contract did not carry it. |
| Dashboard-received time | When Dashboard durably accepted the evidence. It orders receipt and retention; it does not refresh source truth. |
| Evaluated time | When Dashboard applied freshness, policy, or incident rules to the evidence. |
| Verified time | When Dashboard successfully authenticated and validated evidence that requires cryptographic or schema verification. It is separate from source completion, receipt, generic rule evaluation, and response generation. It is absent when verification did not succeed or a legacy row did not retain it. |
| Response-generated time | When the API projection was assembled. It describes response age only. |
| Freshness boundary | The exclusive instant until which a present-state claim is current under its claim-specific policy. At or after the boundary the claim is stale. Immutable historical claims use no boundary and cannot establish present state merely because they remain valid history. |
| Coverage | `complete`, `partial`, or `unavailable` for the sources required by that claim. |
| Retention | `live` or `last-known`. Last-known evidence remains attributable and timestamped but cannot prove current state. |
| Value | A bounded value only when the authority and coverage permit it. Unavailable evidence has no invented value. |
| Unavailable reason | A closed, bounded category owned by the component that observed the failure. It contains no raw exception, command, credential, path, or private payload. |

`complete` means every source required by the claim reported usable evidence.
It does not mean that every claim in the enclosing record is complete.
`partial` means at least one required source reported usable evidence and at
least one did not. `unavailable` means no usable value exists for the claim.
`last-known` means a previously accepted value is retained after its live
source is unavailable or no longer current. A last-known value can be complete
for its original observation and still be stale or unusable for a current
decision.

Freshness is evaluated per claim. A present-state claim is current only when
its required coverage is present and
`evaluatedAt < freshnessBoundary`. The source-observed time normally determines
that boundary. Connector contact is the deliberate exception: connector
`SentAt` remains distinct source metadata, while Dashboard's accepted receipt
time starts the reporting freshness boundary because Dashboard is authoritative
for whether contact was accepted. Receipt, evaluation, and response generation
never move another claim's source-observed time or freshness boundary.

## Claim authority and state matrix

| Claim family | Authority and source identity | Authoritative observation time | Complete/current meaning | Partial, unavailable, or last-known meaning |
| --- | --- | --- | --- | --- |
| Connector reporting | Dashboard acceptance for the node resolved from the connector credential | Connector request creation is source metadata; Dashboard accepted synchronization time is the authoritative contact and freshness origin | A synchronization from that credential-derived node was committed inside the connector boundary | Overdue or absent receipt proves reporting unavailable, not host failure; the last receipt is retained context |
| Connector health cause | Connector-owned bounded health journal for the credential-derived node | Connector event observation time; Dashboard receipt remains separate | The connector reported a bounded cause or recovery interval | No journal or unreachable connector leaves cause unavailable; silence never supplies a cause |
| Manager lifecycle and local sources | Manager-owned observation, scoped by profile and manager source identity | Manager or nested source observation time | Required manager source families reported usable evidence inside their boundaries | Missing families are named partial/unavailable; an older accepted value is last-known and cannot be refreshed by connector contact |
| Workload activity | Manager-owned job, slot, registration, or scale-set evidence for the exact workload claim | The workload source's own observation or lifecycle time | `reported-active` or explicit `measured-zero` is supported by complete current coverage | Resource activity cannot infer work; absent, partial, stale, or retained evidence yields unknown current work |
| Host resource and pressure evidence | The manager source that sampled the stated Docker host, VM, or physical-host scope | Sample time | A bounded measurement describes only its declared host scope | Missing samples do not prove zero pressure, host failure, or recovery |
| Incident condition | Dashboard rule evaluation over the fresh claims required by that rule | Source times remain attached; evaluation time records the decision | Fresh rule-specific evidence proves active or proves cleared | Missing/partial/stale evidence produces waiting for evidence; acknowledgement and diagnostic success cannot clear the condition |
| Diagnostic authorization | Dashboard authorization for the tenant, support identity, mode, and optional profile | Request authorization time | The request was authorized for the exact bounded scope | Rejection proves only that authorization or validation failed |
| Diagnostic transport | Relay for first dispatch and terminal transport state; agent for a closed rejection disposition | Relay dispatch/transition or agent rejection time | The exact session reached the stated transport stage | Relay completion alone does not prove a valid result; missing relay evidence leaves the prior monotonic state unchanged |
| Diagnostic result | The bounded local broker produces the report content; the node support agent attests it with the pinned support identity; Dashboard verifies and accepts rather than authors the evidence | Broker/node observation and completion remain source times; Dashboard receipt, verification, generic evaluation, and response generation remain separate | Result claims are complete or partial exactly as the authenticated, schema-valid report states; terminal `completed` projection occurs only after Dashboard verification | Rejected, timed-out, missing, or unverifiable results are unavailable; authentic content proves only its bounded claims and does not prove remediation |
| API projection | Dashboard response generation | Response-generated time | The response truthfully projects the underlying claim dimensions | A fresh response does not make its contents current |

## Public state taxonomies

Serialized names may retain compatible legacy fields, but new domain and UI
projections use these meanings consistently.

| Surface | Closed semantic states |
| --- | --- |
| Connector reporting | `current`, `overdue`, `never-reported`, `revoked`. `overdue` replaces host-like wording such as "host offline"; it means no synchronization was accepted inside the boundary. |
| Manager observation | `current`, `stale`, `partial`, `unavailable`, `last-known`. Lifecycle values such as running or stopped remain separate from evidence state. |
| Workload evidence | `reported-active`, `measured-zero`, `partial`, `unavailable`, `last-known`. Current work is unknown for the last three states. |
| Incident condition | `triggered`, `waiting-for-evidence`, `resolved`, `monitoring-ended`. Resolution requires fresh authoritative clearing evidence. Monitoring ended records that observation ceased permanently, such as revocation, without claiming recovery. |
| Incident ownership | `unowned`, `acknowledged`. Ownership is orthogonal to condition truth; acknowledgement never resolves or refreshes evidence. |
| Diagnostic readiness | `ready`, `target-selection-required`, `setup-required`, `forbidden`, `unavailable`. Connector reporting and support readiness remain independent. |
| Diagnostic execution | `queued`, `dispatched`, `completed`, `rejected`, `cancelled`, `expired`, preserving ADR-0012. Completed results separately declare `complete`, `partial`, or `unavailable` claim coverage. |

Legacy incident `triggered` maps to active/unowned and `acknowledged` maps to
active/acknowledged. A legacy `resolved` row without clearing provenance remains
historical but is labeled resolution provenance unavailable; it cannot be used
as fresh clearing evidence. New projections do not rewrite such a row with
invented source or evaluation times.

## Precedence and inference rules

1. Credential-derived connector identity overrides any payload identity or
   display-name association. Fleet and support identities remain independent
   unless a separately accepted identity contract proves an association.
2. A claim's authority, source time, coverage, and freshness control that claim.
   Fresh evidence for one subsystem cannot refresh, clear, or authorize from
   another subsystem.
3. Fresh authoritative evidence outranks retained or stale evidence for the same
   claim. Retained evidence remains visible for context and audit.
4. Partial evidence can support only the claims whose required sources are
   present. It cannot be promoted to complete at the enclosing-record level.
5. Missing, malformed, unsupported, stale, partial, or unavailable evidence
   cannot prove healthy, zero, inactive, resolved, remediated, or safe to mutate.
6. Numeric zero is valid only when the authoritative source explicitly measured
   the value with complete current coverage. Empty collections have the same
   rule: measured empty differs from unknown inventory.
7. Incident acknowledgement changes ownership only. Verified diagnostics add
   evidence only. Neither action changes the underlying condition without a
   fresh rule-specific clearing observation.
8. Operations authorize from fresh claim-specific evidence and existing
   authorization and generation fences. Route state, display context, response
   generation time, and legacy unknown-provenance fields grant no authority.
9. Diagnostic content, authenticity, receipt, lifecycle projection, and
   remediation are separate claims. The local broker/node path authors bounded
   content, the pinned node identity attests it, Dashboard records receipt and
   verification, ADR-0012 governs terminal projection, and only later
   rule-specific source evidence can prove remediation.

## Incomplete local observation and retained profiles

The connector may report an explicit incomplete observation without deleting
retained profiles. The additive synchronization contract distinguishes:

- a complete authoritative profile inventory, including a legitimate measured
  empty inventory;
- a partial inventory containing the profiles that were read successfully and
  bounded unavailable claims for failed sources; and
- an unavailable inventory when no profile state could be read.

Only a complete authoritative inventory may prove that an omitted profile was
removed and may delete its current projection. On a compatible contract,
partial or unavailable inventory updates connector reporting and bounded
acquisition evidence, preserves the last-known profile projections, and marks
affected claims unavailable or last-known. It does not send a success-shaped
empty set, infer removal, or silently extend retained evidence freshness.

This partial-progress rule applies only when the receiver contract can
represent inventory coverage and unavailable claims. There is no silent
protocol downgrade. If an older supported receiver cannot preserve those
semantics, the sender withholds the profile-bearing synchronization, or the
whole synchronization when the payload is indivisible. The receiver retains
its prior profile state, connector reporting becomes overdue/unavailable, and
the sender records bounded local incompatibility evidence while Dashboard
derives connector non-reporting from the missing accepted synchronization. The
sender retries after a compatible upgrade. This is a narrow mixed-version
safety exception, not a requirement to reject claim-independent progress when
the accepted contract can carry partial claims safely.

The current connector's last-good cache is process-local. Total state-root loss,
an unreadable root after restart, or another restart before a complete read has
no durable last-good guarantee. The connector must not reconstruct missing
profiles, replay cached values it does not possess, or assign new observation
times to retained Dashboard state.

If a complete inventory later proves removal, Dashboard may remove the current
profile projection under existing retention rules. After an incompatible
mixed-version interval, deletion resumes only after a compatible contract
delivers a complete authoritative inventory. Historical evidence remains
subject to its existing bounded retention rather than being rewritten.

## Additive compatibility and legacy behavior

Manager, connector, storage, API, and browser changes are additive:

- PitCrew manager producers add source-family coverage, provenance, and
  unavailable categories through a new compatible manager contract revision.
  Older consumers may ignore additive fields. A consumer that recognizes a
  contract revision rejects malformed required fields rather than guessing.
- The connector protocol adds an observation envelope or equivalent bounded
  fields for inventory coverage, source identity, source-observed times, and
  unavailable categories. Existing protocol versions remain accepted.
- Existing protocol support does not imply that an older receiver can preserve
  a newer incomplete-inventory meaning. A sender never silently downgrades an
  incomplete observation into the older profile-list shape.
- A sender may mutate receiver profile inventory only through an accepted
  contract that represents complete, partial, unavailable, and authoritative
  empty inventory. With an incompatible older receiver it withholds the
  profile-bearing request, or the indivisible whole request, retains no
  invented durable cache, records bounded local incompatibility evidence, lets
  Dashboard derive non-reporting from absent accepted contact, and retries
  after upgrade.
- Compatible partial contracts continue to accept claim-independent connector
  and successfully observed profile progress. The mixed-version exception does
  not turn every partial local read into a total synchronization outage.
- A new Dashboard accepts older connectors and managers. Unsupported claim
  dimensions are `unavailable` or `last-known`; source identity and timestamps
  are not synthesized from receipt or response time.
- Existing serialized fields remain available during migration. New fields are
  the authority for new consumers when present and valid. Contradictory
  combinations fail validation rather than selecting a convenient value.
- Existing SQLite databases receive nullable or additive metadata through the
  existing fleet, incident, and support storage boundaries. Legacy rows remain
  readable with unknown provenance. No data migration invents source-observed,
  freshness-boundary, clearing, or diagnostic-verification times.
- Support storage and APIs retain source completion/observation, Dashboard
  receipt, Dashboard verification, lifecycle transition, generic evaluation,
  and response generation as distinct nullable fields where applicable. A
  legacy completed result remains readable, but an absent verification
  timestamp stays unavailable rather than being copied from receipt or
  completion.
- Existing protocol behavior that lacks connector health replay, support
  rejection reporting, or manager provenance degrades to the accepted
  unavailable or eventual-expiry behavior in ADR-0005 and ADR-0012.

# Relationship to existing decisions

- ADR-0003 remains authoritative that workload attribution is manager-owned,
  credential-free, bounded, and unavailable rather than inferred. This record
  generalizes those semantics to every claim without adding GitHub credentials
  or cancellation.
- ADR-0005 remains authoritative for outbound-only connector-health replay,
  acknowledgement, receipt-time retention, and older connectors. This record
  prevents replay or a recovered synchronization from refreshing unrelated
  manager and workload claims.
- ADR-0007 remains authoritative for deterministic browser evidence and durable
  UX terminology. This record supplies the truth matrix that fixtures and
  browser assertions must render; it does not make subjective design review a
  merge authority.
- ADR-0008 remains authoritative for the independent, read-only support plane,
  opaque relay, separate support identity, and prohibition on commands,
  arbitrary paths, or remediation. This record keeps support readiness separate
  from connector reporting, assigns result content to the bounded local
  producer authenticated by the pinned identity, and limits accepted results to
  the claims they contain.
- ADR-0012 remains authoritative for monotonic relay-owned dispatch, bounded
  agent rejection, and Dashboard-owned result verification before completed
  projection. Dashboard verification establishes authenticity and acceptance,
  not authorship of broker/node content. This record adds no lifecycle
  transition; it defines how lifecycle evidence participates in the shared
  claim model.

None of these records is superseded.

# Alternatives considered

| Option | Advantages | Drawbacks | Decision |
| --- | --- | --- | --- |
| Claim-level authority, clocks, coverage, freshness, and retention | Preserves subsystem boundaries; represents measured zero, partial evidence, and retained context; supports safe authorization and mixed versions without inference | Adds contract fields, validation, migrations, terminology, and cross-surface test combinations | Selected |
| One status and timestamp for each node, profile, incident, or response | Small payloads and simple rendering; resembles the current `IsOnline` and generated-time projections | A fresh wrapper makes stale nested evidence look current; one unavailable source contaminates or hides unrelated claims; cannot represent partial coverage safely | Rejected |
| A generalized fleet or node health score | Gives operators one sortable value and can compress many signals | Conceals authority and missing evidence, requires arbitrary weighting, encourages inferred health and resolution, and cannot authorize safe operations | Rejected |
| Treat omitted profiles or an empty list as authoritative deletion | Preserves the current storage shape and quickly removes stale inventory | Cannot distinguish measured empty from failed acquisition; destroys retained evidence and can falsely resolve conditions | Rejected |
| Add an event-sourced evidence database or broker | Could retain every provenance transition and support later replay | Introduces a new datastore and operating model without measured need; duplicates existing bounded SQLite projections and contradicts the single-replica constraint | Rejected |
| Silently downgrade incomplete observations to an older profile-list contract | Preserves heartbeat traffic and avoids an upgrade dependency | Converts unknown inventory into deletion or false completeness, invents compatibility behavior, and can replay false freshness | Rejected |
| Withhold profile-bearing synchronization from an incompatible older receiver | Preserves receiver state and prevents destructive ambiguity without adding a cache or negotiation protocol | Connector reporting may become overdue and claim-independent fields in an indivisible payload are delayed until compatible upgrade | Selected as the narrow mixed-version exception |
| Stop synchronizing entirely whenever any local source is incomplete, even when the accepted contract carries partial claims | Avoids partial records and accidental deletion | Hides current connector reporting and successfully read claims, delays bounded failure evidence, and makes one bad profile suppress independent profiles | Rejected |
| Treat Dashboard verification as authorship of diagnostic content | Gives one central component apparent authority over the completed result | Conflates producer truth with authenticity checking, obscures the pinned node attestation boundary, and makes verification time indistinguishable from collection or receipt | Rejected |

# Consequences

## Positive

- Every material claim can state who observed it, when it was observed,
  whether required sources were covered, and whether it remains current.
- Fresh connector contact no longer lends freshness to manager, workload,
  incident, or diagnostic claims.
- Operators can see retained values without mistaking them for current truth.
- Measured zero and measured empty remain usable while missing evidence fails
  closed.
- Incomplete acquisition no longer erases retained profiles or manufactures
  resolution.
- Diagnostic content authority, node authenticity, Dashboard verification,
  terminal lifecycle projection, and remediation become independently testable.
- Incident ownership, condition truth, diagnostic readiness, execution, and
  verified result coverage become independently testable.
- Mixed-version fleets degrade explicitly instead of receiving invented
  provenance.

## Negative

- Protocol, storage, API, schema, fixture, and browser surfaces gain additional
  fields and validation branches.
- During rollout, old consumers may show their legacy projection while new
  consumers show stronger unknown, partial, or last-known states.
- Claim-specific freshness policies require disciplined ownership and can no
  longer be replaced by one convenient generated timestamp.
- Preserving profiles after incomplete observation requires an explicit later
  complete inventory before removal is reflected.
- During an incompatible mixed-version interval, connector reporting and any
  claim-independent fields in an indivisible request can be delayed to protect
  receiver inventory.
- Total local state-root loss after connector restart may leave no local
  last-good profile copy; Dashboard retention remains last-known evidence and
  must not be refreshed or reconstructed.

## Neutral constraints

- This contract does not establish physical-host reachability from connector
  reporting.
- It does not determine incident grouping, authoritative total-count transport,
  page layout, or support/fleet identity association.
- It does not make diagnostics a remediation or mutation channel.
- Existing bounded retention still applies to historical evidence.

# Executable downstream acceptance cases

Documentation-only acceptance for this decision is the stable semantic matrix
below. Each implementation issue must capture its own pre-change failure and
post-change pass against these cases; this ADR does not fabricate red evidence.

| Owner | Executable acceptance cases |
| --- | --- |
| `ncosentino/pitcrew#195` | Contract fixtures prove complete, partial, unavailable, stale nested, and measured-zero source families; each carries bounded authority/source identity and source observation time; malformed required metadata fails closed; contract-18/19 and new/old consumer matrices preserve compatibility without invented provenance. |
| #273 | Incident tests prove connector loss moves dependent conditions to waiting for evidence rather than resolved; fresh rule-specific clearing evidence alone resolves; acknowledgement changes ownership only; legacy resolved rows expose unavailable clearing provenance; 0, 1, 62, 200, and 201-record cases preserve visible count, truncation, and exact tenant-scoped retrieval. |
| #276 | Protocol, SQLite, API, and projection tests execute the complete claim/state matrix; a fresh response and fresh connector receipt leave stale manager/workload claims stale; explicit measured zero renders `0`; a compatible partial/unavailable inventory preserves retained profiles while accepting independent claims; an incompatible older receiver receives no profile mutation or silent downgrade and retains prior state while connector reporting becomes unavailable; restart with total state-root loss proves there is no durable replay guarantee or invented freshness; deletion resumes only after a compatible complete measured-empty inventory. |
| #279 | Browser and API tests prove connector reporting loss does not block an independently ready support identity; target selection is explicit without display-name association; route context grants no authority; local broker/node content authority, pinned-node authenticity, Dashboard receipt, Dashboard verification time, ADR-0012 terminal projection, and remediation state remain distinct; rejected, expired, partial, and exact leave/return results preserve incident context; verified diagnostics do not resolve the incident or enable a stale/prohibited operation. |
| #280 | Browser fixtures render connector reporting, manager observation, workload evidence, incident condition/ownership, diagnostic readiness, and execution as separate taxonomies; copy never labels reporting loss as host offline or retained evidence as healthy/current; urgent styling follows current severity rather than unavailable/retained state; narrow, wide, theme, forced-color, zoom, loading, partial, unavailable, and long-content checks consume the shared scenario corpus. |

# Confirmation

The decision remains confirmed when:

- schemas and public contracts keep authority, source identity,
  source-observed, Dashboard-received, evaluated, verified, and
  response-generated times, freshness boundary, coverage, retention, value,
  and unavailable reason independently representable at claim level;
- protocol tests prove additive old/new manager, connector, and Dashboard
  combinations, no silent incomplete-observation downgrade, compatible partial
  progress, and the narrow incompatible-receiver withholding behavior;
- storage tests prove legacy nullable behavior and preserve profiles on
  incomplete inventory while allowing complete authoritative removal only
  after a compatible complete inventory;
- incident tests prove waiting-for-evidence, ownership independence, fresh
  clearing requirements, monitoring-ended behavior, and legacy unknown
  resolution provenance;
- support tests preserve ADR-0008 content/identity boundaries and ADR-0012
  lifecycle authority while distinguishing producer content, node attestation,
  Dashboard receipt, verification, terminal projection, result coverage, and
  remediation;
- browser tests execute the versioned fleet trust scenarios with contract-true
  terminology; and
- no implementation adds a datastore, health score, credential boundary,
  inbound connector path, generalized command channel, or inferred workload or
  host truth.

# References

- [ADR-0003](adr-0003-manager-owned-workload-attribution.md) demonstrates the
  existing manager-owned, credential-free workload authority and the requirement
  to report unavailable rather than infer attribution.
- [ADR-0005](adr-0005-retrospective-connector-health-replay.md) demonstrates
  outbound replay, Dashboard receipt-time retention, and explicit unavailable
  behavior for older connectors.
- [ADR-0007](adr-0007-browser-ux-evidence-and-design-authority.md) makes
  deterministic stale and unavailable browser states part of the repository's
  executable UX boundary.
- [ADR-0008](adr-0008-support-plane-v1-read-only-diagnostics.md) establishes the
  independent support identity, opaque relay, read-only diagnostic boundary,
  and prohibition on a generic remote operation.
- [ADR-0012](adr-0012-bounded-relay-owned-support-session-lifecycle.md) assigns
  dispatch, rejection, and verified-completion claims to their actual
  observers and defines mixed-version degradation.
- `src/PitCrew.Protocol/PitCrewProtocol.cs` demonstrates the current manager
  observation, connector sent, and Dashboard accepted clock surfaces and the
  protocol 1-through-11 compatibility boundary.
- `src/PitCrew.Connector.Features.Sync/ObservedStateReader.cs` demonstrates
  bounded local acquisition, process-local cached last-good profiles, the lack
  of a durable restart guarantee, and the ambiguity between an incomplete empty
  read and authoritative empty inventory.
- `src/PitCrew.Dashboard.Features.Fleet/SyncConnectorUnitOfWork.cs` and
  `src/Adapters/PitCrew.Dashboard.Adapters.Sqlite/SqliteFleetStore.cs`
  demonstrate credential-derived node resolution and the current empty-list
  profile deletion behavior this decision makes explicit.
- `src/PitCrew.Dashboard.Features.Fleet/AlertRuleEvaluator.cs` demonstrates
  separate connector and manager freshness boundaries and current suppression
  when evidence is unavailable.
- `src/PitCrew.Dashboard.Features.Support/SupportApiContracts.cs` demonstrates
  the existing separation of request, dispatch, expiry, rejection, and verified
  result fields.
- `test-assets/fleet-trust/fleet-trust-scenarios.v1.json` and
  `docs/testing/fleet-trust-scenarios.md` provide the sanitized executable
  baseline for claim authority, independent clocks, retained evidence, measured
  zero, identity mismatch, true resolution, and diagnostic outcomes.
- [Issue #275](https://github.com/ncosentino/pitcrew-dashboard/issues/275)
  owns this decision.
