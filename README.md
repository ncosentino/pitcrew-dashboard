<p align="center">
  <img src="assets/pitcrew-logo.png" alt="PitCrew logo" width="320" height="320">
</p>

<h1 align="center">PitCrew Dashboard</h1>

<p align="center"><strong>Authenticated, read-only fleet visibility for PitCrew runner pools.</strong></p>

<p align="center">
  <a href="https://github.com/ncosentino/pitcrew-dashboard/actions/workflows/ci.yml"><img alt="CI status" src="https://github.com/ncosentino/pitcrew-dashboard/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/ncosentino/pitcrew-dashboard/actions/workflows/container-ci.yml"><img alt="Container status" src="https://github.com/ncosentino/pitcrew-dashboard/actions/workflows/container-ci.yml/badge.svg"></a>
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-blue.svg"></a>
</p>

Optional local and hosted visibility for
[PitCrew](https://github.com/ncosentino/pitcrew) runner fleets.

PitCrew itself remains independent of this repository. Normal runner setup does
not download dashboard source, images, .NET, Node.js, SQLite, or the connector.

## Architecture

```text
PitCrew server
├── one privileged manager per profile
├── ephemeral worker containers
└── one optional connector
    ├── read-only .pitcrew-state mount
    ├── no Docker socket
    ├── no GitHub runner-registration token
    └── outbound HTTP(S) synchronization
                    |
                    v
Dashboard
├── ASP.NET Core API
├── embedded React application
├── GitHub OAuth + tenant authorization
└── single-replica SQLite projection
```

Each PitCrew manager publishes a credential-free
`.pitcrew-state/<profile>/observed-state.json` document. The connector reads all
profiles from one state root, retains the last valid snapshot through transient
read failures, and synchronizes only when state changes or a heartbeat is due.
When the manager supplies resource telemetry, the fleet view shows host
capacity, manager usage, aggregate worker usage, and per-slot CPU cores,
working-set bytes, and PID counts. These are point-in-time samples collected
roughly every 30 seconds; the dashboard's 5-second polling cadence can repeat
the same sample and does not imply a new measurement.

Resource cards require PitCrew manager contract 7 and a telemetry-aware
connector. Older managers and connectors remain compatible and appear as
unavailable rather than reporting zero usage.

One dashboard accepts independently authenticated connectors from multiple
servers. Node and tenant identity are derived from the connector credential,
never trusted from synchronization payloads.

## Local dashboard

Requirements:

- Docker with Linux-container support
- PowerShell 7
- PitCrew manager contract v5 or later

```powershell
.\Start-LocalDashboard.ps1 `
    -PitCrewStateRoot C:\path\to\pitcrew\.pitcrew-state `
    -ServerName build-server
```

The script starts the dashboard with development-only loopback authentication,
creates a tenant-scoped one-time enrollment code through the authenticated API,
stores it in gitignored `.env.local`, and starts the connector. Open
`http://127.0.0.1:5080`.

The dashboard is bound to loopback. The connector communicates with it over a
private Compose network and mounts the PitCrew state root read-only.

## Hosted read-only deployment

Hosted mode uses GitHub OAuth, persisted tenant memberships, and explicit
tenant-scoped API routes. `docker-compose.hosted.yml` defines the
provider-neutral dashboard, while an ingress overlay publishes HTTPS:

- [Caddy](docs/hosting/caddy.md) for a public VM or server with ports 80 and 443.
- [Cloudflare Tunnel](docs/hosting/cloudflare-tunnel.md) for a home server,
  CGNAT, or an outbound-only deployment.

1. Create a GitHub OAuth App with callback
   `https://YOUR_DOMAIN/signin-github`.
2. Find the immutable GitHub user ID for the first system administrator:

   ```powershell
   gh api user --jq .id
   ```

3. Copy `.env.hosted.example` to `.env.hosted`, set the domain, released image
   version, OAuth credentials, and system-administrator GitHub ID.
4. Start the single-replica stack with the selected ingress overlay:

   ```powershell
   docker compose `
       --env-file .env.hosted `
       --file docker-compose.hosted.yml `
       --file deploy/cloudflare-tunnel.compose.yml `
       up -d
   ```

5. Sign in, create a tenant, and create a one-time connector enrollment code.

The connector supports an HTTPS dashboard URL and needs no inbound server port.
Give every server its own persistent connector identity volume. A code is
consumed once; the resulting node credential is hashed in SQLite. Rotation is
delivered on a protocol-v2 sync and persisted atomically by the connector.
Revoked nodes retain their historical projection but must use a new one-time
code to re-enroll.

See [Hosted deployment](docs/hosted-deployment.md) for the ingress decision
guide, shared security contract, and membership workflow.

## Persistence

SQLite stores connector identities and the latest profile projection. The
dashboard contract is deliberately single-replica:

- Use a named persistent volume.
- Keep WAL mode enabled.
- Use the included online backup and verification tool.
- Do not copy the database without its WAL while the application is running.
- Stop the dashboard before restore.

A client/server database adapter becomes appropriate only when horizontal
dashboard replicas or materially higher write concurrency are required.

See [Database operations](docs/database-operations.md) for backup, verification,
restore, and rollback commands.

## Images

- Dashboard: ASP.NET 10 Alpine with prebuilt React assets.
- Connector: framework-dependent .NET 10 Noble Chiseled, non-root and shellless.

Hosted CI validates amd64 execution, arm64 cross-builds, non-root execution, and
the absence of SDK and Node build tooling from final images. Image-size
measurements are written to each workflow summary.

## Branding

Canonical PitCrew artwork lives in `assets/` and is shared by this README, the
SPA favicon and manifest, container packaging, and social metadata. Before a
release, upload `assets/pitcrew-social-preview.png` under **Settings > General >
Social preview**; GitHub does not expose that repository setting through its
public APIs.

## Development

Requirements:

- .NET 10 SDK
- Node.js 22.18 or later

```powershell
dotnet build
dotnet test

Set-Location src\PitCrew.Dashboard.WebApi\ClientApp
npm install
npm test
```

## Security boundaries

- Only PitCrew managers mount the Docker socket.
- Connectors mount only the non-secret state root and their own identity volume.
- Connector credentials are high-entropy, node-scoped, hashed in SQLite, and
  returned only during enrollment or loss-safe rotation delivery.
- Enrollment codes are high-entropy, tenant-scoped, hashed, expiring, and
  consumed once.
- Human APIs require GitHub OAuth, tenant authorization, and antiforgery tokens
  for mutations.
- OAuth access tokens are used only to read the GitHub user profile during
  sign-in and are not stored in SQLite or the authentication cookie.
- The dashboard does not receive GitHub registration or workload credentials.
- Remote capacity control, arbitrary command execution, and log shipping are not
  implemented.

## About

PitCrew Dashboard is built by [Nick Cosentino](https://www.devleader.ca),
creator of [Dev Leader](https://www.devleader.ca) and
[BrandGhost](https://www.brandghost.ai).

## License

PitCrew Dashboard is available under the [MIT License](LICENSE).
