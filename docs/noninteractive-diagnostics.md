# Noninteractive read-only diagnostics

Tenant administrators can issue expiring credentials for headless fleet and
history queries without sharing an interactive GitHub OAuth cookie.

Open **Settings → Diagnostics** for the tenant. A credential has:

- one tenant;
- an expiry between five minutes and 365 days;
- optional node and profile allowlists;
- a fixed `diagnostics.read` permission;
- an operator label and auditable creation, use, rotation, and revocation
  metadata.

The raw value is displayed only after creation or rotation. The database stores
only its SHA-256 hash.

`expiresAt` is the first instant at which authentication is rejected.

## Authenticate

Keep the credential in an environment variable or operating-system credential
store. Never put it in a URL, query string, repository file, command history,
or log.

```powershell
$headers = @{
    Authorization = "PitCrew-Diagnostics $env:PITCREW_DIAGNOSTICS_CREDENTIAL"
}

Invoke-RestMethod `
    -Uri 'https://dashboard.example/api/diagnostics/v1/tenants/example/fleet/nodes?limit=50' `
    -Headers $headers
```

The header scheme is exactly `PitCrew-Diagnostics`.

## Read surfaces

The dedicated API exposes only:

```text
GET /api/diagnostics/v1/tenants/{tenantId}/fleet/nodes
GET /api/diagnostics/v1/tenants/{tenantId}/fleet/history/capabilities
GET /api/diagnostics/v1/tenants/{tenantId}/fleet/nodes/{nodeId}/history
GET /api/diagnostics/v1/tenants/{tenantId}/fleet/nodes/{nodeId}/profiles/{profileId}/history
```

Current fleet reads use a maximum page size of 100 and an `afterNodeId`
cursor. History uses the same advertised range, point, event, and diagnostic
limits as the browser UI. Each credential is limited to 120 diagnostic reads
per minute.

Node and profile restrictions are enforced before returning current or
historical data. Empty restrictions mean every node or profile in the one
tenant.

## Isolation

Diagnostic credentials do not satisfy browser viewer, administrator, owner, or
system-administrator policies. They cannot:

- change capacity;
- recover a manager;
- enroll, rename, revoke, or rotate a connector;
- create or alter tenant memberships;
- acknowledge incidents;
- access connector credentials, registration material, environment values,
  JIT payloads, job logs, or artifacts.

Requests to another tenant fail authorization. Expired, revoked, malformed, or
rotated credentials fail authentication without falling back to browser state.

Rotation creates a new raw value with the same tenant, expiry, and restrictions
and revokes the previous value atomically. Revocation is immediate.
