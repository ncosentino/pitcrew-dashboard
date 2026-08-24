---
title: "ADR-0011: Bounded relay activity projection"
status: "Accepted"
date: "2026-08-23"
authors: ["Nick Cosentino"]
tags: ["architecture", "support", "relay", "observability"]
supersedes: ""
superseded_by: ""
---

# Context and scope

[ADR-0008](adr-0008-support-plane-v1-read-only-diagnostics.md) separates
Dashboard authorization and decryption from an opaque relay that authenticates
nodes and stores encrypted envelopes. The Dashboard support identity contract
already exposes `lastPollAt` and `lastResultAt`, but Dashboard cannot produce
those values: accepted polls and successful result uploads occur at the relay.
Consequently, the support page reports unavailable activity even while a node is
successfully polling and completing diagnostics.

Dashboard issue
[ncosentino/pitcrew-dashboard#157](https://github.com/ncosentino/pitcrew-dashboard/issues/157)
requires trustworthy poll evidence without widening relay visibility into
diagnostic contents, credentials, or private node state.

# Decision drivers

- Report only activity the relay directly accepted.
- Preserve ADR-0008 relay opacity and tenant isolation.
- Avoid one network request per identity.
- Bound request and response size independently from tenant growth.
- Never replace known activity with missing or older evidence.
- Keep support status separate from connector, runner, and host health.

# Decision

The relay stores two nullable UTC timestamps with each registered node:

- `last_poll_at` advances in the same transaction after the node credential is
  accepted, whether or not a session is available.
- `last_result_at` advances in the same transaction after a valid queued or
  dispatched session accepts its opaque result envelope.

Rejected credentials, unknown sessions, and rejected result transitions do not
advance activity. Both values are monotonic.

Dashboard reads activity through one internal-bearer endpoint:

```http
POST /internal/support/v1/nodes/activity
Content-Type: application/json

{
  "tenantId": "<tenant-id>",
  "nodeIds": ["00000000-0000-0000-0000-000000000000"]
}
```

The request contains one tenant and between 1 and 256 distinct node IDs. The
response contains only matching tenant-owned node IDs with nullable
`lastPollAt` and `lastResultAt` values. It never contains transport credentials,
envelopes, payloads, session identifiers, diagnostic metadata, or node-private
details.

When Dashboard lists support identities, it first reads its durable identity
inventory and then performs at most one bounded relay activity request. Valid
activity is persisted with monotonic updates and merged into the response.
Unconfigured, unreachable, non-successful, oversized, malformed, duplicate, or
out-of-request relay evidence is logged and ignored. Previously stored
timestamps remain visible; Dashboard never writes null or an older timestamp
over known activity.

The activity projection is evidence of support relay transport only. It does not
assert connector connectivity, runner health, host availability, or diagnostic
success.

# Alternatives considered

## Request activity once per identity

This is simple but makes page latency and relay load grow linearly and creates a
partial-failure surface. Rejected.

## Let the agent write activity directly to Dashboard

This duplicates transport state across two node calls and lets Dashboard report
activity that the relay may not have accepted. Rejected.

## Project relay sessions or envelope metadata

Session data could provide richer troubleshooting, but it widens the relay
integration boundary beyond the minimum evidence required and increases
disclosure after relay compromise. Rejected.

## Do not persist projected activity

This avoids a write during identity reads, but every relay outage would erase
known evidence from the operator view. Rejected.

## Introduce an event stream

Push delivery can reduce read latency but adds delivery, replay, ordering, and
operational infrastructure for two monotonic timestamps. Deferred unless future
scale measurements show the bounded read contract is insufficient.

# Consequences

Support identity reads now perform an optional internal relay request and may
advance Dashboard-owned activity timestamps. The response remains available
from stored identities when that projection fails.

Evidence is eventually consistent: the relay commits activity first and
Dashboard persists it on the next identity read. A tenant with more than 256
identities retains stored values and emits a bounded warning rather than
splitting into unbounded requests.

Relay schema initialization must add the two nullable columns to existing
databases without rebuilding opaque session data. Canary profiles must require a
non-null poll timestamp after enrollment and a non-null result timestamp after
diagnostic completion.

# Confirmation

- Relay store tests prove accepted-only, tenant-scoped, monotonic poll and result
  activity.
- Relay API tests prove internal-bearer authorization, batch bounds, duplicate
  rejection, and response minimization.
- Dashboard SQLite tests prove tenant-scoped monotonic persistence.
- Dashboard API tests prove one batch projection and preservation across relay
  failure.
- Portable and Windows-installed support canaries require projected poll and
  result activity before succeeding.

# References

- [ADR-0008](adr-0008-support-plane-v1-read-only-diagnostics.md) remains
  authoritative for the opaque relay and read-only support boundary.
- [Support plane v1](../support-plane.md) is the maintained protocol and
  operations contract.
