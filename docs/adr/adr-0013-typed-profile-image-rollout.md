---
title: "ADR-0013: Typed profile image rollout through outbound connectors"
status: "Accepted"
date: "2026-08-29"
authors: ["Nick Cosentino"]
tags: ["architecture", "connectors", "images", "operations", "security"]
supersedes: ""
superseded_by: ""
---

# Context and scope

ADR-0009 gives Dashboard trusted, immutable runner-image candidates without
granting host authority. Applying one ready candidate to an existing PitCrew
profile still requires a host operator to reproduce the profile's complete setup
configuration and monitor worker convergence manually.

ADR-0001 and ADR-0002 permit only explicit connector operations. They keep
connector traffic outbound-only, make local policy authoritative, reject
server-supplied commands and paths, and enforce one active operation per
node/profile. Image rollout is a third operation with different authority and
recovery semantics: changing the configured worker image is intentional, but
changing routing, capacity, admission, volumes, labels, resource policy, or
credentials is not.

This decision governs one approved candidate applied to one existing profile. It
includes the connector protocol, local host policy, setup reconstruction,
at-most-once execution, Dashboard persistence, authorization, and bounded
operator evidence. It does not authorize fleet campaigns, label-based targeting,
automatic widening, automatic rollback, arbitrary registries, or generic host
commands.

# Verified facts and assumptions

Verified facts:

- Connector protocol version 10 uses trailing optional request and response
  fields, allowing an additive version without changing protocols 1 through 10.
- Container connectors are read-only and socketless. Only the explicitly
  installed host connector can run PitCrew setup operations.
- PitCrew's static profile state contains the resolved configuration, a complete
  static fingerprint, the worker revision, and the source manifest document when
  a manifest owns the profile.
- PitCrew's desired-capacity state and manager observation expose the current
  generation, desired-state hash, scope, targets, and worker convergence.
- `Setup-Runner.ps1` accepts a locally resolved external profile manifest and an
  immutable image reference. It pulls and verifies the image before manager
  handoff, stages profile state atomically, preserves compatible busy workers,
  and attempts rollback if replacement-manager activation fails.
- Replaying the same routing and capacity signature does not advance the desired
  generation when the current acknowledgement is valid.
- A ready candidate from ADR-0009 has a closed platform, recipe identifier, and
  immutable digest. Its reported registry reference is evidence, not host
  authority.

Assumptions to confirm during implementation:

- The locally installed PitCrew profile schema remains compatible with the
  bounded manifest reconstruction supported by the connector.
- The operator-configured registry repository is reachable using host-managed
  Docker authentication when private access is required.
- A candidate digest identifies an image for exactly the candidate platform
  advertised by the connector.

# Decision drivers

- Preserve outbound-only synchronization and the socketless container boundary.
- Keep registry, filesystem, executable, and credential authority local to the
  host.
- Apply only one immutable candidate to one existing profile.
- Prove that routing, capacity, and non-image worker configuration did not
  change.
- Prevent overlap with capacity and manager-recovery operations.
- Never automatically repeat an execution that may already have started.
- Keep protocols 1 through 10 compatible and read-only for image rollout.
- Retain immutable requester, candidate, fence, progress, outcome, and
  convergence evidence in the existing SQLite database.

# Decision

Connector protocol version 11 adds one concrete profile-image rollout
capability, command, progress report, and terminal outcome. The new members are
trailing optional fields on the existing synchronization messages. A connector
declaring a lower protocol version cannot send rollout evidence and never
receives an image command.

No generic operation envelope is introduced. No wire field contains a command,
executable, argument list, local path, registry credential, Dockerfile, build
context, tag, or arbitrary URL.

## Candidate and command authority

Dashboard queues a rollout only for a tenant-owned ready candidate whose output
mode is `registry`. The command carries:

- command, candidate, recipe, node, and profile identity;
- the candidate's immutable digest and closed platform;
- expected current image and worker-revision evidence;
- expected static, preserved-configuration, and routing fingerprints;
- expected desired generation and desired-state hash;
- request and expiry timestamps.

The candidate's reported image reference never crosses into local execution
authority. The connector derives the complete digest-qualified reference from
its own recipe policy.

## Local opt-in and registry policy

Image rollout is disabled by default. The host connector must explicitly:

- enable image rollout;
- allowlist profile identifiers;
- map each allowed recipe identifier to one fixed registry repository;
- configure a protected rollout-state directory and bounded timeout/expiry.

The connector validates repository policy as a repository name only. It rejects
schemes, credentials, tags, digests, whitespace, and control characters. During
execution it constructs exactly:

```text
<locally configured repository>@<candidate digest>
```

Container connectors do not advertise this capability. A disabled,
unallowlisted, unsupported, stale, or policy-incompatible host fails closed.

