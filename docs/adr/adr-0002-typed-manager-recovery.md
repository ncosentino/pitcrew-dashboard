---
title: "ADR-0002: Typed manager recovery as a second concrete operation"
status: "Accepted"
date: "2026-07-26"
authors: ["Nick Cosentino"]
tags: ["architecture", "operations", "security"]
supersedes: ""
superseded_by: ""
---

# Context and scope

ADR-0001 added exactly one outbound write operation, `SetCapacityCommand`, and
rejected a generic remote command bus. A stalled or wedged PitCrew manager is
the second operational problem that cannot be resolved from the dashboard: an
operator must reach the host directly to restart the manager for a profile whose
observed state has stopped converging on its desired generation.

This decision governs the dashboard-side wire contract, authorization, command
lifecycle, and SQLite persistence for a manager-recovery operation. It does not
govern connector-side execution, which remains subject to the same local-policy
boundary ADR-0001 established, and it does not add dashboard UI.

Capacity recovery differs from capacity control in one architecturally
significant way. Setting an absolute capacity maximum is idempotent, so ADR-0001
could treat redelivery of an undelivered command as safe. Restarting a manager
is not idempotent: a second execution after a first execution already started is
an additional disruptive action, not a convergent retry. Any reuse of the
capacity lifecycle must not silently import the assumption that redelivery is
always safe.

Verified facts: the connector protocol carried version 3 with a single typed
capacity command before this decision, and SQLite enforced at most one active
capacity command per node and profile. Assumption: manager restart is
sufficient to clear the observed stall classes operators report; this decision
does not attempt automatic remediation or diagnosis.

# Decision drivers

- Preserve ADR-0001's rejection of a generic command bus and its
  outbound-only, credential-free connector boundary.
- Guarantee at-most-once execution for a non-idempotent action.
- Prevent capacity and recovery from racing on the same profile.
- Keep local connector policy authoritative; the dashboard must never widen it.
- Keep existing v1-v3 connectors compatible and unable to receive recovery work.
- Produce an immutable audit trail of requester and outcome.

# Decision

Connector protocol version 4 adds one concrete `RecoverManagerCommand`, one
`RecoveryOperatorCapability` projection, one `RecoveryCommandProgress` report,
and one `RecoveryCommandOutcome`. Every new field is a trailing optional member
of the existing sync request and response, so a v1-v3 connector's payload
remains valid and its responses never contain a recovery command. The dashboard
rejects a synchronization that carries recovery fields while declaring a
protocol version below 4.

No `operation`, `command`, `executable`, `arguments`, or script field exists on
the wire. A queued command carries only a command identifier, node and profile
identity, the expected manager instance, expected desired generation, expected
desired-state hash, request and expiry timestamps, and the requesting actor for
audit. Recovery therefore remains a second concrete operation rather than the
first instance of a general dispatch mechanism.

The capability advertises only non-secret constraints the dashboard needs:
profile identity, manager contract version and support status, expected fences,
observed-state age, local allowlist status, whether exactly one running manager
is locally resolvable, whether another operation is already active locally, and
the local timeout and expiry bounds. The dashboard clamps a queued command's
expiry to the connector's advertised maximum, so local policy can only narrow
the operation.

The lifecycle models queued, claimed, started, succeeded, rejected, failed,
expired, and indeterminate with timestamps. A command is offered only while it
is queued; once a connector durably claims or reports `started`, that command
identifier is never offered again. A lost response after execution resolves
through connector evidence to succeeded, failed, or indeterminate, never through
automatic re-execution. An expiry that arrives after `started` resolves to
indeterminate rather than expired. Terminal outcomes are immutable.

Mutual exclusion is a shared database invariant rather than a per-feature check.
A single `profile_active_operations` row per node and profile is acquired
transactionally by both the capacity and recovery stores and released only when
no active command of either kind remains. Database triggers require the slot to
exist for any active command of either kind, so the invariant cannot be bypassed
by a future caller that forgets the check.

The API is restricted to tenant administrators, enforces tenant, node, and
profile ownership, requires the connector to have advertised the capability,
requires fresh capability observations and matching fences, requires antiforgery
for browser mutation, rejects overlapping requests, and rate-limits repeated
requests for the same profile. The recorded actor and terminal outcome columns
are immutable through triggers.

# Alternatives considered

## Generalize the capacity command into an operation envelope

Adding an `operation` discriminator with a payload would let a single lifecycle
serve both operations and any future one. It loses against the security driver:
it reintroduces the remote-code-execution-shaped interface ADR-0001 rejected,
and it makes each new operation's authorization implicit in payload validation
rather than explicit in the wire type. Rejected; ADR-0001's rejection stands.

## Reuse the capacity command table with a kind column

This would avoid new tables and reuse the delivery loop. It loses against the
at-most-once driver because the capacity table's redelivery semantics assume a
safe repeat, and encoding two contradictory redelivery rules in one table makes
the unsafe default easy to inherit. Recovery uses its own table with its own
constraints, and only the genuinely shared exclusion slot is factored out.

## Enforce mutual exclusion by querying both command tables

Each store could check the other store's active rows before inserting. This
needs no new table but distributes the invariant across callers, leaves it
unenforced at the schema level, and grows quadratically if a third operation is
ever added. It lost to a single slot table backed by triggers.

## Allow redelivery until an outcome arrives

This maximizes the chance of eventual delivery when a connector crashes between
claiming and reporting. It is rejected because it can restart a manager twice,
which is exactly the harm the operation is meant to relieve. Unresolved
attempts surface as indeterminate and require an explicit new request.

## Negotiate the capability without a protocol bump

Advertising the capability under protocol 3 would avoid a version change.
It was rejected because the dashboard could then send a command shape a v3
connector was never specified to understand, and because the version is the only
unambiguous, testable gate for "must never receive recovery work".

# Consequences

The dashboard gains an auditable manager-recovery capability with strictly
stronger delivery guarantees than capacity control, and mutual exclusion is now
a schema invariant shared by both operations rather than duplicated logic.

Recovery can fail closed in ways capacity does not. A connector that dies
between starting execution and reporting leaves an indeterminate command that a
human must interpret; the system will not retry on its own. Operators may see
`indeterminate` outcomes that require checking the node.

A profile can now run only one operation at a time across both features, so a
queued recovery blocks a capacity change and the reverse. This is intentional
but reduces concurrency and can surface as `409 Conflict` where capacity
requests previously succeeded.

Protocol 4 must be maintained alongside v1-v3 request handling, and connectors
that report version 4 without advertising the capability still cannot receive
recovery commands, which means version alone is not sufficient authorization.

The migration adds tables, columns, and triggers to an existing SQLite database
and backfills the exclusion slot from active capacity commands, so a downgrade
to a pre-migration binary is not supported while active commands exist.

# Confirmation

SQLite tests confirm the offer, claim, start, and success path, that a started
command is never offered again, that terminal rows and audit actor columns are
immutable, that capacity and recovery cannot both hold a profile, and that
migration backfill preserves existing active capacity commands. Feature tests
confirm that a connector below protocol 4 is rejected when it sends recovery
fields and never receives a recovery command. API tests confirm tenant
administrator authorization, antiforgery, stale-fence rejection, overlapping
request rejection, and the end-to-end queue-deliver-start-succeed sequence.

Connector-side execution evidence is out of scope for this repository and cannot
be verified here.

# References

- ADR-0001 records the outbound capacity operation and the rejection of a
  generic remote command bus that this decision preserves and extends with a
  second concrete operation.
- `AGENTS.md` documents the outbound-only, credential-free connector boundary
  and the prohibition on arbitrary node commands that constrain this decision.
