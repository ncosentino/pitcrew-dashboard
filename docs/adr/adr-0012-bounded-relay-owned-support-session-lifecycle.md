---
title: "ADR-0012: Bounded relay-owned support session lifecycle projection"
status: "Accepted"
date: "2026-08-24"
authors: ["Nick Cosentino"]
tags: ["architecture", "privacy", "relay", "support", "lifecycle"]
supersedes: ""
superseded_by: ""
---

# Context and scope

Support diagnostic sessions expose six lifecycle states: queued, dispatched,
completed, rejected, cancelled, and expired. Dashboard creates and persists a
queued session. The relay already changes its private copy to dispatched when a
node polls, completed when a result arrives, cancelled on an operator request,
and expired when a poll observes elapsed expiry. Dashboard, however, never
reads relay session state. It fetches only an optional result envelope and
therefore continues to report queued until it verifies a completed result,
cancels the session itself, or projects expiry from its local clock.

The agent records bounded local request-processing dispositions for malformed,
mismatched, unsupported, expired, replayed, and broker-rejected requests. It
does not report those outcomes to the relay. A rejected request therefore
remains dispatched in the relay until expiry and queued in Dashboard until a
later read projects expiry. The explicit lifecycle labels delivered for the UI
cannot make those missing backend transitions true.

This decision governs agent-to-relay rejection reporting, relay session
metadata, Dashboard projection and persistence, public lifecycle evidence,
mixed-version behavior, and the canary evidence required before rollout. It
does not authorize request bodies, encrypted envelopes, reports, nonces,
credentials, exception details, paths, or arbitrary agent reasons as lifecycle
metadata.

# Verified facts and assumptions

Verified facts:

- Relay polling is the authority that knows when one exact opaque session was
  dispatched.
- The agent is the authority that knows whether it produced a result or
  rejected the request before diagnostics.
- Dashboard is the only component that can decrypt and verify a completed
  result; relay `completed` state alone is not proof of a valid diagnostic.
- Agent request-processing dispositions are already closed, bounded, and free
  of request content and identifiers.
- Relay node routes derive identity from the route and hashed transport
  credential rather than a payload tenant or node field.
- Explicit support-session reads already contact the relay for result
  ingestion. Recent-session list reads intentionally avoid per-session relay
  calls.
- The relay and Dashboard SQLite stores already constrain lifecycle status to
  the six public states, but neither persists rejection disposition and
  Dashboard does not retain dispatch time.

Assumptions to confirm during implementation:

- Additive nullable lifecycle fields remain compatible with existing API
  clients.
- Older agents can continue to omit rejection outcomes; their rejected
  sessions remain dispatched and eventually expire rather than being
  misclassified.
- A new agent receiving `404` or conflict from an older relay can retain its
  local bounded disposition and continue polling without disabling identity.

# Decision drivers

- Make every Dashboard lifecycle claim attributable to the component that
  observed the transition.
- Preserve the relay's opaque-envelope boundary.
- Distinguish rejection from expiry without transmitting request content.
- Keep terminal transitions monotonic and idempotent under retries, cancellation
  races, and mixed versions.
- Preserve Dashboard ownership of result verification.
- Avoid N+1 relay calls on session-list reads.
- Retain evidence that dispatch occurred even after a session becomes terminal.
- Keep protocol additions backward compatible and safe to roll independently.

# Decision

The relay owns transport lifecycle metadata. The agent may report one closed
rejection disposition for an exact relay session through a versioned,
transport-authenticated node endpoint. Dashboard projects relay state only
during an explicit single-session read and persists monotonic lifecycle
evidence.

## Closed rejection outcome

The shared support protocol owns the rejection-disposition vocabulary. It
contains only stable request-processing categories already produced by the
agent, including envelope rejection, malformed request, session mismatch,
wrong tenant or node, unsupported capability or diagnostic mode, expiry,
invalid or replayed nonce, replay pending, unsafe broker markdown, invalid
broker report, generic validation rejection, and unavailable result.

The node outcome request contains only that disposition. Node identity comes
from the route and transport credential; session identity comes from the route.
The request cannot carry a tenant, node ID, envelope, report, nonce, path,
exception, command, or free-form reason.

For the same session and disposition, reporting is idempotent. A different
disposition for an already rejected session conflicts. Cancellation, expiry,
and verified completion remain terminal and are never overwritten by a later
rejection report.

## Relay lifecycle evidence

Relay session storage retains:

- current transport status;
- first dispatch timestamp;
- rejection timestamp when applicable; and
- one nullable closed rejection disposition.

Polling atomically moves queued to dispatched and sets the first dispatch
timestamp without changing it on retries. A valid result upload moves queued or
dispatched to completed. A bounded rejection report moves queued or dispatched
to rejected. Cancellation and expiry preserve any prior dispatch timestamp.

The internal Dashboard-to-relay session response exposes those bounded fields
plus the existing tenant, node, session, expiry, and opaque envelope fields.
This remains an authenticated management contract and is not a public relay
endpoint.

