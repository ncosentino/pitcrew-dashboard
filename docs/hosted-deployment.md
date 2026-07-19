# Hosted deployment

PitCrew Dashboard remains an optional read-only control plane. Normal PitCrew
setup does not reference this repository or its images.

## Security model

- Caddy owns public ports 80 and 443 and obtains TLS certificates.
- The dashboard listens only on the private Compose network.
- ASP.NET trusts forwarded scheme and client information only from Caddy's
  fixed private address.
- Human sessions use GitHub OAuth with PKCE and an HTTP-only same-site cookie.
- OAuth access tokens are discarded after the GitHub user profile is read.
- Authenticated mutations require an ASP.NET antiforgery token.
- Connector enrollment and synchronization remain cookie-free, outbound-only
  operations.
- SQLite and ASP.NET data-protection keys share one persistent dashboard volume.
  Protect that volume as a credential-bearing asset.

## GitHub OAuth App

Create a GitHub OAuth App with:

- Homepage URL: `https://YOUR_DOMAIN`
- Authorization callback URL: `https://YOUR_DOMAIN/signin-github`

Use a separate OAuth App for local development. The dashboard requests no
GitHub scopes because it needs only the public user ID, login, name, and avatar.

Find the immutable numeric GitHub ID for the initial system administrator:

```powershell
gh api user --jq .id
```

## Start the stack

Copy the example without committing the result:

```powershell
Copy-Item .env.hosted.example .env.hosted
```

Set:

- `PITCREW_DASHBOARD_VERSION` to an existing released GHCR tag.
- `PITCREW_DASHBOARD_DOMAIN` to the public DNS name.
- `PITCREW_GITHUB_CLIENT_ID` and `PITCREW_GITHUB_CLIENT_SECRET`.
- `PITCREW_SYSTEM_ADMIN_GITHUB_ID` to the immutable bootstrap administrator ID.

Then start one dashboard replica:

```powershell
docker compose `
    --env-file .env.hosted `
    --file docker-compose.hosted.yml `
    up -d
```

SQLite remains single-replica. Do not scale the dashboard service horizontally.
If the fixed `172.31.0.0/24` Compose subnet conflicts with the host, choose a
different private subnet and update the dashboard's configured known-proxy
address to match Caddy.

## Upgrade order

Deploy the dashboard before protocol-v2 connectors. Existing enrolled
protocol-v1 connectors continue to synchronize, but one-time enrollment,
re-enrollment, and credential rotation require the updated connector and the
`PitCrew__Connector__EnrollmentCode` setting.

## Tenant and membership workflow

1. The configured system administrator signs in.
2. Create a tenant with a stable lowercase identifier.
3. Other users sign in once so their GitHub identities become available.
4. A tenant owner grants viewer, administrator, or owner membership.

Roles:

- `viewer`: read fleet state.
- `administrator`: read fleet state, create enrollment codes, revoke nodes, and
  request credential rotation.
- `owner`: administrator capabilities plus membership management.

The final owner cannot be demoted or removed.

## Connector enrollment

Create a one-time code in the selected tenant. Configure the connector:

```text
PitCrew__Connector__DashboardUrl=https://YOUR_DOMAIN
PitCrew__Connector__EnrollmentCode=pc_enroll_...
PitCrew__Connector__DisplayName=build-server-01
PitCrew__Connector__StateRoot=/var/lib/pitcrew/state
PitCrew__Connector__IdentityPath=/var/lib/pitcrew-connector/identity.json
```

Persist the identity path and mount the PitCrew state root read-only. The
connector needs no Docker socket, inbound port, or GitHub runner-registration
credential. Remove the consumed enrollment code from hosted connector
configuration after the identity file has been persisted.

## Credential lifecycle

Rotation is loss-safe:

1. An administrator requests rotation.
2. The dashboard stages a replacement while the current credential remains
   valid.
3. A protocol-v2 connector receives and atomically saves the replacement.
4. The connector's next synchronization promotes the staged credential and
   invalidates the previous credential.

If a delivery response is lost, the current credential remains usable and the
dashboard can stage another replacement.

Revocation is immediate. To restore a revoked connector, create a new one-time
code, configure `PitCrew__Connector__EnrollmentCode`, and restart it. The stable
connector instance reuses the existing dashboard node identity.

## Out of scope

- Remote capacity control or arbitrary node commands.
- Workflow log or workload data shipping.
- Horizontal SQLite replicas.
