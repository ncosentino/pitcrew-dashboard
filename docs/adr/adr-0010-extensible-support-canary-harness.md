---
title: "ADR-0010: Layered cross-repository support canary harness"
status: "Accepted"
date: "2026-08-23"
authors: ["Nick Cosentino"]
tags: ["architecture", "aspire", "canary", "support", "testing"]
supersedes: ""
superseded_by: ""
---

# Context and scope

The Dashboard-owned support plane crosses two repositories and four runtime
processes. Dashboard authorizes and decrypts; the relay stores opaque envelopes;
the network-facing agent enrolls and polls; the broker runs the PitCrew-owned
file-only collector. Unit, API, installer, and package tests cover each boundary,
but they do not prove that one immutable PitCrew revision and one immutable
Dashboard revision work together as a released system.

Manual qualification required separate operators to install a node, create a
session, inspect hosted relay state, and relay observations between machines.
That process found real producer/consumer and lifecycle defects, but it was slow,
non-repeatable, and unable to classify requests rejected before nonce claim.

This decision governs source acquisition, run scaffolding, topology lifecycle,
runtime discovery, scenario registration, evidence, teardown, and GitHub-hosted
execution for cross-repository support canaries. It does not replace production
Compose validation, privileged installer tests, operating-system isolation
tests, or physical-host release qualification.

# Verified facts and assumptions

Verified facts:

- Dashboard owns the support protocol, relay, agent, broker, installer, and
  tenant API, while PitCrew owns the fixed file-only collector and independent
  attestation verifier.
- The repositories already pin exact SDKs and build candidate applications from
  source, so a canary can execute reviewed candidate binaries rather than mocks.
- Aspire can supervise executable resources, allocate loopback endpoints,
  express health dependencies, and expose the same AppHost through
  `Aspire.Hosting.Testing`.
- GitHub-hosted Ubuntu runners can execute public candidate source without
  production credentials or self-hosted capacity.
- Windows GitHub-hosted runners do not provide a supported Linux-container
  engine. Installed Windows and Linux service qualification therefore require
  separate topology profiles rather than one container-only model.
- The current broker evidence policy pins a specific PitCrew commit and
  collector hash. A different PitCrew input is a compatibility failure, not a
  reason for the harness to substitute a fixture collector.
- A bounded spike starts candidate Dashboard and relay executables from one
  AppHost, waits on health, emits a non-secret runtime manifest, lets an external
  runner execute the registered smoke scenario, and stops the exact run through
  its run ID and process fence.

Assumptions to confirm as additional topology profiles land:

- Aspire executable-resource lifecycle remains stable across supported SDK
  updates.
- Privileged installed profiles can retain the same plan, runtime, scenario,
  and result contracts while replacing only topology capabilities and process
  adapters.
- Parallel scenario execution will require independent topology instances until
  a scenario explicitly declares a safe reset contract.

# Decision drivers

- Exercise actual candidate artifacts from two immutable commits.
- Make scenario two through N additive rather than embedding one workflow in
  topology startup or GitHub Actions.
- Keep source resolution, build, scaffold, topology, scenario execution,
  evidence, and teardown independently invocable.
- Use the same topology and scenario implementation locally and in CI.
- Keep all secrets ephemeral and absent from plans, runtime manifests, logs,
  uploaded artifacts, and command arguments.
- Bound every source operation, build, health wait, scenario, process wait, and
  cleanup.
- Preserve exact process and run ownership so cleanup cannot target unrelated
  services, containers, runners, or host state.
- Keep portable evidence honest about the operating-system boundaries it does
  not exercise.

# Decision

Dashboard owns a layered hybrid harness. Aspire owns topology and resource
lifecycle. PowerShell owns source resolution, candidate build, run scaffolding,
local command UX, and GitHub Actions integration. A standalone .NET scenario
library and runner own actions and assertions. `Aspire.Hosting.Testing` is a
second adapter over the same AppHost and scenario code, not the primary owner of
the harness.

## Source and build layers

Every run identifies `ncosentino/pitcrew` and
`ncosentino/pitcrew-dashboard` with full lowercase 40-character commit SHAs.
Source resolution creates detached public checkouts and verifies `HEAD`. Local
checkouts must be clean and match the requested SHA. The build layer compiles
the exact Dashboard AppHost, Dashboard, relay, agent, broker, runner, and
scenario assemblies. It never substitutes published latest tags or mutable
branches.

