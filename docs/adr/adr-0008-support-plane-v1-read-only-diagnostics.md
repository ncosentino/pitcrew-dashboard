---
title: "ADR-0008: Dashboard-owned support plane v1"
status: "Accepted"
date: "2026-08-16"
authors: ["Nick Cosentino"]
tags: ["architecture", "support", "security", "diagnostics"]
supersedes: ""
superseded_by: ""
---

# Context and scope

Parent PitCrew issue
[ncosentino/pitcrew#131](https://github.com/ncosentino/pitcrew/issues/131)
defines an outbound support path for file-only diagnostics when the normal
Dashboard connector is unavailable or stale. Dashboard issues
[ncosentino/pitcrew-dashboard#118](https://github.com/ncosentino/pitcrew-dashboard/issues/118)
through
[#124](https://github.com/ncosentino/pitcrew-dashboard/issues/124)
assign Dashboard ownership for the hosted authorization process, opaque relay,
support identity administration, diagnostic sessions, user experience, guidance,
and release packaging.

This record is the Dashboard half of the paired cross-repository decision. It
pairs with
[PitCrew ADR-0007](https://github.com/ncosentino/pitcrew/blob/main/docs/adr/adr-0007-outbound-read-only-support-plane.md),
which owns the local collector, operations-skill, and cross-repository
service-boundary decision.

Verified facts in this repository:

- ADR-0001 permits only one typed outbound capacity operation and rejects a
  generic command bus.
- ADR-0002 adds a separate typed manager-recovery operation with at-most-once
  semantics and preserves the rejection of generic commands.
- The existing connector API and SQLite stores are tenant-scoped and preserve
  protocol compatibility through explicit version/capability gates.
- Existing scoped diagnostic credentials are read-only and already support
  noninteractive diagnostic reads.

Assumptions:

- The parent PitCrew support collector remains the single locally configured
  file-only script for v1.
- Production deployments will provision Dashboard support signing/decryption keys
  outside the relay process and outside source control.
- Stronger service-account and filesystem ACL packaging will supersede the MVP
  same-user named-pipe boundary without changing the wire protocol.

# Decision drivers

- Remove manual evidence handoffs when the normal connector is offline without
  creating a second remote-operations channel.
- Preserve ADR-0001 and ADR-0002 connector capacity/recovery behavior unchanged.
- Make relay compromise limited to delay, drop, replay attempts, and opaque
  storage disclosure; it must not authorize requests or decrypt results.
- Keep support identities independent from connector identities and revocation.
- Split network-facing transport from local PitCrew-state access on the node.
- Keep v1 strictly read-only and mechanically closed over diagnostic modes.
- Provide PowerShell skill consumers with a detached node-signature attestation.

# Decision

Dashboard adds a separate optional support path. It coexists with the existing
connector and does not migrate, replace, or silently extend typed capacity or
manager-recovery operations. Connector capacity, zero-capacity pause, recovery,
health replay, and scoped diagnostic read APIs remain governed by ADR-0001
through ADR-0005. Any future support-plane mutation, operations broker, shell,
Docker access, arbitrary path, arbitrary URL, script, port, tunnel, or automatic
remediation requires a superseding ADR and cannot ship as a v1 evolution.

The hosted support plane is split into two deployable trust boundaries:

1. Dashboard authorization/decryption process: owns tenant authorization,
   support identity inventory, ECDSA P-256 request signing, RSA 3072 result
   decryption, verified result projection, audit, and the tenant UX.
2. Opaque relay process and database: stores encrypted signed request/result
   envelopes, authenticates nodes with hashed high-entropy transport credentials,
   and routes by tenant/node/session identifiers. The relay never holds request
   signing keys or result decryption keys.

Each node has an independent support identity. The node generates ECDSA P-256
signing and RSA 3072 encryption keys locally. Dashboard stores only public keys.
Dashboard returns a one-time tenant-bound enrollment code, relay URL, transport
credential, Dashboard authorization-signing public key, and Dashboard
result-encryption public key. Private keys are stored atomically with owner-only
permissions and are never logged. Revocation is immediate for relay polling and
does not affect connector identity.

Request sealing uses fixed-order canonical UTF-8 JSON for the diagnostic request.
Dashboard encrypts that payload with AES-256-GCM, wraps the AES key with
RSA-OAEP-SHA256 to the node encryption key, and signs the unsigned envelope with
Dashboard ECDSA P-256 using IEEE-P1363 signatures. Results use the same hybrid
encryption to the Dashboard RSA public key and are signed by the node ECDSA key.
V1 uses only built-in .NET cryptography.

Node software is split into a native network transport agent and a file-only
diagnostics broker. The transport process has network access but no direct
PitCrew state access. The broker has local PitCrew state access but no network
role. MVP IPC uses .NET named pipes with `PipeOptions.CurrentUserOnly` as the
cross-platform peer boundary. The MVP therefore runs both processes under one
dedicated account and provides code-level rather than OS-enforced filesystem
separation. Production packages must replace that limitation with
service-account isolation, owner-controlled filesystem permissions, and
OS-specific IPC ACLs before treating it as a high-trust multi-user host
boundary.

The broker executes exactly one locally configured collector:
`<PitCrewRoot>\plugins\pitcrew-operations\skills\pitcrew-remote-diagnostics\scripts\Collect-PitCrewDiagnostics.ps1`
with fixed `-FileOnly -PassThruOnly`, diagnostic mode, optional profile ID, and
package ID. No server-supplied path, executable, script, port, URL, or Docker
handle crosses IPC. Profile identifiers are validated locally before execution.

Dashboard tenant APIs expose:

- `POST /api/tenants/{tenantId}/support/v1/sessions`
- `GET /api/tenants/{tenantId}/support/v1/sessions/{sessionId}`
- support identity enrollment/list/revocation endpoints under the same support
  version.

The session creation body is `{ nodeId, diagnosticMode, profileId?,
expiresInSeconds }`. Tenant administrators may create/read support sessions. The
existing scoped diagnostic bearer credential may also create/read support
diagnostic sessions because the action is read-only and remains limited by its
tenant/node/profile scope. Existing mutation routes stay forbidden to diagnostic
credentials.

Session creation responses and reads include immutable `capability`,
`requestDigest`, `expiresAt`, and `nodeSigningKeyFingerprint` values. The
capability is exactly `pitcrew.diagnostics.snapshot.v1`; request digest and node
signing fingerprint are lowercase SHA-256 values. Completed reads add a `result`
object containing structured report JSON, markdown, and attestation `{
nodeSigningPublicKeySpki, payloadBase64Url, signatureBase64Url,
signatureAlgorithm: 'ES256-P1363' }`.
The attestation public key is standard base64 SPKI; payload and signature are
base64url. The attestation payload is canonical UTF-8 JSON containing
`tenantId`, `nodeId`, `sessionId`, `capability`, `requestDigest`, `expiresAt`,
`report`, and `markdown`, enabling the PitCrew PowerShell skill to verify the
node signature without trusting rendered Dashboard HTML.

# Alternatives considered

## Extend the existing connector

This would reuse enrollment, polling, and storage, but connector unavailability
is the main failure mode the support path addresses. It also couples support
revocation to connector identity and risks widening existing typed operations.
Rejected.

## Put authorization and decryption in the relay

This simplifies deployment but makes relay compromise equivalent to Dashboard
support authority and plaintext result disclosure. It violates the relay-distrust
driver. Rejected.

## Use one node process for transport and diagnostics

This is simpler to package, but a network compromise would directly read local
PitCrew state. The split transport/broker boundary is accepted despite packaging
cost.

## Use a generic command or tunnel capability

This would make future support tasks easy, but it creates a remote-access
surface and contradicts v1 read-only requirements and existing connector ADRs.
Rejected.

## Require browser-to-node, SSH, or WinRM access

These paths keep Dashboard smaller but fail the outbound-only hosted support
objective and require operator-specific reachability. Existing direct/package
PitCrew diagnostics remain available, but they are not the Dashboard support v1
path.

# Consequences

Dashboard gains a diagnostics-only support plane with independent support status,
identity lifecycle, session lifecycle, audit, safe rendering, and package
artifacts. Operators can diagnose `ConnectorOffline`, `CapacityMismatch`,
`JobNotAssigned`, `HostPressure`, and `Full` without a browser-to-node
connection.

The support path adds a second optional native node installation. Operators must
install it deliberately; connector installation is not silently altered. Mixed
fleets are expected: some nodes will have only connector, some only support, some
both, and some neither. UI wording must keep support status distinct from
connector, runner, profile, and incident health.

The relay database intentionally stores opaque envelopes, so relay operations
cannot inspect diagnostic content. Dashboard and node implementations must retain
protocol tests for canonicalization, tampering, expiry, replay, tenant isolation,
revocation, and cached duplicate delivery.

MVP named-pipe IPC is useful for cross-platform local development and packaging,
but it is not the final least-privilege packaging story. Production hardening
requires service accounts and ACLs that keep the transport process from reading
PitCrew state and keep the broker from acquiring network or Docker capabilities.

# Confirmation

Confirmation requires all of the following:

- Protocol tests cover canonical request serialization, ECDSA IEEE-P1363
  signatures, AES-GCM/RSA-OAEP-SHA256 hybrid encryption, tampering, expiry, and
  replay.
- SQLite tests cover support identity tenant isolation, revocation, session
  lifecycle, and verified result persistence.
- Relay tests prove the relay routes by authenticated node identity and stores
  only opaque envelopes.
- API tests prove tenant administrator and diagnostic-credential support session
  access while preserving antiforgery for browser mutations.
- Agent tests prove duplicate requests return cached sealed results without
  rerunning diagnostics.
- Broker tests prove closed-mode/profile validation, named-pipe IPC, and fixed
  file-only collector invocation.
- Frontend tests cover safe untrusted rendering and support status separation.
- Guidance tests cover positive and negative support-plane instruction matches
  and generated Claude mirror synchronization.

# References

- Parent support outcome: [ncosentino/pitcrew#131](https://github.com/ncosentino/pitcrew/issues/131)
  defines the cross-repository support-plane goal and non-goals.
- Dashboard support issues
  [#118](https://github.com/ncosentino/pitcrew-dashboard/issues/118),
  [#119](https://github.com/ncosentino/pitcrew-dashboard/issues/119),
  [#120](https://github.com/ncosentino/pitcrew-dashboard/issues/120),
  [#121](https://github.com/ncosentino/pitcrew-dashboard/issues/121),
  [#122](https://github.com/ncosentino/pitcrew-dashboard/issues/122),
  [#123](https://github.com/ncosentino/pitcrew-dashboard/issues/123), and
  [#124](https://github.com/ncosentino/pitcrew-dashboard/issues/124) split the
  Dashboard-owned architecture, identity, protocol, relay, agent, UX, and
  guidance work.
- [ADR-0001](adr-0001-outbound-capacity-operations.md) and
  [ADR-0002](adr-0002-typed-manager-recovery.md) remain authoritative for typed
  connector operations and are intentionally not superseded.
- [Support plane v1](../support-plane.md) is the maintained implementation and
  operations overview for this decision.
