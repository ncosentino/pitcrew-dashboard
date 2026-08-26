# Support plane v1

Support plane v1 is an optional, diagnostics-only path for investigating a
PitCrew node when the normal Dashboard connector is unavailable or stale. It is
separate from connector capacity and manager-recovery operations.

## Trust boundaries

- Dashboard authorizes requests, signs request envelopes, decrypts results, and
  renders verified output.
- The relay stores opaque envelopes and authenticates node polling with hashed
  transport credentials. It cannot sign requests or decrypt results.
- The native support transport agent owns outbound HTTPS and request/result
  cryptography. It does not read PitCrew state directly.
- The diagnostics broker owns local file-only PitCrew evidence collection. It has
  no outbound network capability and receives only a closed diagnostic mode,
  optional local profile ID, and package ID over peer-authenticated platform IPC.

V1 forbids mutations, shells, generic commands, arbitrary paths, URLs, scripts,
ports, tunnels, Docker access, and server-supplied executables.

## Relay activity projection

The relay is authoritative only for support transport activity. It advances
`last_poll_at` after accepting a node credential, including accepted polls with
no queued session, and advances `last_result_at` only after accepting an opaque
result for a queued or dispatched session. Rejected credentials, unknown
sessions, and rejected result transitions do not change either value.

Dashboard requests those timestamps through one internal-bearer batch:

```http
POST /internal/support/v1/nodes/activity
Content-Type: application/json

{
  "tenantId": "<tenant-id>",
  "nodeIds": ["00000000-0000-0000-0000-000000000000"]
}
```

The request allows 1 through 256 distinct node IDs. The response contains only
matching node IDs plus nullable `lastPollAt` and `lastResultAt`; it contains no
credentials, envelopes, payloads, sessions, or diagnostic details. Dashboard
persists valid timestamps monotonically. If the relay is unconfigured,
unreachable, rejects the request, or returns malformed or out-of-request
evidence, Dashboard preserves its prior values instead of replacing them with
null. A last-poll timestamp therefore means only that support relay
authentication succeeded; it is not connector, runner, profile, or host health.

## Identity and enrollment

A support node identity is independent from the normal connector identity. The
agent generates an ECDSA P-256 signing key and RSA-3072 encryption key locally.
An administrator first creates a one-time tenant-bound enrollment code. The node
submits that code and only the two public SPKIs to Dashboard. Dashboard consumes
the code atomically and returns the support node ID, relay URL, a transport
credential encrypted to the node RSA key, Dashboard authorization public key,
and Dashboard result-encryption public key. The agent verifies and decrypts the
credential envelope locally before persisting the returned state. Private keys
never cross HTTP and Dashboard stores only public SPKIs.

The node persists a random completion ID before its first request. An exact retry
with the same code, completion ID, and public keys returns the same encrypted
credential envelope, so a lost response or interrupted local commit is
recoverable. Reuse with a different completion ID or key pair is rejected as
replay. A revoked completed identity cannot use this recovery path. Dashboard
retains consumed enrollment envelopes for
`EnrollmentRecoveryLifetimeSeconds` (one hour by default), then removes them in
bounded batches. After that window, recovery requires explicit operator action;
the agent never silently re-enrolls.

On Windows, the agent creates user-scoped persisted CNG keys under the account
running the support-agent service. Private export is prohibited and local state
stores only opaque key names, public metadata, and a transport credential
encrypted to the non-exportable RSA key. Identity directories and files use a
protected ACL granting access only to that service identity. Run the service
under its dedicated service identity so the CNG and ACL boundaries use the
intended account.

On Linux, the agent uses a dedicated identity directory with mode `0700` and
PKCS#8 key, transport credential, and manifest files with mode `0600`. Reload
rejects broader permissions or an owner UID other than the effective support
agent UID. These are software keys protected by Unix ownership and discretionary
access control; `root` can still read them. The implementation does not claim
hardware-backed or root-resistant non-exportability.

Revoking support identity stops the next poll/request exchange. It does not
revoke or rotate the connector identity. A rejected relay credential marks the
local identity as requiring explicit operator action; the agent does not silently
reuse enrollment configuration.

