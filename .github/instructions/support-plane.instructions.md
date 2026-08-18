---
applyTo: "src/PitCrew.Support.Protocol/**/*.cs,src/PitCrew.Support.Agent.App/**/*.cs,src/PitCrew.Support.Agent.App.Tests/**/*.cs,src/PitCrew.Support.Broker.App/**/*.cs,src/PitCrew.Support.Broker.App.Tests/**/*.cs,src/PitCrew.Support.Relay.App/**/*.cs,src/PitCrew.Support.Relay.App.Tests/**/*.cs,src/PitCrew.Dashboard.Features.Support/**/*.cs,src/PitCrew.Dashboard.Features.Support.Abstractions/**/*.cs,src/Adapters/PitCrew.Dashboard.Adapters.Sqlite/*Support*.cs,src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/**/*.ts,src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/**/*.tsx,assets/support-plane/**,docs/support-plane.md,docs/adr/adr-0008-support-plane-v1-read-only-diagnostics.md,scripts/package-support-plane.ps1,scripts/Install-PitCrewSupportPlane.ps1,tests/Test-SupportPlaneInstaller*.ps1,.github/workflows/ci.yml,.github/workflows/publish-support-plane.yml"
---

# Support plane boundaries

- V1 is read-only. Do not add mutations, operations brokers, shells, arbitrary commands, arbitrary paths/URLs/scripts/ports, tunnels, Docker access, or automatic remediation.
- Keep the relay opaque: signed/encrypted envelopes and hashed transport credentials only; never Dashboard signing/decryption keys, node private keys, or plaintext reports.
- Keep support identity independent from connector identity. Enrollment is tenant-bound and one-time; hash high-entropy transport credentials; support revocation never alters connector credentials.
- Generate ECDSA P-256/RSA-3072 keys locally. Windows uses non-exportable persisted CNG keys and service-only ACLs; Linux uses service-owned `0700` directories and `0600` files and documents root access.
- Enrollment sends public keys and bounded one-time material only; credentials return node-encrypted. Bound exact retries, reject mismatches, lease/retry relay cleanup durably, persist runtime state locally, and never silently re-enroll disabled/rejected nodes.
- Rotation uses durable staged relay/Dashboard/local phases. Keep old credentials usable through local commit, pre-accept replacements, block sessions until finalization, resume exact retries, and never use compensation rollback or connector identity.
- The packaged rotation mode and worker polling share cross-process exclusion; polling reloads identity state.
- Anonymous identity routes use separate bounded functional/network limits keyed by a fixed tenant hash and route node GUID, never secrets, bodies, or unbounded IDs.
- Local removal always requires an explicit preserve-keys or delete-keys choice.
  Legacy environment-provided node private keys remain disabled unless the explicit
  compatibility gate is enabled.
- Use only built-in .NET cryptography for v1: canonical fixed-order UTF-8 JSON, AES-256-GCM, RSA-OAEP-SHA256, and ECDSA P-256 IEEE-P1363.
- The transport process may use the network but cannot read PitCrew state; the broker may read only allowlisted local evidence and owns no outbound network behavior.
- Production packages use separate identities. Windows pipe ACLs and peer checks allow only product/SYSTEM lifecycle SIDs; Linux requires socket mode `0660`, ownership, `SO_PEERCRED`, and the configured agent UID.
- The broker has no outbound network. The fixed collector runs in-process. Windows owns enabled service-, service-SID-, and exact-program firewall blocks. Linux requires `PrivateNetwork=true`, `AF_UNIX` only, and no capabilities. Verify effective firewall/systemd state and reject drop-ins.
- Keep the PitCrew v0.10.1 evidence ACL exact: metadata-only access to its three
  root-validation files; non-inherited profile-directory enumeration on
  `.pitcrew-state`; the fixed collector; the four `.pitcrew-state/<profile>`
  projections; and connector-health snapshot/event journal. Do not grant `.env`,
  Docker socket, checkout-wide, or arbitrary-file reads. Detect
  atomic-replacement ACL drift instead of adding inheritable broad reads. Pin and
  verify the collector content hash. Treat broader, writable, inherited, masked,
  ownership, mode, and default-ACL drift as failures.
- Install separately from the connector; refuse partial overlap. Stage updates, retain rollback, support lifecycle actions under a privileged lock, revoke evidence ACLs, safely remove service identities, and preserve protected identity unless typed removal runs as the agent. Installer code never reads private keys.
- Persist failed installer mutations as a protected bounded phase/operation,
  exception-type, native-exit-code, and rollback-status record. Never persist
  exception messages, paths, arguments, settings, identities, or credentials.
- The broker executes only
  `<PitCrewRoot>\plugins\pitcrew-operations\skills\pitcrew-remote-diagnostics\scripts\Collect-PitCrewDiagnostics.ps1`
  with fixed `-FileOnly -PassThruOnly`, closed diagnostic mode, optional locally
  validated profile ID, and package ID. No server path or executable crosses IPC.
- Treat report JSON and markdown as untrusted node output; render markdown as text or through an approved sanitizer, never raw HTML.
- Session creation and completed API responses expose immutable `capability`,
  `requestDigest`, `expiresAt`, and `nodeSigningKeyFingerprint` values. The signed
  canonical payload contains tenant ID, node ID, session ID, capability, request
  digest, expiry, report, and markdown; the attestation carries base64 SPKI and
  base64url payload/signature fields.
- Scoped diagnostic bearer credentials may create/read support sessions only; never connector or support-identity mutations.
- Tests must include negative coverage for tampering, replay, expiry, tenant/node
  mismatch, revoked support identity, relay cross-node routing, duplicate cached
  results, broker allowlist failures, named-pipe/Unix peer mismatch, socket mode
  drift, service identity separation, broker network isolation, lifecycle rollback,
  disabled-firewall and systemd-drop-in overrides, exact ACL drift, uninstall
  revocation, prohibited file/Docker access, enrollment replay/cross-tenant/expiry,
  duplicate identities, rotation interruption, old-credential rejection,
  owner-only storage validation, removal semantics, isolated rate-limit partitions
  with a source ceiling, leased cleanup recovery, absence of private material, and
  absence of mutation-shaped fields.
See ADR-0008 and `docs/support-plane.md`.
