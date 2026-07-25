---
title: "ADR-0001: Outbound connector capacity operations"
status: "Accepted"
date: "2026-07-24"
authors: ["Nick Cosentino"]
tags: ["architecture", "operations", "security"]
supersedes: ""
superseded_by: ""
---

# Context and scope

PitCrew Dashboard currently accepts credential-free manager observations through
an outbound-only connector. The connector protocol can rotate its own node
credential, but it cannot request a runner-capacity change. Operators must leave
the dashboard and reproduce the correct local `Setup-Runner.ps1` invocation.

The existing architecture deliberately excludes remote capacity control and
arbitrary node commands. This decision narrows that exclusion for one operation:
setting the absolute maximum of an existing, unambiguous profile capacity
target. Profile creation, deletion, routing changes, autoscaling policy changes,
release updates, and arbitrary command execution remain out of scope.

The operation is useful only when it preserves these existing boundaries:

- connectors initiate every network connection;
- the dashboard never receives a Docker socket or GitHub runner credential;
- the dashboard never supplies local paths or shell arguments;
- PitCrew's setup script remains responsible for locking, generation updates,
  validation, and manager acknowledgement;
- existing protocol-v1 and protocol-v2 connectors remain read-only.

# Decision drivers

- Deliver a useful end-to-end write capability without introducing a generic
  remote command system.
- Keep local policy authoritative if the dashboard is compromised or
  misconfigured.
- Reuse PitCrew's idempotent capacity reconciliation instead of duplicating it
  in the connector.
- Require no inbound host port and no additional container.
- Make retries, connector restarts, and lost responses safe.
- Preserve an auditable record of who requested an operation and its outcome.

# Decision

Protocol v3 adds one concrete `SetCapacityCommand`, one connector capability
projection, and one command outcome. There is no generic operation envelope or
server-provided argument list.

The connector advertises capacity capability only when operator mode is enabled
locally. Operator mode is supported for the connector binary running as a host
service. The existing container deployment remains read-only.

The connector derives supported profiles from its configured PitCrew state root
and a local allowlist. The initial contract supports repository profiles with
exactly one repository target and organization or enterprise profiles with one
shared replica target. The connector reports the current generation, current
maximum, and locally configured ceiling. The dashboard can queue only a value
inside that advertised contract.

Queued commands contain an absolute maximum and expected generation. The
connector re-reads local state, rejects stale or expired commands, reconstructs
the complete setup invocation from local files, and invokes
`Setup-Runner.ps1 -CapacityOnly`. No path, repository URL, token, or free-form
argument crosses the dashboard boundary.

The connector process never serializes or transmits the stored GitHub runner
credential. Operator mode nevertheless increases the host service account's
blast radius because the child setup process reuses the locally stored profile
credential. Operator mode is therefore disabled by default and requires an
explicit profile allowlist and capacity ceiling.

SQLite stores one active command per node and profile. Delivery is claimed
transactionally, may be retried after a bounded redelivery interval, and uses
the command identifier as its idempotency key. Re-executing the same absolute
capacity request converges safely.

# Alternatives considered

## Retain a read-only dashboard

This retains the smallest attack surface but does not solve the operational
problem.

## Add a generic remote command bus

This would make future operations easy to add, but it would also create a
remote-code-execution-shaped interface and contradict the repository's security
boundary. It is rejected.

## Give the connector container the Docker socket

This is operationally simple but expands a read-only reporting container into a
Docker-host administrator and breaks PitCrew's socket-isolation invariant. It
is rejected.

## Add an inbound host API or SSH automation

This adds firewall, authentication, certificate, and reachability concerns that
the outbound connector architecture intentionally avoids. It is rejected.

## Add a separate privileged sidecar or container

This could preserve the read-only connector, but it adds another deployed
component and violates the no-new-container delivery constraint. It is rejected
for this operation.

# Consequences

The dashboard gains a useful capacity control and a reusable, typed
command-delivery lifecycle. Older connectors remain compatible and read-only.
The local connector policy remains the final authorization boundary.

Write-enabled connector hosts must run the connector binary as a host service
with permission to execute the PitCrew setup script. This is a material increase
in local privilege compared with the read-only connector. Capacity commands can
take as long as the setup acknowledgement timeout, and the next connector sync
may be delayed while one executes.

Linux installations use systemd and the invoking sudo user. Windows
installations use a native .NET Windows Service running as `LocalSystem`.
Windows requires Docker-engine access, which is already host-equivalent
privilege; the local allowlist, ceiling, and typed command reconstruction remain
the security boundary.

Multi-repository profile control is intentionally unavailable until the protocol
exposes distinct local targets without accepting server-supplied repository
identities.

# Confirmation

Protocol tests confirm v1/v2 compatibility and v3 command serialization.
Feature and SQLite tests confirm tenant authorization, capability validation,
single-active-command enforcement, delivery, redelivery, and outcome
idempotency. Connector tests confirm disabled-mode rejection, local allowlist
and ceiling enforcement, stale-generation rejection, process timeout, and
argument construction without server-supplied paths.

The operational runbook requires one manual confirmation against a disposable
PitCrew profile before operator mode is enabled on a production host.

# References

- `AGENTS.md` documents the existing outbound-only, credential-free connector
  boundary that this decision preserves except for the explicit opt-in host
  execution consequence.
- PitCrew `Setup-Runner.ps1 -CapacityOnly` owns the authoritative capacity
  generation and acknowledgement workflow reused by this decision.
