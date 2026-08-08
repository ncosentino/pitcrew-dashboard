---
# AUTO-GENERATED from .github/instructions/dashboard-boundaries.instructions.md — do not edit
paths:
  - "src/PitCrew.Protocol/**/*.cs"
  - "src/PitCrew.Connector.Features.Sync/**/*.cs"
  - "src/PitCrew.Dashboard.Features.{Access,Fleet}/**/*.cs"
  - "src/PitCrew.Dashboard.Features.{Access,Fleet}.Abstractions/**/*.cs"
  - "src/PitCrew.Dashboard.Kernel.*/**/*.cs"
---
# Dashboard protocol and trust boundaries

- Keep connector network flow outbound-only. Container connectors remain read-only
  and never receive the Docker socket.
- Derive node and tenant identity from authenticated connector credentials, never
  synchronization payload fields.
- Add host operations as explicit typed capabilities with local allowlists, fences,
  timeouts, and immutable audit. Do not add free-form commands, paths, executables,
  arguments, or remote shell semantics.
- Preserve protocol compatibility through trailing optional fields and explicit
  version gates. Older connectors report unsupported evidence as unavailable.
- Keep capacity convergence idempotent and manager recovery at-most-once. Do not share
  redelivery assumptions between operations with different execution semantics.
- Never serialize or log runner credentials, connector identity, JIT material,
  environment values, job logs, step output, or raw external payloads.
- Preserve `null` as unavailable and `0` as measured zero. Keep local Docker counts,
  GitHub control-plane counts, and desired targets as separate evidence.
- New operation types, credential boundaries, or remote-control capabilities require
  an accepted ADR before implementation.

See [Architecture decision records](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/adr/README.md).
