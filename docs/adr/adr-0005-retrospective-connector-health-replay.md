---
title: "ADR-0005: Retrospective connector health replay"
status: "Accepted"
date: "2026-08-07"
authors: ["Nick Cosentino"]
tags: ["architecture", "connectors", "observability", "security"]
supersedes: ""
superseded_by: ""
---

# Context and scope

Dashboard determines connector liveness from the last accepted outbound
synchronization. When that channel fails, the cause exists only on the node.
The local connector health journal retains bounded, sanitized failures and
recovery intervals, but Dashboard cannot receive that evidence until the same
channel recovers.

This decision governs replay of connector-owned health evidence, its
acknowledgement, and its bounded SQLite projection. It does not add an inbound
node API, a second telemetry channel, workflow logs, connector identity, or a
generic remote operation.

# Decision drivers

- Preserve the outbound-only connector security boundary.
- Make lost responses and connector restarts safe without duplicate history.
- Retain the failure that explains an outage rather than inferring a cause from
  the absence of heartbeats.
- Keep older connectors compatible with explicitly unavailable evidence.
- Bound request size, local acknowledgement state, and Dashboard storage.
- Commit current fleet state and replayed evidence atomically.

# Decision

Connector protocol 10 adds one optional health replay envelope to the existing
synchronization request. The envelope contains the connector's bounded current
health snapshot and at most 256 sanitized journal events. Dashboard validates
known states, event kinds, failure categories, profile identifiers, timestamps,
retry values, and connector-owned detail text before persistence.

The normal synchronization response acknowledges every event identifier
durably accepted in the fleet transaction. The connector stores a bounded local
set of acknowledged identifiers beside the health journal. A lost response
therefore causes safe redelivery, while a restart suppresses events already
acknowledged. Event identifiers are node-scoped idempotency keys.

SQLite stores one current connector-health row per node and an event ledger
keyed by node and event identifier. The replay write shares the existing fleet
and history transaction, so a failed health write cannot advance the node
heartbeat alone. Retention uses event receipt time plus a hard per-node count.

Protocol 1 through 9 requests remain valid without replay fields. Protocol 10
connectors may omit the envelope when the local journal is absent or unreadable;
Dashboard then reports the reason as unavailable rather than inventing it.

# Alternatives considered

## Add a second connector-health endpoint

A separate endpoint could have a narrower payload, but it would create another
delivery lifecycle and could still fail with the primary channel. Reusing the
authenticated synchronization transaction keeps ordering and identity
unambiguous.

## Poll or connect inbound to the node

Inbound collection could diagnose the channel while it is down, but it adds
reachability, firewall, and credential management that contradict the existing
outbound-only architecture. It is rejected.

## Upload rolling text logs

Logs contain unbounded exception and environment-adjacent text and are not a
stable schema. The connector-owned journal won because its fields, categories,
retention, and redaction are explicit.

## Send only the latest snapshot

A snapshot proves current state but can lose the sequence and category that
explains a recovered outage. The bounded event ledger preserves that evidence
without creating an unbounded log store.

## Rely on duplicate inserts without acknowledgements

Database uniqueness would protect Dashboard, but every connector would resend
its entire retained journal forever. A bounded local acknowledgement projection
reduces traffic while preserving safe redelivery after lost responses.

# Consequences

Recovered connector outages become centrally explainable after the normal
channel returns. Dashboard still cannot know the cause while the connector is
unreachable, and the UI must state that limitation.

The connector performs additional bounded local reads and writes. Failure to
persist an acknowledgement does not fail synchronization; it causes harmless
redelivery. A successful synchronization necessarily precedes creation of its
local recovery event, so a request that carried an active outage schedules one
immediate follow-up to replay that exact recovery. Ordinary success events wait
for the normal heartbeat and do not amplify steady-state polling.

Dashboard gains node-scoped health history and retention settings. The history
uses connector timestamps for the outage interval and Dashboard receipt time
for retention, so clock skew cannot preserve rows indefinitely.

# Confirmation

Protocol tests cover version 9 compatibility and version 10 replay and
acknowledgement serialization. Connector tests cover restart-safe
acknowledgement, lost-response redelivery, truncation, and absent-journal
compatibility. Feature and SQLite tests cover validation, atomic persistence,
event idempotency, current projection, retention, and tenant-scoped reads.

# References

- ADR-0001 preserves outbound-only connector communication and rejects a
  generic command bus.
- ADR-0003 keeps operational evidence credential-free and requires unavailable
  state instead of inferred workload truth.
