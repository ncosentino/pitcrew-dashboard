---
title: "ADR-0003: Manager-owned workload attribution with link-only GitHub intervention"
status: "Accepted"
date: "2026-08-06"
authors: ["Nick Cosentino"]
tags: ["architecture", "operations", "security", "observability"]
supersedes: ""
superseded_by: ""
---

# Context and scope

PitCrew managers already receive GitHub scale-set job lifecycle messages to
protect busy ephemeral workers. Before manager contract 15, they discarded the
job identity after updating worker state. Dashboard could show anonymous busy
workers and resource totals but could not identify the repository, workflow
run, or job responsible for host pressure.

The dashboard could obtain richer step state and cancellation through GitHub's
Actions API, but that would require storing or delegating repository-scoped
write credentials. The existing connector boundary is outbound-only and
credential-free, and ADR-0001 and ADR-0002 permit only narrow typed host
operations rather than a general remote-control surface.

This decision governs live and retained workload attribution, pressure
incident presentation, and the boundary between Dashboard and GitHub. It does
not govern profile admission control, job cancellation inside GitHub, or
workflow-log collection.

# Decision drivers

- Identify the active workload without collecting workflow logs or secrets.
- Preserve the credential-free manager and connector synchronization path.
- Keep cancellation authorization in the system that owns the workflow.
- Retain enough bounded evidence to explain an incident after an ephemeral
  worker exits.
- Keep older managers and connectors readable with explicit unavailable state.

# Decision

Connector protocol 8 carries manager contract 15 `currentJob` and contract 16
`hostPressure` fields.

`currentJob` contains only a canonical GitHub repository URL, workflow-run and
job identifiers, bounded display/event text, lifecycle timestamps, and a
bounded result. The existing hashed runner assignment interval is enriched
with that context because one ephemeral runner owns one job. Dashboard does
not create a second unbounded workload store.

`hostPressure` contains aggregate Docker-host or VM CPU, load, memory, swap,
and optional PSI evidence. Dashboard evaluates node-scoped pressure incidents
from samples deduplicated by connector heartbeat so several profiles cannot
make one moment look like several sustained observations.

Dashboard constructs exact GitHub run/job links from the bounded manager
fields. It does not store a GitHub Actions write token and does not expose a
cancel API. An authenticated operator follows the link and cancels in GitHub,
where repository authorization and audit already exist.

# Alternatives considered

## Poll GitHub Actions from Dashboard

This provides step names, log-derived progress, and direct cancellation. It
requires a new GitHub App or token storage boundary, repository-installation
authorization, rate-limit handling, tenant-to-repository mapping, and a write
audit model. It lost because the immediate operational need is attribution and
safe navigation, not a second workflow control plane.

## Kill the worker container from Dashboard

This would stop the resource consumer without GitHub credentials. It violates
PitCrew's busy-worker preservation invariant, reports an infrastructure loss
instead of an authorized workflow cancellation, and risks cleanup races. It is
rejected.

## Store a separate job-history table

This isolates workload fields but duplicates identity, interval, retention,
and truncation logic already implemented by runner assignments. Enriching the
one-job ephemeral assignment won because its lifecycle and bounds are already
defined.

# Consequences

Operators can identify long-running jobs, open the exact GitHub page, and see
the workload interval over resource history without giving Dashboard new
GitHub privileges.

Fixed runners and recovered autoscaled jobs whose start event was not retained
remain unattributed. Dashboard states that limitation rather than inferring
identity from CPU or memory.

The manager job display name is workload metadata and is retained under the
same tenant and diagnostic-history bounds as the assignment. Logs, step output,
workflow refs, labels, payloads, raw runner identity, and credentials remain
excluded.

Cancellation remains one navigation step away instead of one click inside
Dashboard. Adding direct cancellation later would require a separate ADR and
credential/authorization design.

# Confirmation

Protocol and connector tests require explicit contract-15/16 fields and
protocol 8. SQLite tests prove job lifecycle enrichment and pressure samples
survive worker exit. Alert tests prove one node heartbeat contributes at most
one pressure sample. Frontend tests prove the command center renders exact
GitHub links and no cancellation button.

Live Docker Desktop and WSL deployments remain an operator validation boundary;
the UI labels those measurements as Docker-host or VM pressure rather than
physical-host truth.

# References

- ADR-0001 defines the outbound-only typed capacity operation and rejects a
  generic command bus.
- ADR-0002 defines the second typed host operation and preserves the same
  credential boundary.
- `docs/runner-correlation.md` defines retained assignment identity and
  truncation semantics.
