<p align="center">
  <img src="assets/pitcrew-logo.png" alt="PitCrew logo" width="320" height="320">
</p>

<h1 align="center">PitCrew Dashboard</h1>

<p align="center"><strong>Authenticated fleet visibility with opt-in, locally constrained capacity control.</strong></p>

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
    ├── read-only container mode, or opt-in host operator mode
    ├── no Docker socket
    ├── no transmitted GitHub runner-registration token
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

PitCrew manager contract 8 adds demand-driven autoscaling observations without
changing connector protocol version 2. The fleet view labels fixed-capacity
profiles separately from autoscaled scale sets. Autoscaled profiles show the
configured maximum, current activation target, live and draining containers,
job demand, idle and busy runners, minimum-idle policy, scale-down timing,
scale-set count, and degraded errors. Maximum capacity is a ceiling rather than
a health target, so an idle profile with zero active runners can be healthy.
Older connectors may omit these additive fields; those observations continue
to appear as fixed-capacity profiles.

PitCrew manager contract 10 adds authoritative GitHub registration state
without changing connector protocol version 3. Fleet and runner views show
local slot processes separately from GitHub-eligible capacity, and each slot
reports connected, disconnected, registration-missing, or unknown. Older
manager observations remain readable but display registration eligibility as
unknown rather than treating local process state as usable capacity.

PitCrew manager contract 11 adds worker admission policy, image identity,
container I/O, and bounded exit evidence without changing connector protocol
version 3. Profile detail shows the configured per-worker memory, memory plus
swap, CPU, and PID limits alongside the profile admission ceiling, and lists
each scale-set target with local Docker worker counts kept separate from
timestamped GitHub scale-set statistics. Divergence such as two live containers
against eight registered runners is surfaced without implying that a live
container is eligible for work or that a registration is safe to remove, and
statistics are labelled current, stale, or unavailable. Slot and runner views
add the worker image digest, cumulative network and block I/O counters, and the
last-exit classification, exit code, signal, Docker out-of-memory flag, and
evidence source. Unmeasured values read as unavailable rather than zero, and
absent exit evidence is never described as a clean exit. Contract 10 and older
observations remain accepted and display the new evidence as unavailable.

PitCrew manager contract 12 adds subsystem operation health, a bounded durable
operation journal, and explicit capacity-deficit evidence without changing
connector protocol version 3. Profile detail shows the Docker and GitHub
operations the manager itself performed, with the last success, last failure,
consecutive failures, and scheduled retry; this describes manager operations
rather than the health of Docker or GitHub. Capacity shortfalls are measured
against the desired slots of a fixed profile or the accepted activation target
of each scale-set target, never against the configured autoscaling maximum, and
the blocking reason is labelled as manager-supplied evidence rather than a
dashboard diagnosis. Manager operations are listed newest first and are
deduplicated by durable sequence, so a manager restart or a repeated connector
heartbeat does not duplicate entries. The journal is labelled current,
truncated, or unavailable, and a truncated journal reports how many entries the
manager discarded. Unmeasured durations and unavailable eligibility read as
unavailable rather than zero, while a measured eligibility shortfall is reported
as a shortfall even when local capacity meets the target. Collapsed summaries
report the manager outcomes that did not complete and distinguish a degraded or
unavailable subsystem from a healthy one. Malformed, oversized, or inconsistent
evidence is rejected without losing the last valid projection, and contract 11
and older observations remain accepted and display the new evidence as
unavailable.

Connector protocol 3 adds one typed write capability for host-installed,
explicitly allowlisted connectors: setting the absolute capacity maximum of an
existing single-target profile. Container connectors remain read-only. See
[Capacity operations](docs/capacity-operations.md). Releases include
self-contained host connector archives and an installer that migrates the
existing connector identity and rolls back to the container if service startup
fails.

