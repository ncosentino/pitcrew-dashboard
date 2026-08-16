---
applyTo: "src/PitCrew.Support.Protocol/**/*.cs,src/PitCrew.Support.Agent.App/**/*.cs,src/PitCrew.Support.Agent.App.Tests/**/*.cs,src/PitCrew.Support.Broker.App/**/*.cs,src/PitCrew.Support.Broker.App.Tests/**/*.cs,src/PitCrew.Support.Relay.App/**/*.cs,src/PitCrew.Support.Relay.App.Tests/**/*.cs,src/PitCrew.Dashboard.Features.Support/**/*.cs,src/PitCrew.Dashboard.Features.Support.Abstractions/**/*.cs,src/Adapters/PitCrew.Dashboard.Adapters.Sqlite/*Support*.cs,src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/**/*.ts,src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/**/*.tsx,assets/support-plane/**,docs/support-plane.md,docs/adr/adr-0008-support-plane-v1-read-only-diagnostics.md,scripts/package-support-plane.ps1,scripts/Install-PitCrewSupportPlane.ps1,tests/Test-SupportPlaneInstaller*.ps1,.github/workflows/ci.yml,.github/workflows/publish-support-plane.yml"
---

# Support plane boundaries

- V1 is read-only. Do not add mutations, operations brokers, shells, arbitrary
  commands, arbitrary paths, arbitrary URLs, script names, ports, tunnels, Docker
  access, or automatic remediation.
- Keep the relay opaque. Relay code stores signed/encrypted envelopes and hashed
  transport credentials only; it must not hold Dashboard request-signing keys,
  Dashboard result-decryption keys, node private keys, or plaintext reports.
- Keep support identity independent from connector identity. Enrollment is
  tenant-bound and one-time; transport credentials are high entropy and stored only
  as hashes; support revocation must not alter connector credentials.
- Use only built-in .NET cryptography for v1: canonical fixed-order UTF-8 JSON,
  AES-256-GCM payload encryption, RSA-OAEP-SHA256 key wrapping, and ECDSA P-256
  IEEE-P1363 signatures.
- The support transport process may use the network but must not read PitCrew state.
  The diagnostics broker may read locally allowlisted PitCrew state but must not own
  outbound network behavior.
- Production node packages use separate agent and broker service identities. Windows
  named pipes grant only the support-agent service SID plus broker/SYSTEM lifecycle
  identities and validate the impersonated client SID. Linux sockets use owner/group
  mode `0660`, verify socket ownership and `SO_PEERCRED`, and accept only the
  configured support-agent UID.
- The broker has no outbound network capability. Windows installation owns a
  broker-service-scoped outbound firewall block that also covers the fixed
  collector child process and must reject disabled rules. Linux installation
  requires `PrivateNetwork=true`, `RestrictAddressFamilies=AF_UNIX`, and
  capability removal. Verify effective systemd properties with `systemctl show`;
  base-unit text is not evidence when drop-ins can override it.
- Keep the PitCrew v0.10.0 evidence ACL exact: metadata-only access to its three
  root-validation files; non-inherited profile-directory enumeration on
  `.pitcrew-state`; the fixed collector; the four `.pitcrew-state/<profile>`
  projections; and connector-health snapshot/event journal. Do not grant `.env`,
  Docker socket, checkout-wide, or arbitrary-file reads. Detect
  atomic-replacement ACL drift instead of adding inheritable broad reads. Pin and
  verify the collector content hash. Treat broader, writable, inherited, masked,
  ownership, mode, and default-ACL drift as failures.
- Support installation is explicit and separate from connector installation. Refuse
  unmanaged/partial overlap. Stage updates before switching services, retain a
  rollback target, support enable/disable/uninstall, and preserve support identity
  state until the #119 preserve/delete contract is available. Serialize every
  lifecycle action with a privileged lock. Uninstall revokes product evidence
  ACLs and removes safe service identities before deleting installer state,
  preserving only the opaque selected agent identity state.
- The broker executes only
  `<PitCrewRoot>\plugins\pitcrew-operations\skills\pitcrew-remote-diagnostics\scripts\Collect-PitCrewDiagnostics.ps1`
  with fixed `-FileOnly -PassThruOnly`, closed diagnostic mode, optional locally
  validated profile ID, and package ID. No server path or executable crosses IPC.
- Result rendering treats report JSON and markdown as untrusted node output. Render
  diagnostic markdown as text or through an approved sanitizer; never use raw HTML.
- Session creation and completed API responses expose immutable `capability`,
  `requestDigest`, `expiresAt`, and `nodeSigningKeyFingerprint` values. The signed
  canonical payload contains tenant ID, node ID, session ID, capability, request
  digest, expiry, report, and markdown; the attestation carries base64 SPKI and
  base64url payload/signature fields.
- Existing scoped diagnostic bearer credentials may create/read support diagnostic
  sessions because the action is read-only. They must not gain access to connector
  mutations or support identity mutation routes.
- Tests must include negative coverage for tampering, replay, expiry, tenant/node
  mismatch, revoked support identity, relay cross-node routing, duplicate cached
  results, broker allowlist failures, named-pipe/Unix peer mismatch, socket mode
  drift, service identity separation, broker network isolation, lifecycle rollback,
  disabled-firewall and systemd-drop-in overrides, exact ACL drift, uninstall
  revocation, prohibited file/Docker access, and absence of mutation-shaped fields.

See [ADR-0008](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/adr/adr-0008-support-plane-v1-read-only-diagnostics.md)
and [Support plane v1](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/support-plane.md).
