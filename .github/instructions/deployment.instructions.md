---
applyTo: "Dockerfile,src/PitCrew.Connector.App/Dockerfile,src/PitCrew.Support.Relay.App/Dockerfile,docker-compose*.yml,deploy/**,.env.hosted.example,.container/**,scripts/container/**,.github/workflows/{container-ci,publish-container,publish-host-connector}.yml"
---

# Container and deployment contracts

- Keep dashboard and connector images independently buildable, testable, publishable,
  and versioned. The optional support relay is a third independently pinned image.
- Final images run non-root and contain no .NET SDK or Node build toolchain. Preserve
  the measured image-size and idle-footprint release gates.
- The connector image remains read-only, socketless, and limited to its state-root and
  identity mounts. Host operator mode is a separate native-service deployment.
- Keep SQLite single-replica with its database and data-protection keys on the
  dashboard volume. Backup and restore use the packaged database tool.
- Keep relay SQLite single-replica on its own volume. Package the database tool in
  the relay image and verify backup/restore independently from Dashboard.
- Use the complete hosted base-plus-ingress Compose model for lifecycle operations.
  Include every enabled optional overlay. Do not publish Dashboard or relay directly
  or bypass ingress dependency coordination.
- The relay image runs non-root with no host port and receives only its database path
  and internal bearer. Dashboard signing/decryption keys never enter relay
  configuration.
- Keep ingress trust pinned to the configured private proxy address and preserve
  application-owned browser security headers.
- Never render secret-bearing Compose configuration in logs or tests. Validate with
  scoped placeholder values and `config --quiet` unless a test consumes sanitized
  structured output.
- Release workflows validate canonical semantic tags, smoke-test before registry
  authentication, publish amd64/arm64 indexes, and retain immutable version and source
  tags.

See [Hosted deployment](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/hosted-deployment.md)
and [Container packaging](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/container/README.md).
