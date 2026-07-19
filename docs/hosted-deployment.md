# Hosted deployment

PitCrew Dashboard remains an optional read-only control plane. Normal PitCrew
setup does not reference this repository or its images.

The hosted deployment is composed from:

- `docker-compose.hosted.yml`: provider-neutral dashboard, SQLite volume,
  private network, authentication, and container hardening.
- One ingress adapter that publishes HTTPS and forwards to
  `http://dashboard:8080`.

## Choose an ingress adapter

| Environment | Supported adapter | Public host ports |
|-------------|-------------------|-------------------|
| Public VM or server with routable ports 80 and 443 | [Caddy](hosting/caddy.md) | 80/TCP, 443/TCP+UDP |
| Home server, CGNAT, or no inbound firewall changes | [Cloudflare Tunnel](hosting/cloudflare-tunnel.md) | None |
| Existing reverse proxy, load balancer, or tunnel | [Custom ingress](hosting/custom-ingress.md) | Adapter-specific |

Caddy and Cloudflare Tunnel are the officially validated adapters. Additional
adapters can implement the documented custom-ingress contract without changing
the dashboard or connector images.

## Shared security model

- The dashboard has no host port and listens only on the private Compose
  network.
- ASP.NET trusts forwarded scheme and client information only from the selected
  adapter's fixed `172.31.0.2` address.
- Browser security headers and HSTS are application-owned, so every adapter
  receives the same policy.
- Human sessions use GitHub OAuth with PKCE and an HTTP-only same-site cookie.
- OAuth access tokens are discarded after the GitHub user profile is read.
- Authenticated mutations require an ASP.NET antiforgery token.
- Connector enrollment and synchronization remain cookie-free, outbound-only
  operations.
- SQLite and ASP.NET data-protection keys share one persistent dashboard volume.
  Protect that volume as a credential-bearing asset.

## Common configuration

Use Docker Compose v2.17.0 or later. The ingress overlays use dependency restart
coordination introduced in that release.

Create a GitHub OAuth App with:

- Homepage URL: `https://YOUR_DOMAIN`
- Authorization callback URL: `https://YOUR_DOMAIN/signin-github`

Use a separate OAuth App for local development. The dashboard requests no
GitHub scopes because it needs only the public user ID, login, name, and avatar.

Find the immutable numeric GitHub ID for the initial system administrator:

```powershell
gh api user --jq .id
```

Copy the environment example without committing the result:

```powershell
Copy-Item .env.hosted.example .env.hosted
```

Set:

- `PITCREW_DASHBOARD_VERSION` to an existing released GHCR tag.
- `PITCREW_DASHBOARD_DOMAIN` to the public DNS name.
- `PITCREW_GITHUB_CLIENT_ID` and `PITCREW_GITHUB_CLIENT_SECRET`.
- `PITCREW_SYSTEM_ADMIN_GITHUB_ID` to the immutable bootstrap administrator ID.
- `PITCREW_CLOUDFLARE_TUNNEL_TOKEN_FILE` only when using the Cloudflare
  adapter.

Then follow the selected adapter's startup instructions. SQLite remains
single-replica; do not scale the dashboard service horizontally.

If the fixed `172.31.0.0/24` Compose subnet conflicts with the host, choose a
different private subnet and update the dashboard and ingress adapter addresses
together. The ingress adapter must remain the only trusted proxy.

## Upgrade order

Deploy the dashboard before protocol-v2 connectors. Existing enrolled
protocol-v1 connectors continue to synchronize, but one-time enrollment,
re-enrollment, and credential rotation require the updated connector and the
`PitCrew__Connector__EnrollmentCode` setting.

> **Important:** Set `PITCREW_DASHBOARD_VERSION` to `0.2.0` or later before
> starting either ingress overlay. The hosted health check rejects v0.1.0, and
> the overlay dependency policy stops ingress during Compose-managed dashboard
> updates until the replacement satisfies the v0.2.0 contract.

The provider-neutral Compose split changes the v0.1.0 startup command. Existing
Caddy deployments add `--file deploy/caddy.compose.yml`; no application data or
SQLite migration is required. The overlays require dashboard image v0.2.0 or
later because browser security headers moved from Caddy into ASP.NET.

Use the base file and selected overlay together for every hosted `up`, `restart`,
or `down` operation. Replacing the dashboard container directly with `docker`
commands bypasses Compose dependency coordination.

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