Rotation uses a durable prepare/promote protocol rather than compensating
rollback. The node stages new keys and a replacement credential while continuing
to use the old identity. Relay prepare durably accepts both credentials, and
Dashboard records a `prepared` rotation that blocks new diagnostic sessions.
Only after both stores acknowledge prepare does the node atomically commit its
staged identity. The node then finalizes with the replacement credential:
Dashboard atomically activates the replacement public keys and credential hash,
the relay promotes the replacement and retires the old credential, and Dashboard
records `finalized` before sessions resume. Exact retries resume `prepared`,
`dashboard_promoted`, or `finalized` state. An interruption never depends on
best-effort rollback: before local commit the old credential remains usable; after
local commit the replacement is already accepted by the relay. Connector identity
is not read or changed.

The worker and operator rotation command share a cross-process identity-operation
lock. The worker reloads protected identity state before each poll, so a completed
rotation cannot leave it polling with the retired credential.

Local management exposes status, disable, and remove operations. Removal requires
an explicit preserve-keys or delete-keys choice. Preserving keys removes enrollment
and transport state but leaves the local key set unusable by the agent; deleting
keys removes both state and private material. Service stop/start and uninstall
mechanics remain installer responsibilities.

## API contract

Tenant support sessions use:

```http
POST /api/tenants/{tenantId}/support/v1/sessions
Authorization: Bearer <diagnostic-credential-or-browser-session>
Content-Type: application/json

{
  "nodeId": "00000000-0000-0000-0000-000000000000",
  "diagnosticMode": "ConnectorOffline",
  "profileId": "default",
  "expiresInSeconds": 300
}
```

Administrators create a one-time node enrollment authorization with:

```http
POST /api/tenants/{tenantId}/support/v1/enrollment-authorizations
Authorization: ******
Content-Type: application/json

{
  "displayName": "Support node"
}
```

The node completes enrollment without a Dashboard user session:

```http
POST /api/support-agent/v1/enrollments/complete
Content-Type: application/json

{
  "tenantId": "<tenant-id>",
  "enrollmentCode": "<one-time-enrollment-code>",
  "completionId": "00000000-0000-0000-0000-000000000000",
  "nodeSigningPublicKeySpki": "<base64url-public-spki>",
  "nodeEncryptionPublicKeySpki": "<base64url-public-spki>"
}
```

The response is cache-disabled and contains the node ID, display name, encrypted
transport-credential envelope, relay URL, and Dashboard public keys. It contains
no plaintext transport credential, node private material, or enrollment code.
Cross-tenant submission, expiry, mismatched retries, and duplicate node key pairs
fail.

Enrollment and rotation responses are single-sourced in
`PitCrew.Support.Protocol`. The Dashboard API producer and support-agent consumer
compile against those same wire records, while protocol tests pin the exact v1
serialized property names. A wire change must update producer, consumer,
serialization, and mixed-version coverage together; a separately declared
lookalike response type is not compatible evidence.

The original `POST /api/tenants/{tenantId}/support/v1/enrollments` manual-key
contract remains available only when
`PitCrew__SupportPlane__AllowLegacyManualEnrollment=true`. Its original request
and response shapes are preserved for compatibility, but it returns plaintext
bootstrap secrets and is disabled by default. New installations use the
authorization and node-completion flow above.

Rotation prepare uses:

```http
POST /api/support-agent/v1/identities/{nodeId}/rotate
Content-Type: application/json

{
  "rotationId": "00000000-0000-0000-0000-000000000000",
  "tenantId": "<tenant-id>",
  "currentTransportCredential": "<current-credential>",
  "replacementTransportCredential": "<locally-staged-credential>",
  "nodeSigningPublicKeySpki": "<base64url-public-spki>",
  "nodeEncryptionPublicKeySpki": "<base64url-public-spki>"
}
```

After local commit, finalization uses:

```http
POST /api/support-agent/v1/identities/{nodeId}/rotate/finalize
Content-Type: application/json

{
  "rotationId": "00000000-0000-0000-0000-000000000000",
  "tenantId": "<tenant-id>",
  "currentTransportCredential": "<replacement-credential>"
}
```

