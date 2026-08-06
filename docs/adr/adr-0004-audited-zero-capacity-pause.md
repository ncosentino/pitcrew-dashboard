---
title: "ADR-0004: Audited zero-capacity admission pause"
status: "Accepted"
date: "2026-08-06"
authors: ["Nick Cosentino"]
tags: ["architecture", "operations", "security", "resilience"]
supersedes: ""
superseded_by: ""
---

# Context and scope

ADR-0001 permits one outbound, typed operation that changes the absolute
maximum of an existing profile. Its original contract required a positive
maximum. During host pressure, reducing a profile to one still leaves an idle
runner able to accept new work. Killing a busy worker would violate PitCrew's
job-preservation invariant, while cancelling the workflow requires GitHub
authorization that Dashboard deliberately does not hold.

PitCrew manager contract 17 adds an explicit zero-capacity pause. The profile,
targets, manager, scale-set sessions, and retained history remain present.
Busy workers drain normally, while no replacement or new worker is admitted.

This decision governs Dashboard pause and resume. It does not add workflow
cancellation, arbitrary remote commands, profile deletion, or automatic
incident remediation.

# Decision drivers

- Stop new admission without preempting running jobs.
- Reuse the existing tenant authorization, antiforgery, expiry, generation
  fence, local allowlist, local ceiling, idempotency, and operation exclusion.
- Keep older connectors and container-mode connectors read-only.
- Preserve an immutable audit of the pre-pause maximum and any fenced resume.
- Keep GitHub Actions write authorization and cancellation audit in GitHub.

# Decision

Connector protocol 9 extends `SetCapacityCommand` to permit zero only when the
host connector explicitly advertises `supportsZeroMaximum` for the existing
profile. The connector derives that capability from PitCrew manager contract
17 and invokes `Setup-Runner.ps1 -Pause`. No dashboard-supplied path,
repository, token, argument list, or shell content crosses the wire.

SQLite records the previous and requested maximum for every new capacity
command. A pause records the positive pre-pause maximum. Dashboard offers
**Resume to N** only while the acknowledged pause generation still matches the
connector's current generation. The resume command records its relationship
to that pause. If local state changes out of band, the fenced resume disappears
and an administrator must choose an explicit positive maximum.

Pause, resume, ordinary capacity changes, and manager recovery share the
existing per-profile operation slot. Delivery and redelivery remain
idempotent absolute-value convergence. Dashboard declares a profile paused
only after the connector advertises an acknowledged current maximum of zero;
a failed or lost pause response does not manufacture paused state.

Pressure incidents may present a confirmed pause action for authorized,
write-enabled profiles. Read-only connectors retain exact GitHub job links but
show no pause action. Incidents never trigger pause automatically.

Dashboard continues to store no GitHub Actions write credential. Operators
cancel or inspect a running job through the exact GitHub link, where repository
authorization and audit already exist.

# Alternatives considered

## Add a separate pause command

A distinct command could make the UI intent explicit, but it would duplicate
capacity authorization, fencing, redelivery, local resolution, and audit
behavior. Zero is an absolute capacity maximum with stronger capability
fencing, so extending the typed operation is smaller and safer.

## Add a generic remote command envelope

A generic envelope would simplify future operation additions, but it creates a
remote-code-execution-shaped boundary and weakens local argument ownership. It
is rejected for the same reasons documented in ADR-0001 and ADR-0002.

## Kill workers or remove the profile

Killing workers stops resource use quickly but preempts jobs and turns an
authorized workload decision into infrastructure failure. Removing the profile
also destroys routing and desired-state identity rather than pausing
admission. Both are rejected.

## Give Dashboard GitHub cancellation credentials

Direct cancellation could reduce navigation, but it introduces repository
installation mapping, write-token storage, expanded tenant authorization, and
a second cancellation audit. Exact links plus typed admission pause address
the incident workflow without that larger security boundary.

# Consequences

Administrators can stop replacement and new admission from either profile
capacity controls or an active pressure incident while busy workers remain
visible and linked to GitHub.

Host-installed connectors must be upgraded to protocol 9 and report manager
contract 17 before zero is accepted. Protocol 1-8 connectors and container-mode
connectors never advertise zero support and never receive a zero command.

An out-of-band generation change intentionally invalidates one-click resume.
This trades convenience for evidence that the recorded pre-pause value still
describes the current local profile.

# Confirmation

Protocol and feature tests fence zero to protocol 9 and explicit local
advertisement. Connector tests prove `-Pause` reconstruction, local rejection,
and acknowledged zero state. SQLite tests cover migration, immutable audit
fields, redelivery, restart inference, fenced resume, and out-of-band
invalidation. API and frontend tests cover authorization, confirmation,
pausing, paused, resuming, exact resume values, and pressure-incident actions.

# References

- ADR-0001 defines the outbound typed capacity operation and rejects a generic
  command bus.
- ADR-0002 preserves per-profile exclusion for manager recovery.
- ADR-0003 keeps GitHub job cancellation behind exact links and GitHub's own
  authorization boundary.
- PitCrew manager contract 17 defines zero-capacity pause and natural busy
  worker drain.