The selected Dashboard candidate supplies the harness implementation. The
selected PitCrew candidate supplies the collector, import scripts, relay
verifier, and immutable support evidence contract. Dashboard's embedded broker
policy must name the selected PitCrew commit and collector hash; disagreement is
a closed compatibility result.

## Scaffold and manifest layers

Scaffolding creates one run root identified by a random 128-bit lowercase
hexadecimal run ID. It copies only the fixed PitCrew installation sentinels and
remote-diagnostics scripts into a run-scoped file-only fixture and creates the
dedicated support-evidence directory. It does not alter either source checkout.

Three versioned JSON contracts separate lifecycle stages:

1. `plan.json` records run ID, topology profile, selected scenarios, immutable
   repositories and commits, and scaffold time.
2. `runtime.json` records the same source identity, loopback endpoints,
   capabilities, and topology-ready time.
3. `evidence/<scenario>.json` records only stable step names, success/failure,
   bounded categories, durations, and timestamps.

Plans and runtime manifests contain no secrets or developer paths. Scenario
results contain no credentials, request bodies, reports, private host details,
or exception text.

## Topology layer

`PitCrew.Support.Canary.AppHost` is the topology authority. The portable profile
starts built Dashboard and relay executables on dynamic loopback HTTP endpoints,
uses run-scoped SQLite and data-protection storage, waits on `/health` and
`/healthz`, then launches a one-shot manifest writer. The relay-management
bearer is generated for the process lifetime, passed only through process
environment, and omitted from every persisted contract.

The AppHost monitors an exact `stop.request` containing its run ID. The stop
script also fences the AppHost with persisted PID and process start time before
requesting graceful shutdown. A bounded exact-PID fallback is allowed; process
name cleanup is not.

The initial topology capability model is:

- `dashboard-http`
- `relay-http`
- `support-agent-process`
- `support-broker-process`
- `pitcrew-file-only-evidence`

Future `containerized`, `windows-installed`, and `linux-installed` profiles add
or replace capabilities. Scenarios declare required capabilities and cannot
infer stronger evidence from a profile name.

## Scenario layer

`ICanaryScenario` is the extension seam. A scenario has one stable identifier,
a closed required-capability set, and one asynchronous operation that accepts a
validated runtime manifest plus non-serialized local paths. Registration is
explicit and duplicate identifiers fail at startup. Topology startup and
workflow orchestration do not switch on scenario implementation.

The standalone runner validates the plan and runtime schemas, verifies that the
scenario was selected, checks capabilities, applies a total timeout, writes one
redacted result, and exits nonzero on failure. Tests invoke the same scenario
object against an AppHost created by `Aspire.Hosting.Testing`; they do not copy
scenario steps into a test-specific workflow.

The first release scenario,
`support-fresh-enrollment-diagnostic-v1`, performs:

1. exact source and collector-policy validation;
2. fresh tenant-bound enrollment;
3. first accepted relay poll;
4. typed removal of enrollment bootstrap settings;
5. agent-only restart and second accepted poll;
6. diagnostic credential creation;
7. execution of the actual PitCrew relay verifier through agent, relay, broker,
   collector, Dashboard ingestion, and node-signature verification;
8. support identity revocation and typed DeleteKeys cleanup; and
9. before/after hashing proving the run-scoped PitCrew fixture, connector, and
   runner surfaces were not mutated.

The portable profile runs the agent and broker as unprivileged candidate
processes. It proves process, protocol, cryptography, and file-only collection
compatibility. It does not claim service identity, ACL, firewall, systemd,
Windows Service Control Manager, or installed-package evidence.

## CI and trust boundary

Dashboard provides a reusable and manually dispatchable workflow with exact
Dashboard SHA, PitCrew SHA, scenario ID, and topology profile inputs. Pull
requests run the portable profile on public GitHub-hosted Ubuntu with the pull
request SHA and the broker policy's pinned PitCrew SHA.

The workflow uses no production secrets, no `pull_request_target`, and no
self-hosted runners. It uploads only the bounded scenario result. Run state,
ephemeral settings, topology logs, databases, identity material, and secrets
remain runner-local and are deleted with the job.

