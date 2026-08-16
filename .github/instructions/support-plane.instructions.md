---
applyTo: "src/PitCrew.Support.Protocol/**/*.cs,src/PitCrew.Support.Agent.App/**/*.cs,src/PitCrew.Support.Agent.App.Tests/**/*.cs,src/PitCrew.Support.Broker.App/**/*.cs,src/PitCrew.Support.Broker.App.Tests/**/*.cs,src/PitCrew.Support.Relay.App/**/*.cs,src/PitCrew.Support.Relay.App.Tests/**/*.cs,src/PitCrew.Dashboard.Features.Support/**/*.cs,src/PitCrew.Dashboard.Features.Support.Abstractions/**/*.cs,src/Adapters/PitCrew.Dashboard.Adapters.Sqlite/*Support*.cs,src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/**/*.ts,src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/**/*.tsx,docs/support-plane.md,docs/adr/adr-0008-support-plane-v1-read-only-diagnostics.md,scripts/package-support-plane.ps1,.github/workflows/publish-support-plane.yml"
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
- MVP local IPC uses .NET named pipes with `PipeOptions.CurrentUserOnly`. Packaging
  and docs must state that production service-account and ACL hardening is required.
- The broker executes only
  `<PitCrewRoot>\plugins\pitcrew-operations\skills\pitcrew-remote-diagnostics\scripts\Collect-PitCrewDiagnostics.ps1`
  with fixed `-FileOnly -PassThruOnly`, closed diagnostic mode, optional locally
  validated profile ID, and package ID. No server path or executable crosses IPC.
- Result rendering treats report JSON and markdown as untrusted node output. Render
  diagnostic markdown as text or through an approved sanitizer; never use raw HTML.
- Completed API responses expose `nodeSigningKeyFingerprint` as lowercase SHA-256,
  base64 SPKI in `result.attestation.nodeSigningPublicKeySpki`, and a signed
  canonical payload containing tenant ID, node ID, session ID, report, and markdown.
- Existing scoped diagnostic bearer credentials may create/read support diagnostic
  sessions because the action is read-only. They must not gain access to connector
  mutations or support identity mutation routes.
- Tests must include negative coverage for tampering, replay, expiry, tenant/node
  mismatch, revoked support identity, relay cross-node routing, duplicate cached
  results, broker allowlist failures, and absence of mutation-shaped fields.

See [ADR-0008](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/adr/adr-0008-support-plane-v1-read-only-diagnostics.md)
and [Support plane v1](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/support-plane.md).