Neither endpoint accepts private key bytes or connector identity. Anonymous
enrollment completion and rotation requests are limited in-process to 30 requests
per minute for each remote network identity and validated functional partition.
Enrollment partitions use a fixed-size hash of the validated tenant ID. Rotation
partitions add the validated route node ID, and prepare/finalize share a partition.
A separate 240-request per-minute remote-network-and-operation ceiling prevents
partition churn from bypassing abuse protection. Tenant IDs, node IDs, request
bodies, enrollment codes, and credentials are never used as unbounded counter keys
or written to rate-limit telemetry. Functional and source counter maps each have a
hard 1,024-key bound.

`diagnosticMode` is one of `ConnectorOffline`, `CapacityMismatch`,
`JobNotAssigned`, `HostPressure`, or `Full`. `profileId` is optional and must be
validated locally by the broker before any file access.

Read the same session with:

```http
GET /api/tenants/{tenantId}/support/v1/sessions/{sessionId}
Authorization: Bearer <diagnostic-credential-or-browser-session>
```

Creation and completed reads include the pinned session values a client must
retain for resume and signature verification:

```json
{
  "capability": "pitcrew.diagnostics.snapshot.v1",
  "requestDigest": "<lowercase-sha256-of-canonical-request>",
  "expiresAt": "2026-08-01T00:05:00.0000000+00:00",
  "nodeSigningKeyFingerprint": "<lowercase-sha256-of-spki>",
  "result": {
    "report": {
      "schemaVersion": 1,
      "collectionScope": "file-only",
      "diagnosticMode": "ConnectorOffline",
      "profile": "default",
      "pitcrewRoot": "<pitcrew-root>",
      "packageId": "<lowercase-hex-package-id>",
      "collectorSha256": "<lowercase-sha256>"
    },
    "markdown": "<diagnostic markdown>",
    "attestation": {
      "nodeSigningPublicKeySpki": "<base64-spki>",
      "payloadBase64Url": "<base64url-canonical-json>",
      "signatureBase64Url": "<base64url-signature>",
      "signatureAlgorithm": "ES256-P1363"
    }
  }
}
```

The attestation payload is canonical UTF-8 JSON containing `tenantId`, `nodeId`,
`sessionId`, `capability`, `requestDigest`, `expiresAt`, `report`, and
`markdown`. The PitCrew PowerShell diagnostic skill can verify the signature
without scraping Dashboard pages.

The Dashboard session list remains storage-only and does not call the relay once
per row. Select **Check result** on a pending session, or use the single-session
API, to project the exact relay lifecycle. Dashboard persists the first relay
dispatch time and any closed agent rejection disposition. Relay `completed`
state causes Dashboard to fetch the opaque result, but Dashboard reports
`Completed` only after decrypting and verifying the node-signed payload.

When the agent cannot produce a result, it posts one closed disposition to the
exact transport-authenticated relay session. The request contains no tenant,
node, envelope, report, nonce, path, exception, command, or free-form reason.
Relay retries are idempotent for the same disposition; cancellation, expiry,
completion, and a conflicting prior rejection remain terminal. Older agents
continue to degrade to dispatched-then-expired, and a new agent treats an older
relay's missing outcome route as non-fatal while retaining its local bounded
status.

The agent persists the first closed rejection disposition beside replay state
before reporting it. Relay redelivery or agent restart returns that same
disposition without rerunning diagnostics. Broker mode/profile rejection,
missing collector, evidence denial, execution failure, invalid response, local
I/O failure, and timeout remain distinct. A later replay observation cannot
replace the first actionable outcome.

Relay and Dashboard storage enforce that shared closed vocabulary. Startup
migrations replace older, narrower lifecycle constraints transactionally while
preserving existing session status, dispatch, rejection, request, and result
evidence. Every protocol-supported broker rejection must therefore reach the
same terminal relay and Dashboard projection path.

Empty, oversized, or malformed serialized relay envelopes enter the same typed
processing path and produce `envelope-payload-rejected`. They are never treated
as successful empty polls after the relay has dispatched a session.

Broker execution is capped at two minutes. Terminal outcome reporting reserves
20 percent of the remaining authorization window, bounded from five seconds
through one minute; production sessions retain a full minute while short
hermetic sessions remain executable. Deadline cancellation persists
`broker-timeout`; service-stop cancellation still terminates the worker without
creating a false request outcome.