# Alternatives considered

| Option | Advantages | Drawbacks | Decision |
| --- | --- | --- | --- |
| PowerShell plus existing Compose | Reuses production container definitions and familiar lifecycle commands. | Couples portable testing to Docker, cannot model native process/service profiles well, and tends to merge topology and scenario logic into scripts. | Retained for production deployment tests, rejected as harness authority. |
| Aspire AppHost and Aspire testing as the whole harness | Strong resource model, health waits, dynamic endpoints, and test integration. | Does not own immutable cross-repository checkout, release staging, privileged installation, evidence publication, or a durable external scenario protocol. | Rejected as an all-in-one design. |
| Testcontainers-owned topology | Excellent per-test isolation and container APIs. | Makes tests own topology, privileges Linux containers over native processes, and duplicates production process/installer execution paths. | Rejected as the primary topology model. |
| Hybrid Aspire topology plus independent scripts and scenarios | Separates responsibilities, supports local and CI execution, and keeps future profiles/scenarios additive. | Adds Aspire and multiple small projects, requires explicit manifest contracts, and has more lifecycle surfaces to maintain. | Selected. |
| Custom process/container orchestrator | Complete control over attachment and lifecycle. | Reimplements endpoint allocation, dependency health, supervision, and cleanup without a verified requirement. | Rejected. |

# Consequences

Cross-repository compatibility becomes executable, repeatable evidence rather
than a coordinated operator conversation. One failed step yields a stable
category and duration, and the run can be reproduced from two commits.

The harness adds Aspire SDK and testing dependencies plus versioned public
contracts that must remain backward-compatible or receive new schema versions.
Maintainers must keep the embedded PitCrew evidence policy synchronized
deliberately; arbitrary PitCrew commits are expected to fail closed until
Dashboard supports them.

Portable canaries are cheaper and can run for untrusted public pull requests,
but they cannot replace installed-service and physical-host evidence. New
profiles must state their additional capabilities and preserve the existing
scenario contract rather than forking scenario logic.

Running actual candidates costs more than same-process API tests and may expose
platform nondeterminism. Explicit timeouts, run-scoped state, immutable sources,
and exact teardown keep that cost bounded.

# Confirmation

The decision remains valid when:

- one external command can scaffold, build, start, run, and stop the portable
  scenario from two clean commit-pinned checkouts;
- the AppHost emits a valid runtime manifest only after Dashboard and relay are
  healthy;
- the external runner and `Aspire.Hosting.Testing` adapter execute the same
  `topology-smoke-v1` scenario implementation;
- the MVP scenario completes enrollment through DeleteKeys using candidate
  agent, broker, Dashboard, relay, PitCrew collector, and PitCrew verifier;
- a mismatched PitCrew SHA, collector hash, missing capability, malformed
  manifest, failed health check, rejected request, or teardown fence produces a
  nonzero bounded failure;
- only redacted scenario JSON is uploaded by GitHub Actions; and
- adding a second scenario requires one implementation and one registry entry,
  without changes to AppHost lifecycle or workflow control flow.

# References

- [ADR-0008](adr-0008-support-plane-v1-read-only-diagnostics.md) assigns
  Dashboard and PitCrew their support-plane trust boundaries and explains why
  the canary must execute both repositories.
- `docs/support-plane.md` defines enrollment, relay, broker, attestation, and
  DeleteKeys behavior that the MVP scenario checks end to end.
- `src/PitCrew.Support.Canary.AppHost` demonstrates that Aspire owns candidate
  process topology and dynamic health-gated endpoints.
- `src/PitCrew.Support.Canary.Contracts` defines the non-secret plan, runtime,
  and result boundaries that let external and test adapters attach.
- `src/PitCrew.Support.Canary.Scenarios` demonstrates additive scenario
  registration and actual candidate-process execution.
- `scripts/canary/` demonstrates separate source, scaffold, build, start, run,
  and stop entry points.
- [Epic #165](https://github.com/ncosentino/pitcrew-dashboard/issues/165)
  owns the product outcome; [issue #166](https://github.com/ncosentino/pitcrew-dashboard/issues/166)
  owns this architecture decision.
