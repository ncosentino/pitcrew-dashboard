---
version: 1
slug: "api-clientapp-src-features-support-supportpage-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/SupportPage.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/manifest.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/ui/TaskNavigation.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/ui/OperationalList.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/ui/ReadinessSummary.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/core/ui/DetailPanel.tsx"]
---

## Scope and mode

Tenant-scoped support diagnostics workspace covering readiness, read-only diagnostic
requests, session investigation, support-node enrollment, and identity revocation.
Visitor mode: Operate.

## Audience and job

An administrator arrives during routine setup or an active runner incident. They need
to know whether support diagnostics are available, request the correct bounded
evidence, follow its lifecycle, and inspect one result without confusing support
identity with normal connector or runner health.

## Direction

Use a three-region workbench inside the established Pit Wall system: readiness leads,
a stable task rail separates Overview, Run diagnostic, Sessions, and Support nodes,
and one focused work region carries the selected task. Repeated identities and
sessions use full-width operational rows; one selected session opens into a bounded
detail panel. Rare enrollment, revoked history, structured reports, attestation, and
identifiers remain progressively disclosed.

Concept provenance: surface seed `6970b9b7`; the assigned seventh grounded structure
was the three-region workbench implemented here. The established Pit Wall visual
world remains authoritative, so this route does not introduce a replacement-world
contract or external visual dependency.

## States and constraints

Cover initial loading, unavailable API state, no active nodes, request progress,
queued, dispatched, completed, rejected, cancelled, expired, automatic-refresh
failure, copy-once enrollment, revocation confirmation, long evidence, and empty
history. Preserve exact wire values, tenant authorization, antiforgery, outbound-only
support transport, and read-only diagnostics. Never render missing evidence as zero
or expose private operational data in fixtures or documentation.

## Memorable moment

The first viewport answers whether support is ready, what is active, and what needs
attention before the operator chooses a task.