The support page renders the exact session lifecycle independently from semantic
severity: `Queued`, `Dispatched`, `Completed`, `Rejected`, `Cancelled`, or
`Expired`. New browser requests use the default 15-minute maximum session
window. While the page is mounted, up to 16 `Queued` or `Dispatched` sessions
refresh automatically every five seconds with one in-flight batch. Polling stops
for terminal sessions and aborts on navigation. Operators can leave and return;
the initial session read restores any completed result without a manual refresh
button. Dispatched and later states show the first dispatch time when known.
Rejected sessions also show the closed rejection disposition; they never
display relay payloads or free-form agent output.

## Production node isolation

The support agent and broker use a separate explicit installer. The connector
installer is not extended, and the support installer refuses unmanaged or partial
installations that overlap its fixed service names or product roots.

### Windows

- `PitCrewSupportAgent` and `PitCrewSupportBroker` run as distinct virtual
  service identities. The network-facing agent uses an unrestricted service
  SID; the file-only broker uses a restricted service SID so every resource
  access must also satisfy the broker-specific ACL.
- The broker creates `pitcrew-support-broker-v1` with a protected ACL containing
  only the support-agent service SID, broker service SID, LocalSystem, and local
  Administrators. It impersonates each client and requires the configured agent
  service SID before reading a request.
- The fixed collector runs in-process inside the broker. Installer-owned outbound
  firewall blocks are scoped independently to the broker service, service SID,
  and exact executable path. Verification rejects missing, disabled,
  non-blocking, non-outbound, or incorrectly scoped rules.
- Agent, broker, and installer state use separate roots below
  `%ProgramData%\PitCrew\Support`; versioned binaries use separate agent and
  broker roots below `%ProgramFiles%\PitCrew\Support`.
- Before accepting IPC, the running broker verifies its effective service
  identity and every locally allowlisted evidence boundary, then writes one
  bounded broker-owned startup disposition. Enabled-installation `Verify`
  waits for a current `ready` disposition instead of inferring runtime access
  from service configuration and ACL text alone. Broker startup retries
  preflight for up to 60 seconds so atomic projection replacement can converge
  without turning one transient access race into a failed installation.
  A persistent denial names only its closed validation stage—installation
  metadata, state/profile traversal, evidence directory/entries, collector,
  projection, or connector health—without recording paths or file contents.

### Linux

- `pitcrew-support-agent` and `pitcrew-support-broker` are separate system users.
  A product-owned IPC group grants only those identities access to
  `/run/pitcrew-support/broker.sock`.
- The broker requires socket owner/group to match its configured UID/GID, requires
  mode `0660`, reads `SO_PEERCRED`, and rejects a peer whose UID is not the
  configured support-agent UID.
- The broker unit uses `PrivateNetwork=true`,
  `RestrictAddressFamilies=AF_UNIX`, `IPAddressDeny=any`, an empty capability
  bounding set, and read-only system protection. `ProtectHome=tmpfs` hides home
  trees while an exact read-only bind exposes the locally configured PitCrew
  root when it is located beneath one. The broker's `HOME` is its protected
  writable state root so the embedded PowerShell runtime never depends on the
  service account's intentionally nonexistent login home. The agent retains
  only `AF_UNIX`, `AF_INET`, and `AF_INET6`.
- Lifecycle verification reads effective properties with `systemctl show`,
  rather than trusting the base unit text. The current installer owns no
  drop-ins, so any non-empty effective `DropInPaths` fails verification, as do
  overrides of identity, command, network, capability, filesystem,
  runtime-directory, or hardening properties.
- Agent and broker state use `/var/lib/pitcrew-support-agent` and
  `/var/lib/pitcrew-support-broker`; versioned binaries use separate roots below
  `/opt`.

## Exact PitCrew v0.10.8 evidence ACL

The package-owned
`support-evidence-policy-v0.10.8.json` is the shared runtime, installer, and test
contract, verified against PitCrew v0.10.8 commit
`a9fc5884b7e1aea6ef731c701401c46a51d0d3f5` and collector
SHA-256
`18ed0cdb53e288f981bf5cc49cb404a5129b98ac14faaa5a6cbcab07b3591580`.
Installer and runtime canonicalize UTF-8 line endings to LF before hashing, so
Git checkouts and release assets remain equivalent while semantic content drift
is rejected. The broker receives only:

