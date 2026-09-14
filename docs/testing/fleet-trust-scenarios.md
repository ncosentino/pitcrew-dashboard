# Fleet trust scenario corpus

The version 1 fleet trust corpus is sanitized, deterministic executable evidence
for reporting, freshness, incident-cardinality, condition-decomposition, and
support-diagnostic failures. It is a characterization baseline for later API,
storage, and browser work; it does not change production projection, grouping,
or lifecycle semantics.

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
- Credential-derived connector identity is authoritative. A disagreeing payload
  identity does not replace it.
- The current incident decomposition uses one stable candidate key for one
  persistent condition episode. Independent condition keys remain separate;
  no cross-condition grouping is performed.
- Incident reads are bounded to 200 by default. The page exposes truncation but
  no authoritative total, so the 201-record case intentionally expects 200
  visible records and `truncated: true`.
- Completed support results are durably stored and exact tenant-scoped session
  reads return the same verified result after leaving and returning.
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
queries the production store with the characterized 200-record limit, verifies
truncation, and confirms that the current page does not invent an authoritative
total. `SqliteSupportStoreTests` computes the expected result digest before
persistence, creates a fresh store context, reads the exact tenant/session
record again, and verifies both returned values against that independent digest.

Changes to the corpus require updating its typed contract tests. Projection,
freshness, pagination, and grouping changes should consume these scenarios and
change expectations only in their owning issues rather than deleting original
signals or conditions.
