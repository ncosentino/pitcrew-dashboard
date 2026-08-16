---
# AUTO-GENERATED from .github/instructions/support-plane.instructions.md — do not edit
paths:
  - "src/PitCrew.Support.Protocol/**/*.cs"
  - "src/PitCrew.Support.Agent.App/**/*.cs"
  - "src/PitCrew.Support.Agent.App.Tests/**/*.cs"
  - "src/PitCrew.Support.Broker.App/**/*.cs"
  - "src/PitCrew.Support.Broker.App.Tests/**/*.cs"
  - "src/PitCrew.Support.Relay.App/**/*.cs"
  - "src/PitCrew.Support.Relay.App.Tests/**/*.cs"
  - "src/PitCrew.Dashboard.Features.Support/**/*.cs"
  - "src/PitCrew.Dashboard.Features.Support.Abstractions/**/*.cs"
  - "src/Adapters/PitCrew.Dashboard.Adapters.Sqlite/*Support*.cs"
  - "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/**/*.ts"
  - "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/**/*.tsx"
  - "docs/support-plane.md"
  - "docs/adr/adr-0008-support-plane-v1-read-only-diagnostics.md"
  - "scripts/package-support-plane.ps1"
  - ".github/workflows/publish-support-plane.yml"
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
- Generate node ECDSA P-256 signing and RSA-3072 encryption keys locally. Windows
  uses user-scoped persisted CNG keys with private export prohibited and protected
  service-identity-only state ACLs. Linux uses an owner-only `0700` identity
  directory and `0600` PKCS#8/state files, and reload validates the effective
  service UID owns every identity artifact; document that root can still access
  software keys.
- Node enrollment sends only public SPKIs and one-time tenant-bound enrollment
  material plus a locally persisted completion identifier. Return the transport
  credential only in an envelope encrypted to the node key. Exact completion retries
  may recover that same envelope only for a configured bounded retention window;
  mismatched retries fail. Queue relay cleanup under an enrollment lease before the
  side effect; retry orphans independently with atomic claims, expiring leases, and bounded backoff.
  Persist the returned node, relay, transport, and Dashboard public-key state
  locally; never silently re-enroll a disabled or authorization-rejected node.
- Rotation stages new local keys and a transport credential, then uses durable
  relay dual-credential prepare, Dashboard prepare, local commit, Dashboard key
  promotion, relay credential promotion, and Dashboard finalization. The old
  credential remains usable until local commit; the replacement is relay-accepted
  before local commit; sessions remain blocked from Dashboard prepare through
  finalization; exact retries resume persisted phases. Do not use compensating
  rollback as the safety mechanism. Connector identity is never part of rotation.
- The packaged rotation mode and worker polling share cross-process exclusion; polling reloads identity state.
- Anonymous enrollment-completion and rotation routes use separate bounded
  functional and network limits, keyed by a fixed-size validated-tenant hash and,
  for rotation, the route node GUID; never use secrets, raw bodies, or unbounded IDs.
- Local removal always requires an explicit preserve-keys or delete-keys choice.
  Legacy environment-provided node private keys remain disabled unless the explicit
  compatibility gate is enabled.
- Use only built-in .NET cryptography for v1: canonical fixed-order UTF-8 JSON,
  AES-256-GCM payload encryption, RSA-OAEP-SHA256 key wrapping, and ECDSA P-256
  IEEE-P1363 signatures.
- The support transport process may use the network but must not read PitCrew state.
  The diagnostics broker may read locally allowlisted PitCrew state but must not own
  outbound network behavior.
- MVP local IPC uses .NET named pipes with `PipeOptions.CurrentUserOnly`. Packaging
  and docs must state that production service-account and ACL hardening is required.
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
  results, broker allowlist failures, enrollment replay/cross-tenant/expiry,
  duplicate identities, rotation interruption and old-credential rejection,
  owner-only storage validation, removal semantics, isolated rate-limit partitions
  with a source ceiling, leased cleanup recovery, and absence of private material.

See [ADR-0008](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/adr/adr-0008-support-plane-v1-read-only-diagnostics.md)
and [Support plane v1](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/support-plane.md).
