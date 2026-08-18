# Hosted support relay

The optional support relay is a separate opaque trust boundary for
[support-plane v1](../support-plane.md). It stores signed or encrypted envelopes
and hashed transport credentials. It never receives Dashboard authorization-signing
keys, result-decryption keys, node private keys, or plaintext diagnostic reports.

The supported hosted model adds `deploy/support-relay.compose.yml` to the existing
Dashboard base and ingress files. The relay has no host port, runs as a non-root
container, and stores its SQLite database in the `support-relay-data` volume.
Dashboard management traffic uses a second internal-only Compose network that is
not joined by Caddy or Cloudflare Tunnel.

## Prerequisites

- Dashboard and relay images from the same compatible release.
- Docker Compose v2.17.0 or later.
- A second public HTTPS hostname for the relay.
- The existing Caddy or Cloudflare Tunnel ingress adapter.
- An owner-only `.env.hosted` file.

Keep `PITCREW_DASHBOARD_VERSION` and `PITCREW_SUPPORT_RELAY_VERSION` independently
pinned. A Dashboard update must not silently replace the relay, and a relay rollback
must not replace Dashboard.

## Initialize configuration

Run the repository script once before rendering the support overlay:

```powershell
./scripts/Initialize-PitCrewHostedSupportPlane.ps1 `
    -Version 0.12.2 `
    -RelayDomain '<relay-domain>' `
    -EnvFile .env.hosted
```

Replace `<relay-domain>` with the relay's DNS hostname. The script:

- generates a high-entropy relay bearer;
- generates ECDSA P-256 authorization-signing and RSA-3072 result-decryption keys;
- writes only the five `PITCREW_SUPPORT_*` entries;
- never prints secret or private-key values;
- rejects duplicate or partial support configuration; and
- validates existing complete configuration without rotating it.

The Dashboard receives all four support-plane configuration classes. The relay
receives only its database path and the shared internal bearer.
Dashboard returns the public HTTPS relay URL to nodes but uses
`http://support-relay-internal:8080` for bearer-authenticated management calls on
the internal-only network.

## Compose models

Cloudflare Tunnel uses:

```powershell
$compose = @(
    '--env-file', '.env.hosted',
    '--file', 'docker-compose.hosted.yml',
    '--file', 'deploy/cloudflare-tunnel.compose.yml',
    '--file', 'deploy/support-relay.compose.yml'
)
```

Caddy uses the additional two-site configuration overlay:

```powershell
$compose = @(
    '--env-file', '.env.hosted',
    '--file', 'docker-compose.hosted.yml',
    '--file', 'deploy/caddy.compose.yml',
    '--file', 'deploy/support-relay.compose.yml',
    '--file', 'deploy/support-relay-caddy.compose.yml'
)
```

Use the same complete model for every lifecycle command. The relay must become
healthy before Dashboard starts, and ingress remains dependent on Dashboard health.

Validate without rendering resolved secrets:

```powershell
docker compose @compose config --quiet
```

## Configure ingress

For Cloudflare Tunnel, add a second published application route:

- Hostname: the configured relay domain.
- Service URL: `http://support-relay:8080`.

The existing tunnel container can reach that service on the private Compose network.
No host port or firewall rule is required.

For Caddy, create public DNS records for both domains. The
`support-relay-caddy.compose.yml` overlay selects the two-site Caddyfile and routes
the relay hostname privately to `support-relay:8080`.

## First deployment

Pull only the pinned application images, then start the complete model:

```powershell
docker compose @compose pull dashboard support-relay
docker compose @compose up --detach --wait --wait-timeout 120
```

Verify:

- Dashboard private and public health;
- exact `pitcrew-dashboard-hosted-ingress-v1` response;
- relay `/healthz` returns HTTP 200 with `status=healthy`;
- neither Dashboard nor relay publishes a host port;
- the relay volume exists and is writable only by its container user; and
- normal connector synchronization resumes.

Do not enroll nodes until both public origins are healthy.

## Relay backup

Before changing the relay version, stop the complete model so no Dashboard or node
request can mutate relay state:

```powershell
docker compose @compose stop
```

Create and verify a timestamped backup inside the existing relay volume with the old
relay version still pinned:

```powershell
docker compose @compose run --rm --no-deps `
    --entrypoint /app/tools/database/PitCrew.Dashboard.DatabaseTool `
    support-relay `
    backup `
    --database /var/lib/pitcrew-support-relay/support-relay.db `
    --output /var/lib/pitcrew-support-relay/backups/support-relay-<timestamp>.db

docker compose @compose run --rm --no-deps `
    --entrypoint /app/tools/database/PitCrew.Dashboard.DatabaseTool `
    support-relay `
    verify `
    --input /var/lib/pitcrew-support-relay/backups/support-relay-<timestamp>.db
```

Retain the exact backup path for rollback. If backup or verification fails, restart
the unchanged complete model and stop.

## Relay update and rollback

1. Pre-pull the target relay image with a process-scoped
   `PITCREW_SUPPORT_RELAY_VERSION` override.
2. Clear the override.
3. Stop the complete model.
4. Create and verify the relay database backup.
5. Replace only the `PITCREW_SUPPORT_RELAY_VERSION` line.
6. Start `support-relay` privately with `--no-deps --wait`.
7. Verify its image identity and `/healthz`.
8. Start the complete model and verify both public origins.

If private relay verification fails, restore the previous version line and use the
database tool's `restore` command with the verified backup before restarting the old
complete model.

Public ingress activation is the commit boundary. After clients can write to the new
relay, do not restore the old database automatically; preserve current state and
report the partial update for diagnosis.

## Operational boundaries

- Never use `docker compose down` for routine Dashboard or relay updates.
- Never restart Docker or stop unrelated containers.
- Never place Dashboard private keys in relay configuration.
- Never expose relay SQLite, the internal bearer, or opaque envelopes in logs.
- Never scale the relay horizontally; its SQLite database is single-writer.
- Back up Dashboard and relay volumes independently.
