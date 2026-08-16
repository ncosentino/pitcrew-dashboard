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
node generates an ECDSA P-256 signing key and RSA 3072 encryption key locally.
Dashboard stores the public keys, issues a one-time tenant-bound enrollment code,
and returns the relay URL, transport credential, Dashboard authorization public
key, and Dashboard result-encryption public key. Private keys are written
atomically with owner-only permissions and are never logged.

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

A completed response includes `report`, `markdown`, and:

```json
{
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
`sessionId`, `report`, and `markdown`. The PitCrew PowerShell diagnostic skill
can verify the signature without scraping Dashboard pages.

## Packaging

Support agent, broker, and relay are separate .NET projects. The deterministic
packaging script publishes self-contained archives and SHA-256 checksum files for
`linux-x64`, `linux-arm64`, `win-x64`, and `win-arm64` by default. Manual service
setup must keep the transport process and broker process under separate service
identities with file and network permissions matching ADR-0008.

The support package does not alter existing connector installation. Operators opt
in to support identity enrollment separately.