- the fixed
  `plugins/pitcrew-operations/skills/pitcrew-remote-diagnostics/scripts/Collect-PitCrewDiagnostics.ps1`
  collector;
- attribute/traverse access needed to validate `Setup-Runner.ps1`,
  `RunnerProfiles.Functions.ps1`, and `docker-compose.yml`;
- non-inherited list/traverse/attribute access on `.pitcrew-state` solely to
  enumerate profile directory names as required by the fixed collector;
- traverse-only access on each selected profile directory;
- object-inherited/default file read inside each selected profile's dedicated
  `support-evidence` directory, which contains only `desired-capacity.json`,
  `acknowledged-capacity.json`, `static-profile.json`, and
  `observed-state.json`;
- `connector-health.json`, `connector-events.jsonl`, and
  `connector-health-acknowledgement.json` from the standard connector health root
  through the same dedicated-directory inheritance contract.

The profile-state root grant does not inherit to profiles or files. Managers
atomically mirror only the four approved projections into `support-evidence`;
unrelated manager state remains outside the broker-readable directory. The
connector health directory likewise contains only its three approved files. The
acknowledgement file contains a timestamp and at most 256 replay event identifiers;
it is bounded, contains no credential or payload data, and is allowlisted because
the connector atomically persists it beside the two collector projections.
The policy does not grant `.env`, connector identity, checkout-wide read, Docker
socket, arbitrary file, arbitrary script, URL, port, or command access. Profile
IDs come from installer-local configuration; a server-supplied path never
crosses IPC.

Installer verification and broker preflight reject nested, linked, or unexpected
persistent directory entries. They permit only the fixed final names and
dot-prefixed `.tmp` files used during same-directory atomic replacement. The
shared policy caps persistent entries at 32 and transient entries at 256.
Transient files have their own budget so a bounded backlog from interrupted
atomic replacements cannot crowd out otherwise valid diagnostic evidence.

Projection and journal writers replace files atomically inside their dedicated
evidence directories. Windows object-inherit read ACEs and Linux default
file-read ACLs therefore survive replacement without granting inherited read on
the broader profile directory. `Verify` rejects per-file-only, broad-profile,
writable, malformed, or masked access. Run
`-Action RepairEvidenceAcl -AllowMachineChanges` only to restore this exact
directory contract after external ACL drift.

Verification compares the complete product-owned Windows ACE shape, including
rights and inheritance, and the effective Linux named/default entries and masks.
Linux installation records a protected hash of the fixed PitCrew directories,
sentinels, and collector's owner, group, file type, special bits, owner mode, and
other mode. Replaceable projection and connector-health files are checked
against inherited named/default ACLs, ownership exclusion, and safe-mode policy.
Group mode is represented by the POSIX ACL mask and is compared through the exact
ACL contract instead of the metadata hash.

## Install and lifecycle

Prepare an agent `appsettings.json` through the support identity workflow, then
install the matching agent and broker archives:

```powershell
./Install-PitCrewSupportPlane.ps1 `
  -Action Install `
  -Version <version> `
  -PitCrewRoot <pitcrew-root> `
  -Profiles default `
  -AgentSettingsPath ./appsettings.json `
  -AllowMachineChanges
```

Linux PitCrew roots must use a systemd-safe absolute path without whitespace,
colon, or percent characters so the effective read-only bind can be verified
without ambiguous escaping.

When local archive paths are omitted, the installer downloads only the fixed
release asset names for the selected version and native architecture. Every
archive is verified against its SHA-256 sidecar before extraction.

Lifecycle actions are `Update`, `Disable`, `Enable`, `Rollback`,
`FinalizeEnrollment`, `RepairEvidenceAcl`, `Verify`, `DiagnoseFailure`, and
`Uninstall`.
Failed lifecycle mutations persist one protected bounded record containing only
the lifecycle action, phase, operation, exception type, native exit code when
available, rollback status, and occurrence time. It never contains exception
messages, paths, arguments, settings, identities, or credentials. Read it with:

```powershell
./Install-PitCrewSupportPlane.ps1 -Action DiagnoseFailure
```

