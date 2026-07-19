# Caddy ingress

Use the Caddy adapter on a public VM or server where ports 80 and 443 can reach
the Docker host.

## Requirements

- Public DNS `A` and/or `AAAA` records for `PITCREW_DASHBOARD_DOMAIN`.
- Inbound 80/TCP, 443/TCP, and 443/UDP.
- Outbound access for ACME certificate issuance.

Caddy owns TLS certificates, HTTP-to-HTTPS redirects, HTTP/3, and response
compression. Browser security headers are emitted by ASP.NET so they remain
consistent with other ingress adapters.

## Start

```powershell
docker compose `
    --env-file .env.hosted `
    --file docker-compose.hosted.yml `
    --file deploy/caddy.compose.yml `
    up -d
```

The dashboard remains private at `dashboard:8080`. Caddy is the only service
with host ports.

## Persistent state

- `dashboard-data`: SQLite and ASP.NET data-protection keys.
- `caddy-data`: certificates and ACME state.
- `caddy-config`: Caddy runtime configuration.

Back up the dashboard and Caddy volumes independently.

## Stop

```powershell
docker compose `
    --env-file .env.hosted `
    --file docker-compose.hosted.yml `
    --file deploy/caddy.compose.yml `
    down
```