## Dashboard projection

An explicit Dashboard session read fetches the relay session state when the
local session is queued or dispatched.

- Relay dispatched state advances Dashboard to dispatched and records the first
  dispatch timestamp.
- Relay rejected state advances Dashboard to rejected and stores the closed
  disposition.
- Relay cancelled or expired state advances Dashboard to the same terminal
  state.
- Relay completed state causes Dashboard to fetch, decrypt, and verify the
  result. Dashboard advances to completed only after that verification
  succeeds.
- Missing, mismatched, malformed, or unavailable relay state leaves the current
  Dashboard state unchanged.

Projection is monotonic. Terminal Dashboard state never regresses, and a
dispatch timestamp may be added to a later terminal state without changing its
status. Dashboard's own clock may expire a queued or dispatched session and
persist that transition when relay state is unavailable.

Recent-session list reads remain storage-only to avoid one relay call per row.
The existing explicit refresh action owns synchronization for one session.

Public session responses add nullable `dispatchedAt` and
`rejectionDisposition` fields. The disposition is present only for rejected
sessions and remains in the closed vocabulary. No relay envelope or internal
timestamp beyond the bounded dispatch evidence becomes public.

## Compatibility and rollout

All additions are nullable or new endpoints.

- New relay and Dashboard versions accept older agents. If an older agent
  rejects a request without reporting, the session remains dispatched and
  eventually expires.
- New agents treat an older relay's missing outcome endpoint or a terminal
  session race as a non-fatal unreported outcome while preserving local status.
- Dashboard continues to accept relay state that omits new nullable fields.
- Existing completed-result verification and release canaries remain unchanged.

Release qualification adds a terminal-lifecycle scenario only after mixed
version, relay, agent, storage, API, and UI tests pass.

# Alternatives considered

| Option | Advantages | Drawbacks | Decision |
| --- | --- | --- | --- |
| Infer dispatch and rejection from node poll timestamps | No protocol or storage changes. | A node poll cannot prove which session was dispatched, and silence cannot distinguish rejection, outage, cancellation, or expiry. | Rejected. |
| Store bounded agent outcome and relay lifecycle metadata | Each transition has an authoritative observer, retries are idempotent, and the relay remains content-opaque. | Adds one authenticated endpoint, nullable metadata, migrations, projection logic, and mixed-version tests. | Selected. |
| Upload an encrypted synthetic result for rejection | Reuses the result path and Dashboard cryptography. | Creates success-shaped output for a request that did not run, may lack a trustworthy parsed request, and conflates attestation with rejection metadata. | Rejected. |
| Send rejection directly from agent to Dashboard | Avoids relay schema changes. | Adds a second agent network destination, weakens outbound topology, duplicates authentication, and bypasses relay session ordering. | Rejected. |
| Query relay once per session on every list read | Makes list results immediately fresher. | Creates an N+1 management path and couples page cost to result count. | Rejected; explicit single-session refresh remains the synchronization boundary. |

# Consequences

Dashboard can truthfully distinguish a request that has not been polled, one
that reached the node, one the node rejected, and one that merely expired.
Operators receive a bounded reason without report or request disclosure.
Completed remains stronger evidence than relay transport state because
Dashboard still verifies the node-signed result.

The relay now stores a small allowlisted metadata surface in addition to opaque
envelopes. That surface must remain closed in protocol tests and migrations.
Explicit refresh is required to project relay state into Dashboard; list reads
may remain stale until that bounded synchronization occurs.

Older components degrade to the previous dispatched-then-expired behavior
instead of failing enrollment or valid diagnostics. Rejection reporting is
best-effort across a mixed-version rollout but authoritative once both agent
and relay support it.

# Confirmation

The decision is confirmed when:

- protocol tests reject unknown or unbounded dispositions;
- relay tests prove authenticated, idempotent, monotonic dispatch, rejection,
  cancellation, completion, and expiry transitions;
- agent tests prove every no-result disposition is reported without disabling a
  valid identity;
- Dashboard storage and API tests prove monotonic projection and nullable public
  evidence;
- mixed-version tests preserve valid diagnostics and bounded degradation; and
- portable and containerized canaries prove queued, dispatched, completed,
  rejected, cancelled, and expired evidence with no secret-bearing artifacts.

# References

- [ADR-0008: Dashboard-owned support plane v1](adr-0008-support-plane-v1-read-only-diagnostics.md)
- [ADR-0010: Layered cross-repository support canary harness](adr-0010-extensible-support-canary-harness.md)
- [Support plane v1](../support-plane.md)
- [Cross-repository support canary](../testing/support-canary.md)
- [Issue #161](https://github.com/ncosentino/pitcrew-dashboard/issues/161)
- [Issue #163](https://github.com/ncosentino/pitcrew-dashboard/issues/163)
- [Issue #190](https://github.com/ncosentino/pitcrew-dashboard/issues/190)