The support agent also atomically records a protected
`agent-startup-status.json` containing only schema version, startup phase,
terminal disposition, exception type when one exists, and occurrence time.
Provisioning dispositions distinguish active identity load, unsupported local
lifecycle, missing enrollment material, pending identity creation, Dashboard
enrollment rejection, local enrollment commit, and unavailable legacy
configuration.
`Verify` includes those bounded fields when the Windows agent stops. The agent
records an explicit accepted disposition after its first relay poll; no message,
stack, path, setting, identity, credential, or payload is persisted.
Transient broker I/O and timeout failures record separate bounded
`request-processing` dispositions so a later replay rejection cannot erase the
first actionable local failure. Typed broker rejections preserve only the
broker's closed status; the broker error text remains local and unpersisted.

Evidence verification operations distinguish tree enumeration, unexpected or
malformed broker ACEs, agent denial count/shape, root metadata, selected evidence
reads, and prohibited environment access without recording the affected path.
Windows verification walks one directory at a time, audits reparse-point objects
without following them, and skips a non-required descendant that disappears
after discovery. A still-existing directory retries only `ItemNotFoundException`,
bounded by 20 attempts and five seconds. Missing scan roots, access denial,
unexpected entries, and ACL drift remain terminal.

A later successful install, update, rollback, or uninstall removes the prior
failure record. Updates extract both components
into staging, verify their checksums/executables, switch service definitions only
after staging succeeds, and restore the prior version if either service fails to
start. Binary updates must retain the installed PitCrew root and local profile
allowlist. One previous version remains the explicit rollback target; older
version directories are removed after a successful switch. A rollback that
cannot start its target restores the current service definitions before failing.
Every lifecycle action, including `Verify`, holds a privileged installer lock.
The empty lock remains after uninstall so releasing one lifecycle action cannot
race creation of a second lock inode; it contains no installation or identity
data.

After the first accepted poll, run the typed finalization action from the
installed package:

```powershell
./Install-PitCrewSupportPlane.ps1 -Action FinalizeEnrollment -AllowMachineChanges
```

`FinalizeEnrollment` requires an enabled managed installation and an Active
local identity. It stops only the agent, leaves the broker process unchanged,
and asks the agent service identity to preserve an exact protected backup before
removing `DashboardUrl`, `TenantId`, `DisplayName`, and `EnrollmentCode`.
The installer verifies unchanged settings ownership/ACLs, restarts only the
agent, and requires a second accepted relay poll before deleting the backup.
Failure restores the exact prior settings through the agent identity, restores
the prior service state, and records bounded rollback evidence. Re-running the
action after success is idempotent.

Uninstall requires an explicit `-IdentityHandling PreserveKeys` or
`-IdentityHandling DeleteKeys` choice. `PreserveKeys` retains the complete
protected agent state without reading or rewriting private key material.
`DeleteKeys` stops only the agent, invokes the typed local identity manager under
the agent service identity, requires a bounded success record, and removes the
protected agent state after key deletion. Before deleting
installer state, uninstall removes every product agent/broker ACE or
named/default ACL from PitCrew and connector-health trees and removes Windows
services or validated Linux system users/groups.
Revoke the matching Dashboard support identity before `DeleteKeys`; Dashboard
revocation and local key deletion are deliberately separate authorized actions,
and neither operation silently performs the other.
Windows uninstall reuses the same no-follow evidence walk when revoking product
ACEs, so a dangling reparse entry cannot prevent cleanup after keys are deleted.
Failure records advance from identity removal into the exact uninstall cleanup
operation, and an explicit `DeleteKeys` retry remains idempotent when key material
is already absent.
Linux uninstall refuses before mutation when any external account is a
supplementary member of, or uses as its primary GID, any product-owned group.
The installer never reads, rewrites, prints, or selectively deletes private key
values.

The relay and Dashboard configuration remains environment-based:

| Process | Required configuration |
| --- | --- |
| Relay | `SupportRelay__DatabasePath`, `SupportRelay__InternalBearerSecret` |
| Dashboard | public `PitCrew__SupportPlane__RelayUrl`, optional private `PitCrew__SupportPlane__RelayInternalUrl`, `PitCrew__SupportPlane__RelayInternalBearerSecret`, `PitCrew__SupportPlane__AuthorizationSigningPrivateKeyPkcs8`, `PitCrew__SupportPlane__ResultDecryptionPrivateKeyPkcs8` |
| Broker | `PitCrewSupport__Broker__PitCrewRoot`, optional `PitCrewSupport__Broker__PipeName` |
| Agent first enrollment | `PitCrewSupport__Agent__IdentityRoot`, `DashboardUrl`, `TenantId`, `DisplayName`, one-time `EnrollmentCode`, `ReplayRoot`, and optional `PipeName`/`SocketPath` under the `PitCrewSupport__Agent__` prefix |
| Agent after enrollment | `PitCrewSupport__Agent__IdentityRoot`; the node ID, relay URL, transport credential, Dashboard public keys, and node key references load from protected local state |

