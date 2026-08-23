# Architecture decision records

Accepted records retain their original reasoning. Material changes use a new record
and explicit supersession links.

| Record | Status | Decision |
| --- | --- | --- |
| [ADR-0001](adr-0001-outbound-capacity-operations.md) | Accepted | Permit one outbound typed capacity operation while rejecting a generic command bus. |
| [ADR-0002](adr-0002-typed-manager-recovery.md) | Accepted | Add manager recovery as a separate at-most-once typed operation. |
| [ADR-0003](adr-0003-manager-owned-workload-attribution.md) | Accepted | Keep workload attribution manager-owned and intervention link-only. |
| [ADR-0004](adr-0004-audited-zero-capacity-pause.md) | Accepted | Permit generation-fenced zero-capacity admission pause without preemption. |
| [ADR-0005](adr-0005-retrospective-connector-health-replay.md) | Accepted | Replay bounded connector health evidence after outbound synchronization recovers. |
| [ADR-0006](adr-0006-docs-first-agent-guidance.md) | Accepted | Use docs-first guidance with a Genesis-managed base, project-owned specialization, Impeccable design support, and generated Claude mirrors. |
| [ADR-0007](adr-0007-browser-ux-evidence-and-design-authority.md) | Accepted | Use durable product/design authority and deterministic browser UX evidence while keeping subjective design findings advisory. |
| [ADR-0008](adr-0008-support-plane-v1-read-only-diagnostics.md) | Accepted | Add a separate optional read-only support plane with opaque relay, independent node identity, and split transport/broker processes. |
| [ADR-0009](adr-0009-trusted-image-candidate-orchestration.md) | Accepted | Use a GitHub App to dispatch immutable registered image workflows and poll exact runs for bounded candidate artifacts. |
| [ADR-0010](adr-0010-extensible-support-canary-harness.md) | Accepted | Use a layered Aspire topology with independent source, scaffold, scenario, evidence, and CI adapters. |