Connector protocol 4 adds one additional typed write capability: recovering a
wedged manager for an existing profile. Recovery is queued only for connectors
that advertise the capability, is limited to tenant administrators, executes at
most once per queued command, and cannot run while a capacity command is active
for the same profile. Protocol 1-3 connectors never receive recovery work. See
[Manager recovery](docs/manager-recovery.md) and
[ADR-0002](docs/adr/adr-0002-typed-manager-recovery.md).

One dashboard accepts independently authenticated connectors from multiple
servers. Node and tenant identity are derived from the connector credential,
never trusted from synchronization payloads.

## Dashboard navigation

The authenticated application uses tenant-scoped routes and persistent
navigation:

- **Fleet** summarizes nodes and links to node and profile detail.
- **Runners** searches slots across every node and profile in the active tenant.
- **Settings** separates tenant naming, access, and connector enrollment from
  operational fleet views.
- **Tenant administration** remains visible only to configured system
  administrators.

Node and profile detail pages carry a collapsed **History** panel with truthful
bounded ranges: the last four hours of per-observation samples, or hourly peaks
over 24 hours, 7 days, or 30 days. Every range requests an explicit point and
event cap, so the panel never claims a window wider than the data it shows.
Charts are decorative for assistive technology and are always paired with an
equivalent data table, plot points at time-proportional positions so a real gap
in retained observations is drawn as a gap, and unavailable measurements break
the plotted line instead of being drawn as zero. Scrollable tables and event
lists are keyboard-focusable labelled regions, and node history groups each
profile behind its own disclosure.

Capacity changes and node lifecycle actions remain on their relevant detail
pages rather than the fleet overview. Direct links survive authentication and
browser refresh through the ASP.NET SPA fallback.

Contributor guidance for feature manifests, route ownership, shared polling,
and boundary checks is in
[Frontend architecture](docs/frontend-architecture.md).

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

### Historical telemetry

SQLite also stores bounded historical telemetry alongside the latest projection.
Latest-state reads are unchanged. History and latest state are written inside one
SQLite transaction on the connector heartbeat path, so a crash, cancellation, or
history failure can never advance the latest projection while losing its sample
and events.

- A telemetry sample is appended only when the authoritative manager
  `observedAt` advances, so a duplicated connector heartbeat creates no
  duplicate sample.
- A manager event is stored once per durable `(node, profile, epoch, sequence)`
  identity. The epoch is a local, durable generation counter that advances only
  when a manager sequence regression proves the manager journal was lost, so an
  ordinary heartbeat or manager restart replay still deduplicates while a reset
  journal becomes new history rather than being silently discarded.
- Hourly rollups accumulate incrementally as samples arrive and are never
  recomputed from raw rows, so pruning raw samples can never lower or overwrite a
  completed hourly peak or sample count.
- Contract-12 subsystem health and every target-keyed capacity-deficit evidence
  change is retained on change. Autoscaling targets are never collapsed into one
  selected deficit, and success, failure, and backoff summaries are preserved.
- Observations and events stamped further ahead of dashboard time than the
  configured clock-skew tolerance (five minutes by default) are rejected and
  counted, so a mis-set manager clock cannot create future buckets that ordinary
  age-based retention would never reach.
- Retention sweeps every historical profile recorded for the node, including
  profiles that are offline, removed, or absent from the newest heartbeat, and
  expires the cursors themselves. Profile-identifier churn therefore cannot
  bypass the node-wide bounds.
- Journal and retention gaps stay explicit: the dashboard records durable
  sequences the manager advanced past between deliveries, sequences the manager
  still retains above the highest delivered one, detected journal resets,
  rejected future timestamps, and how many rows dashboard retention itself
  deleted, with the oldest retained observation for each kind of row.
- `null` continues to mean unavailable and `0` continues to mean measured zero.
  Local worker counts and GitHub control-plane counts remain separate evidence.

