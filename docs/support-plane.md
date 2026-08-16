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
  no network role and receives only a closed diagnostic mode, optional local
  profile ID, and package ID over authenticated named-pipe IPC.

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

## MVP process configuration

The support archives are self-contained binaries. V1 MVP configuration is
environment-based:

| Process | Required configuration |
| --- | --- |
| Relay | `SupportRelay__DatabasePath`, `SupportRelay__InternalBearerSecret` |
| Dashboard | `PitCrew__SupportPlane__RelayUrl`, `PitCrew__SupportPlane__RelayInternalBearerSecret`, `PitCrew__SupportPlane__AuthorizationSigningPrivateKeyPkcs8`, `PitCrew__SupportPlane__ResultDecryptionPrivateKeyPkcs8` |
| Broker | `PitCrewSupport__Broker__PitCrewRoot`, optional `PitCrewSupport__Broker__PipeName` |
| Agent | `PitCrewSupport__Agent__TenantId`, `NodeId`, `RelayUrl`, `TransportCredential`, both Dashboard public keys, both node private keys, `ReplayRoot`, and optional `PipeName` under the `PitCrewSupport__Agent__` prefix |

Start the relay first, then Dashboard, then the broker and agent. The relay
serves `/healthz`. The broker resolves profiles only from
`.pitcrew-state/<profile>` and invokes the fixed collector from the configured
PitCrew root. Keep `ReplayRoot` outside the PitCrew checkout and
`.pitcrew-state`; the defaults are `%ProgramData%\PitCrew\Support` on Windows
and `/var/lib/pitcrew-support` on Linux.

`PipeOptions.CurrentUserOnly` means the current MVP agent and broker must run
under the same dedicated low-privilege account. Their code and deployment
artifacts are separate, but filesystem privilege separation is not yet enforced
by the package. Do not treat the MVP archive as a hardened multi-user-host
installation; OS-specific service accounts, pipe ACLs, secure key provisioning,
and install/update/uninstall automation remain required before production
rollout.

## Packaging

Support agent, broker, and relay are separate .NET projects. The deterministic
packaging script publishes self-contained archives and SHA-256 checksum files for
`linux-x64`, `linux-arm64`, `win-x64`, and `win-arm64` by default. These archives
contain binaries and checksums, not hardened service installers.

The support package does not alter existing connector installation. Operators opt
in to support identity enrollment separately.
