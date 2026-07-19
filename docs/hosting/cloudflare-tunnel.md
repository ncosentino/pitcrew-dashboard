# Cloudflare Tunnel ingress

Use the [Cloudflare Tunnel](https://developers.cloudflare.com/tunnel/) adapter
for a home server, CGNAT connection, or any host where inbound ports should
remain closed. Tunnel is available on Cloudflare's free plan; domain
registration remains separate.

The adapter runs the pinned, non-root
`cloudflare/cloudflared:2026.7.2` image and establishes outbound connections to
Cloudflare. No dashboard or tunnel port is published on the Docker host.

## Cloudflare setup

1. Add the domain to Cloudflare DNS.
2. In Cloudflare, create a remotely managed named tunnel.
3. Add a published application route:
   - Hostname: the value of `PITCREW_DASHBOARD_DOMAIN`.
   - Service URL: `http://dashboard:8080`.
4. Copy the generated tunnel token into a local secret file.

Create the secret directory and ensure it is not committed:

```powershell
New-Item -ItemType Directory -Path secrets -Force
$tokenPath = Join-Path (Resolve-Path .) 'secrets\cloudflare-tunnel-token'
$token = Read-Host 'Cloudflare tunnel token' -MaskInput
[IO.File]::WriteAllText($tokenPath, $token)
Remove-Variable token
```

On Linux, keep the file private while allowing cloudflared's container group to
read it:

```bash
sudo chown "$(id -u):65532" secrets/cloudflare-tunnel-token
chmod 640 secrets/cloudflare-tunnel-token
```

The container runs as UID/GID `65532`. Plain Docker Compose mounts file-backed
secrets from the host, so the file must remain readable by that group.

Set this path in `.env.hosted`:

```text
PITCREW_CLOUDFLARE_TUNNEL_TOKEN_FILE=./secrets/cloudflare-tunnel-token
```

## Start

```powershell
docker compose `
    --env-file .env.hosted `
    --file docker-compose.hosted.yml `
    --file deploy/cloudflare-tunnel.compose.yml `
    up -d
```

The `cloudflared` health check reports ready only after an edge connection is
established.

## Firewall

No inbound rule or router port forwarding is required. A restrictive outbound
firewall must permit Cloudflare Tunnel traffic on TCP and UDP port 7844.

## Cloudflare Access

Cloudflare Access is not required because PitCrew Dashboard already authenticates
humans through GitHub OAuth. Applying Access to the entire hostname would also
block connector enrollment and synchronization unless path-specific bypass
policies or connector service tokens were configured. Keep Access disabled by
default.

## Stop

```powershell
docker compose `
    --env-file .env.hosted `
    --file docker-compose.hosted.yml `
    --file deploy/cloudflare-tunnel.compose.yml `
    down
```
