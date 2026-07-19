# Pitcrew Dashboard

Optional local and hosted visibility for
[Pitcrew](https://github.com/ncosentino/pitcrew) runner fleets.

Pitcrew itself remains independent of this repository. Normal runner setup does
not download dashboard source, images, .NET, Node.js, SQLite, or the connector.

## Architecture

```text
Pitcrew server
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
└── single-replica SQLite projection
```

Each Pitcrew manager publishes a credential-free
`.pitcrew-state/<profile>/observed-state.json` document. The connector reads all
profiles from one state root, retains the last valid snapshot through transient
read failures, and synchronizes only when state changes or a heartbeat is due.

One dashboard accepts independently authenticated connectors from multiple
servers. Node and tenant identity are derived from the connector credential,
never trusted from synchronization payloads.

## Local dashboard

Requirements:

- Docker with Linux-container support
- PowerShell 7
- Pitcrew manager contract v5 or later

```powershell
.\Start-LocalDashboard.ps1 `
    -PitCrewStateRoot C:\path\to\pitcrew\.pitcrew-state `
    -ServerName build-server
```

The script creates a gitignored `.env.local` containing a generated enrollment
token, builds the dashboard and connector images, and starts the stack. Open
`http://127.0.0.1:5080`.

The dashboard is bound to loopback. The connector communicates with it over a
private Compose network and mounts the Pitcrew state root read-only.

## Hosted deployment

The connector supports an HTTPS dashboard URL and needs no inbound server port.
Give every server its own persistent connector identity volume.

Enrollment uses a deployment-level secret only until the dashboard issues the
node-scoped credential. For hosted installations:

1. Enroll the intended connectors.
2. Persist each connector identity volume.
3. Rotate the dashboard enrollment token.
4. Restart enrolled connectors without the enrollment token.

The read-only fleet UI and `GET /api/fleet/v1/nodes` endpoint do not yet
implement human authentication. Do not expose either publicly without an
authenticated TLS reverse proxy.

## Persistence

SQLite stores connector identities and the latest profile projection. The
dashboard contract is deliberately single-replica:

- Use a named persistent volume.
- Keep WAL mode enabled.
- Use SQLite's online backup API or a quiesced volume snapshot.
- Do not copy the database without its WAL while the application is running.

A client/server database adapter becomes appropriate only when horizontal
dashboard replicas or materially higher write concurrency are required.

## Images

- Dashboard: ASP.NET 10 Alpine with prebuilt React assets.
- Connector: framework-dependent .NET 10 Noble Chiseled, non-root and shellless.

Hosted CI validates amd64 execution, arm64 cross-builds, non-root execution, and
the absence of SDK and Node build tooling from final images. Image-size
measurements are written to each workflow summary.

## Development

Requirements:

- .NET 10 SDK
- Node.js 22.18 or later

```powershell
dotnet build PitCrew.Dashboard.slnx
dotnet test PitCrew.Dashboard.slnx

Set-Location src\PitCrew.Dashboard.WebApi\ClientApp
npm install
npm test
```

## Security boundaries

- Only Pitcrew managers mount the Docker socket.
- Connectors mount only the non-secret state root and their own identity volume.
- Connector credentials are high-entropy, node-scoped, hashed in SQLite, and
  never returned after enrollment.
- The dashboard does not receive GitHub registration or workload credentials.
- Remote capacity control, arbitrary command execution, and log shipping are not
  implemented.

## License

Pitcrew Dashboard is available under the [MIT License](LICENSE).