## Capability and fences

For every observed profile, the host capability reports only bounded,
non-secret evidence needed for compatibility and confirmation:

- profile identifier and `linux/amd64` or `linux/arm64` architecture;
- current image reference, immutable digest when present, local image identity,
  and worker revision;
- exact static-profile fingerprint;
- a connector-computed fingerprint of all non-image worker configuration;
- a connector-computed routing/capacity fingerprint;
- desired generation and desired-state hash;
- locally allowed recipe identifiers;
- local support/allowlist state, active-operation state, and bounded
  timeout/expiry;
- applying/current/stale worker convergence and bounded last error.

Dashboard stores the latest capability observation and queues only when it is
fresh and every supplied fence still matches. The connector re-reads local state
after acquiring its local operation gate and verifies the same fences before any
setup process starts.

## Local setup reconstruction

The connector does not trust the current repository manifest to reproduce the
installed profile because that manifest may have changed since the profile was
last applied. It reconstructs a command-specific manifest from PitCrew's stored
static profile state:

- profile identity, labels, default-label policy, runner group and prefix;
- verification commands;
- autoscaling and maximum-active-worker policy;
- worker resources and runtime devices;
- read-only volumes and service network;
- host-admission identity;
- all other supported non-image manifest fields.

The connector removes local image-build configuration, replaces only the image
with the locally derived digest-qualified reference, and enables image pulling.
It writes the bounded manifest beneath the protected rollout-state directory
using a connector-generated command path. The path is never received from
Dashboard.

The connector reconstructs current scope, repository targets, organization or
enterprise identity, desired capacity, and pause state from local
desired-capacity state. It invokes the locally configured PowerShell executable
and `Setup-Runner.ps1` with the local manifest and those local routing values,
omitting the runner credential so PitCrew reuses its protected registration
credential.

Protocol v11 supports a single-target repo shape only. PowerShell `-File`
parameter binding rejects a repeated `-AddRepos` switch, binds only the first
value when adjacent values are supplied to a string-array parameter, and
treats a comma-joined token as a single string, so there is no safe way to
project more than one repository target through the Setup-Runner CLI in this
release. Zero-count is retained as the fully-paused shape (`-Pause`);
positive-count emits one `-AddRepos url=count` pair. Multi-target repo
routing is reported as the closed `unsupported-topology` category and never
executed. This mirrors the existing capacity protocol's single-target
invariant. Later protocol revisions may add a multi-target repo shape.

Profiles whose stored schema or manifest cannot be reconstructed exactly are
reported unsupported. The connector does not guess, silently drop fields, or
fall back to a success-shaped partial invocation.

Successful command manifests are retained while referenced by current static
profile state. Failed, unreferenced, and excess historical manifests are cleaned
up under a bounded retention policy. Capacity operations recognize a current
connector-generated manifest so a later capacity-only change does not restore
the repository's older image-build definition.

## Lifecycle and recovery

Image rollout follows at-most-once semantics after local `started`, matching
manager recovery rather than capacity redelivery:

```text
queued -> claimed -> started -> succeeded
                            -> rejected
                            -> failed
                            -> indeterminate
```

The connector records the command and all fences durably before starting
`Setup-Runner.ps1`. A repeated command identifier returns the recorded progress
or outcome. A connector restart resolves a previously started command from local
static, desired, and observed state:

- exact target and preserved fences proven: `succeeded`;
- unchanged pre-operation state proven after a pre-handoff failure: `failed`;
- any other unresolved state: `indeterminate`.

A started command is never executed automatically again. Expiry after `started`,
a lost outcome, or incomplete postconditions becomes `indeterminate` and
requires a new explicit request. Dashboard never presents it as failed or safe
to retry.

The successful outcome includes the target digest and revision, rollout status,
current-worker count, stale-worker count, and bounded last error. Later
capability observations continue to show rolling convergence after the command
has terminalized.

## Shared exclusion and persistence

The existing `profile_active_operations` row remains the database authority for
one active operation per node/profile. Image rollout acquires the same slot used
by capacity and recovery. Schema triggers require the slot for active rollout
rows and release it only after the owning operation is terminal.

SQLite stores capability observations and immutable rollout commands behind a
shared kernel rollout contract that both the fleet synchronization path and the
image feature consume without depending on each other. The image feature
validates candidate authority and then queues through that contract; the fleet
synchronization path can deliver commands without depending on the image
feature implementation.

The queue mutation is tenant-administrator-only, antiforgery-protected,
rate-limited, and idempotent. It returns `202 Accepted` with the durable command
identifier and status location. Viewer reads remain tenant-scoped and bounded.
Audit identity, candidate identity, requested fences, and terminal outcomes are
immutable.

