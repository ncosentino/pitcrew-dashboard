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

## Identity and enrollment

A support node identity is independent from the normal connector identity. The
operator generates an ECDSA P-256 signing key and RSA 3072 encryption key on the
node and submits only their public SPKI values to Dashboard. Dashboard returns
the support node ID, relay URL, transport credential, Dashboard authorization
public key, and Dashboard result-encryption public key. The current MVP packages
do not generate or install private key material; operators must keep exported
PKCS#8 values in an owner-only local secret store and never submit them to
Dashboard or the relay.

Revoking support identity stops the next poll/request exchange. It does not
revoke or rotate the connector identity.

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

The Dashboard session list does not decrypt relay results implicitly. Select
**Check result** on a pending session, or use the single-session API, to fetch,
verify, decrypt, and persist the completed result.

## Production node isolation

The support agent and broker use a separate explicit installer. The connector
installer is not extended, and the support installer refuses unmanaged or partial
installations that overlap its fixed service names or product roots.

### Windows

- `PitCrewSupportAgent` and `PitCrewSupportBroker` run as distinct restricted
  virtual service identities.
- The broker creates `pitcrew-support-broker-v1` with a protected ACL containing
  only the support-agent service SID, broker service SID, LocalSystem, and local
  Administrators. It impersonates each client and requires the configured agent
  service SID before reading a request.
- An installer-owned outbound firewall block is scoped to the broker service
  identity, so it also covers the fixed collector child process. Verification
  rejects a missing, disabled, non-blocking, non-outbound, or wrong-service rule
  and checks the active broker binary path separately.
- Agent, broker, and installer state use separate roots below
  `%ProgramData%\PitCrew\Support`; versioned binaries use separate agent and
  broker roots below `%ProgramFiles%\PitCrew\Support`.

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
  root when it is located beneath one. The agent retains only `AF_UNIX`,
  `AF_INET`, and `AF_INET6`.
- Lifecycle verification reads effective properties with `systemctl show`,
  rather than trusting the base unit text. The current installer owns no
  drop-ins, so any non-empty effective `DropInPaths` fails verification, as do
  overrides of identity, command, network, capability, filesystem,
  runtime-directory, or hardening properties.
- Agent and broker state use `/var/lib/pitcrew-support-agent` and
  `/var/lib/pitcrew-support-broker`; versioned binaries use separate roots below
  `/opt`.

## Exact PitCrew v0.10.0 evidence ACL

The package-owned
`support-evidence-policy-v0.10.0.json` is the shared runtime, installer, and test
contract, verified against PitCrew v0.10.0 commit `4d30a031` and collector
SHA-256
`01e8fbcb54ec7f79d8403284d521c0d98956be2f4a617aa881d490b28f88e0a3`.
Installer and runtime both reject collector content drift. The broker receives only:

- the fixed
  `plugins/pitcrew-operations/skills/pitcrew-remote-diagnostics/scripts/Collect-PitCrewDiagnostics.ps1`
  collector;
- attribute/traverse access needed to validate `Setup-Runner.ps1`,
  `RunnerProfiles.Functions.ps1`, and `docker-compose.yml`;
- non-inherited list/traverse/attribute access on `.pitcrew-state` solely to
  enumerate profile directory names as required by the fixed collector;
- `desired-capacity.json`, `acknowledged-capacity.json`,
  `static-profile.json`, and `observed-state.json` for installer-selected local
  profiles;
- `connector-health.json` and `connector-events.jsonl` from the standard connector
  health root.

The profile-state root grant does not inherit to profiles or files. Selected
profile directories receive traverse/attribute access only, and only the four
named projections receive content read. The policy does not grant `.env`,
connector identity, checkout-wide read, Docker socket, arbitrary file, arbitrary
script, URL, port, or command access. Profile IDs come from installer-local
configuration; a server-supplied path never crosses IPC.

Projection and journal writers may replace files atomically without retaining a
per-file ACL. The installer intentionally does not solve this by adding inherited
directory-wide read. `Verify` and broker preflight instead report exact evidence
ACL drift. Run `-Action RepairEvidenceAcl -AllowMachineChanges` to reapply only
the pinned file ACLs after replacement.

Verification compares the complete product-owned Windows ACE shape, including
rights and inheritance, and the effective Linux named/default entries and masks.
Linux installation records a protected hash of the fixed PitCrew directories,
sentinels, and collector's owner, group, file type, special bits, owner mode, and
other mode. Replaceable projections, connector-health paths, and journals are
instead checked against the exact named/default ACL, ownership exclusion, and
safe-mode policy so newly created allowlisted evidence can be repaired explicitly.
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
`RepairEvidenceAcl`, `Verify`, and `Uninstall`. Updates extract both components
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

Uninstall currently requires `-IdentityHandling Preserve`. Before deleting
installer state it removes every product agent/broker ACE or named/default ACL
from PitCrew and connector-health trees, removes Windows services or validated
Linux system users/groups, and reduces agent state to protected
`appsettings.json` plus `identity-preserved.json`.
Linux uninstall refuses before mutation when any external account is a
supplementary member of, or uses as its primary GID, any product-owned group.
`-IdentityHandling ExternalDeleteRequired` refuses before mutation because issue
#119 owns the future preserve/delete contract. The installer never reads,
rewrites, prints, or selectively deletes private key values. A later managed
install recognizes the product-owned preservation marker and reuses the
protected settings without copying key material through another interface.

The relay and Dashboard configuration remains environment-based:

| Process | Required configuration |
| --- | --- |
| Relay | `SupportRelay__DatabasePath`, `SupportRelay__InternalBearerSecret` |
| Dashboard | `PitCrew__SupportPlane__RelayUrl`, `PitCrew__SupportPlane__RelayInternalBearerSecret`, `PitCrew__SupportPlane__AuthorizationSigningPrivateKeyPkcs8`, `PitCrew__SupportPlane__ResultDecryptionPrivateKeyPkcs8` |

## Packaging

Support agent, broker, and relay remain separate .NET projects. The deterministic
packaging script publishes their self-contained archives plus a platform-tagged
installer archive and SHA-256 sidecars for `linux-x64`, `linux-arm64`,
`win-x64`, and `win-arm64` by default. The installer archive contains the
lifecycle script and the pinned PitCrew v0.10.0 evidence policy.

Hosted Windows and Linux lifecycle jobs establish a public `example.com:443`
control connection, then repeat the fixed TCP attempt from the installed broker
service context and require denial. No application data or host identifiers are
sent.

The support package does not alter existing connector installation. Operators opt
in to support identity enrollment separately.