Dashboard optionally accepts
`PitCrew__SupportPlane__EnrollmentRecoveryLifetimeSeconds`; the validated range
is 300 through 86,400 seconds and the default is 3,600. Durable relay cleanup runs
at startup and then every
`PitCrew__SupportPlane__RelayCleanupIntervalSeconds`; its validated range is 1
through 3,600 seconds and the default is 30.
`PitCrew__SupportPlane__MaximumSessionLifetimeSeconds` caps an otherwise valid
requested session lifetime; its validated range is 30 through 3,600 seconds and
the default is 900.

Start the relay first, then Dashboard, then the broker and agent. The relay
serves `/healthz`. The broker resolves profiles only from
`.pitcrew-state/<profile>` and invokes the fixed collector from the configured
PitCrew root. Keep `ReplayRoot` outside the PitCrew checkout and
`.pitcrew-state`; the defaults are `%ProgramData%\PitCrew\Support` on Windows
and `/var/lib/pitcrew-support` on Linux.

Hosted deployments use the independently versioned, non-root relay image and
optional private-network Compose overlay described in
[Hosted support relay](hosting/support-relay.md). Dashboard keys remain outside
the relay container; only the shared internal bearer crosses that boundary.
Use the [support-plane release rollout](hosting/support-plane-rollout.md) to
preflight a qualified release pair, stage each boundary, preserve rollback
commit points, and complete Dashboard-only acceptance.

The agent archive includes `support-agent.env.example`. Use
`FinalizeEnrollment` to remove one-time bootstrap material after enrollment;
do not edit protected service settings manually. Hardened
configuration does not accept node private PKCS#8 values. Existing manual
configuration remains available only when
`PitCrewSupport__Agent__AllowLegacyPrivateKeyConfiguration=true`; it is a
compatibility path, not the production default.

Packaged agents expose an installer-consumable rotation mode:

```text
PitCrew.Support.Agent.App rotate
```

It writes one JSON outcome containing only `status` and `rotationId`. Exit code
`0` means rotation finalized, `2` means the locally committed rotation remains
safe and requires a retry to finalize, and `1` means prepare or local identity
validation failed. Re-running the same command resumes persisted state.

Production packages run the agent and broker under separate service identities.
Windows uses a protected named-pipe ACL plus exact agent SID validation. Linux
uses an owner-controlled Unix socket plus `SO_PEERCRED`, and the broker service
has no network namespace.

## Packaging

Support agent, broker, and relay remain separate .NET projects. The deterministic
packaging script publishes their self-contained archives plus a platform-tagged
installer archive and SHA-256 sidecars for `linux-x64`, `linux-arm64`,
`win-x64`, and `win-arm64` by default. The installer archive contains the
lifecycle script, agent configuration example, and pinned PitCrew v0.10.8
evidence policy.

Hosted Windows and Linux lifecycle jobs establish a public `example.com:443`
control connection, then repeat the fixed TCP attempt from the installed broker
service context and require denial. No application data or host identifiers are
sent.

The support package does not alter existing connector installation. Operators opt
in to support identity enrollment separately.

Dashboard durably queues relay registration cleanup before enrollment
registration. Successful enrollment removes that cleanup record in the same
SQLite transaction that creates the identity. The enrollment operation owns a
one-minute durable lease, so maintenance cannot revoke an in-flight registration.
After interruption or failure, a hosted maintenance worker independently claims
up to 16 eligible records with a two-minute lease. Failed relay calls release the
record with exponential backoff starting at 30 seconds and capped at one hour;
expired leases are reclaimable after process interruption. Confirmed relay
revocation or absence removes the queue entry. Cleanup logs only generic retry
state and never tenant IDs, node IDs, credentials, or private host details.