# Alternatives considered

## Add a generic connector operation envelope

A discriminated operation payload would reuse delivery and persistence code and
make later commands cheaper. It also recreates the generic remote-command shape
rejected by ADR-0001 and makes local authority depend on increasingly complex
payload validation. The third operation remains a concrete protocol type.

## Send the candidate's complete registry reference

This would avoid local recipe mapping and make configuration simpler. It would
let a compromised Dashboard select an arbitrary registry and repository, even
if the digest itself were immutable. Local policy must own the repository, so
the wire carries only recipe identity and digest.

## Add a new upstream `Setup-Runner.ps1 -ImageOnly` mode

A first-class upstream mode could own manifest reconstruction entirely inside
PitCrew and reduce connector code. It would also create a new minimum PitCrew
version and a cross-repository release dependency before Dashboard could expose
the operation.

The current setup contract already accepts a local external manifest and owns
the transactional image preparation, handoff, acknowledgement, and rollback.
Using that existing contract keeps the operation deployable without an upstream
protocol change. The connector accepts the cost of a narrow, schema-gated
manifest projection and must reject future schemas it cannot preserve. If that
projection becomes materially broader or begins to drift, an upstream
image-only mode should supersede this part of the decision.

## Replay the current repository profile directly

Passing only `-Profile`, `-Image`, and `-PullImage` is compact. It is unsafe for
profiles whose repository manifest changed or still contains local build
configuration: the replay could change non-image policy or rebuild instead of
pulling the approved candidate. Stored applied state, not mutable repository
defaults, is the reconstruction authority.

## Allow automatic retry or rollback

Automatic retry improves apparent availability, and automatic rollback can
reduce time spent on a bad image. Either action can repeat or reverse a rollout
after workers have accepted work. Lost started outcomes remain indeterminate,
and rollback remains a separately approved future campaign.

# Consequences

Dashboard gains one bounded host write capability without Docker access,
inbound connectivity, or arbitrary registry authority. Existing connectors
remain compatible and read-only. Operators can confirm exact candidate and
profile fences, observe immutable command history, and distinguish applying,
rolling, failed, rejected, and indeterminate state.

The host connector's privilege and local maintenance burden increase. It must
retain a protected ledger and bounded generated manifests, understand supported
PitCrew profile schemas, and keep recipe-to-repository policy current. A profile
schema change can make rollout unavailable until the connector is updated; this
is an intentional fail-closed compatibility boundary.

Applying a new image may leave compatible busy workers on the prior revision
until they finish naturally. Command success therefore means PitCrew accepted
the target and reported bounded convergence, not that every currently running
worker already uses it.

The operation adds another SQLite table family and protocol version, but no new
database, broker, cache, connector port, container, credential, or remote shell.

# Confirmation

Architecture compliance is confirmed by tests that prove:

- protocols 1 through 10 deserialize with null rollout fields and never receive
  image commands;
- protocol 11 rejects malformed digest, platform, recipe, fence, progress, and
  outcome evidence;
- disabled and container connectors advertise no rollout capability;
- profile and recipe allowlists, registry repository policy, architecture,
  expiry, topology, freshness, and shared-operation conflicts fail distinctly;
- the command contains no path, executable, argument list, registry repository,
  credential, or arbitrary URL;
- manifest reconstruction changes only image/build authority and preserves every
  supported non-image field from stored local state;
- pre-execution rejection never starts a process;
- started ledger entries are never executed twice and restart recovery produces
  succeeded, failed, or indeterminate evidence without guessing;
- capacity, recovery, and image rollout cannot overlap in memory or SQLite;
- candidate authority, tenant authorization, antiforgery, rate limiting,
  idempotency, immutable audit identity, and bounded history are enforced;
- profile rollout UI preserves unavailable and indeterminate state and requires
  exact identity, fences, effects, prohibited effects, and acknowledgement.

One disposable host integration proves that a registry candidate is pulled,
verified, applied through PitCrew, reported with its target revision, and
converges without cancelling a compatible busy worker.

# References

- ADR-0001 demonstrates why connector writes remain explicit typed operations
  with local allowlists rather than a command bus.
- ADR-0002 establishes at-most-once started semantics and the shared
  `profile_active_operations` invariant reused here.
- ADR-0009 establishes immutable candidate authority and keeps candidate
  production separate from host rollout.
- `docs/ui/runner-image-workspaces.md` defines the operator confirmation,
  unavailable-state, convergence, and Browser UX contract for this capability.
- PitCrew's `Setup-Runner.ps1` and `RunnerProfiles.Functions.ps1` demonstrate the
  local manifest, desired-state signature, image verification, rolling handoff,
  acknowledgement, and rollback behavior this operation delegates to.