Retention is bounded by measured growth. A retained sample measures at about
**444 bytes** of checkpointed SQLite growth (425,984 bytes of database file for
960 samples, measured after `PRAGMA wal_checkpoint(TRUNCATE)` in
`SqliteFleetHistoryStoreTests`). That figure is the total cost of the append
divided by the samples appended, not the sample row alone: it also covers the
hourly rollups those samples aggregate into, manager events, subsystem health
changes, target-keyed capacity-deficit evidence, cursor rows, and every
supporting index. The write-ahead log is measured separately and
peaked at about **4.0 MB** (4,157,112 bytes) across those 960 single-heartbeat
transactions before checkpointing, which is transient working space rather than
retained growth.

A profile polled every fifteen seconds therefore costs about **2.4 MiB per day**
of checkpointed growth, so the conservative defaults are:

| Tier | Default | Approximate cost per profile |
| ---- | ------- | ---------------------------- |
| Per-observation samples | 7 days, at most 60,000 rows | about 17 MB |
| Hourly rollups | 90 days | under 1 MB |
| Durable manager events | 30 days, at most 20,000 rows | a few MB |

Every tier is configurable through `FleetDashboard` options. Each is capped by a
hard per-profile row ceiling and by hard node-wide ceilings
(`MaximumTelemetrySamplesPerNode`, `MaximumTelemetryRollupsPerNode`,
`MaximumManagerEventsPerNode`), so a misbehaving connector cannot grow the
database without bound even by rotating profile identifiers.

History is read through bounded, tenant-scoped, time-range endpoints:

- `GET /api/tenants/{tenantId}/fleet/v1/nodes/{nodeId}/history`
- `GET /api/tenants/{tenantId}/fleet/v1/nodes/{nodeId}/profiles/{profileId}/history`

Both accept `from`, `to`, `resolution` (`raw` or `hourly`), `points`, and
`events`. The range defaults to 24 hours, is rejected beyond the configured
maximum, and every response is capped by explicit per-profile and node-wide
point and event limits that report truncation rather than silently dropping
data. Truncation always keeps the most recent data inside the requested range
and hides older data inside the same range. Each response is served from one
consistent SQLite read transaction, so ownership, points, events, cursors, and
gaps always describe the same instant.

Hourly requests are served on whole UTC hour boundaries. The served range is
aligned inward — `from` is rounded up and `to` is rounded down to a whole hour —
and the aligned bounds are returned in the response, so an hourly answer never
includes a bucket that reaches outside the requested range. A request narrower
than one whole hour at hourly resolution is rejected instead of being answered
with an edge bucket that contains data from outside the range. The dashboard
aligns its own hourly requests the same way.

A node owned by another tenant is indistinguishable from a missing node.

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

See [Frontend architecture](docs/frontend-architecture.md) before adding routes,
navigation, or shared frontend data.

## Security boundaries

- Only PitCrew managers mount the Docker socket.
- Container connectors mount only the non-secret state root and their own
  identity volume. Opt-in host-service connectors can invoke the typed
  capacity-only setup path without receiving server-supplied commands or paths.
- Connector credentials are high-entropy, node-scoped, hashed in SQLite, and
  returned only during enrollment or loss-safe rotation delivery.
- Enrollment codes are high-entropy, tenant-scoped, hashed, expiring, and
  consumed once.
- Human APIs require GitHub OAuth, tenant authorization, and antiforgery tokens
  for mutations.
- OAuth access tokens are used only to read the GitHub user profile during
  sign-in and are not stored in SQLite or the authentication cookie.
- The dashboard does not receive GitHub registration or workload credentials.
- Remote operations are limited to the typed, locally constrained capacity
  maximum. Arbitrary command execution and log shipping are not implemented.

## About

PitCrew Dashboard is built by [Nick Cosentino](https://www.devleader.ca),
creator of [Dev Leader](https://www.devleader.ca) and
[BrandGhost](https://www.brandghost.ai).

## License

PitCrew Dashboard is available under the [MIT License](LICENSE).
