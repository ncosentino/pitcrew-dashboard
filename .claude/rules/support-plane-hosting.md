---
# AUTO-GENERATED from .github/instructions/support-plane-hosting.instructions.md — do not edit
paths:
  - "src/PitCrew.Support.Relay.App/Dockerfile"
  - ".env.hosted.example"
  - ".container/support-relay-image.json"
  - "deploy/support-relay*.yml"
  - "deploy/caddy/Caddyfile.support-relay"
  - "docs/hosting/{support-relay,support-plane-rollout}.md"
  - "scripts/Initialize-PitCrewHostedSupportPlane.ps1"
  - "tests/Test-HostedSupportPlaneConfiguration.ps1"
  - ".github/workflows/{container-ci,publish-container}.yml"
---
# Hosted support-relay boundaries

- Keep the relay opaque and read-only: encrypted/signed envelopes and hashed
  transport credentials only; never Dashboard private keys or plaintext reports.
- Run the independently pinned relay image as non-root with no host port, a dedicated
  single-replica SQLite volume, and the packaged database backup/restore tool.
- Keep the overlay opt-in. Start relay before Dashboard, retain ingress dependency
  coordination, and preserve base hosted deployments when the overlay is absent.
- Keep the Dashboard management bearer on an internal-only network that ingress
  adapters do not join. Public node traffic uses the ingress-shared relay endpoint.
- Generate hosted secrets without printing them. Reject duplicate, partial, invalid,
  or silently rotated configuration.
- Validate Cloudflare and Caddy routing without rendering resolved secrets. Relay
  lifecycle commands use the complete selected Compose model and preserve unrelated
  containers.
