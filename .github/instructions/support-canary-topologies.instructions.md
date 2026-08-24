---
applyTo: "src/PitCrew.Support.Canary.*/**,scripts/canary/**,.github/workflows/support-canary.yml,tests/Test-SupportReleaseGate.ps1,docs/testing/support-canary.md,docs/adr/adr-0010-extensible-support-canary-harness.md"
---

# Support canary topology profiles

- The portable profile proves process, protocol, cryptography, and file-only
  compatibility only. Do not claim installed service, ACL, firewall, systemd,
  container, or physical-host evidence.
- The Windows-installed profile runs only on a disposable standard
  GitHub-hosted Windows runner. Use the packaged installer, separate service
  identities, typed finalization, exact local cleanup, and the same registered
  scenario; never substitute a self-hosted or live node.
- The Linux-installed profile runs only on a disposable standard GitHub-hosted
  Ubuntu runner with passwordless administrative access. Use the packaged
  installer, separate product identities, exact Unix socket and systemd
  isolation verification, typed finalization, bounded privileged inspection,
  exact local cleanup, and the same registered scenario; never substitute a
  self-hosted or live node.
- The containerized profile runs only on disposable public Linux-hosted
  infrastructure. Build exact run-scoped Dashboard and relay images from the
  selected source, verify immutable local image IDs and labels, use Aspire-owned
  session networking plus run-scoped storage, record container IDs before
  cleanup, and never infer production Compose or multi-architecture evidence.
