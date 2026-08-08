# Agent Instructions

Pitcrew Dashboard is an optional read-only-by-default fleet control plane for
Pitcrew GitHub Actions runner pools. It supports loopback and hosted deployments,
outbound-only connectors, and narrowly typed host operations.

## Sources of truth

- Start with the [documentation map](docs/README.md) and
  [architecture decisions](docs/adr/README.md).
- Path-scoped files under `.github/instructions/` own exact rules for matching
  edits. Genesis-managed instructions are defaults; local specialization remains
  outside their subtree.
- The project-local Impeccable skill and agents own bounded UI design procedures.
- `.github/instructions/` is the source for generated Claude rules under
  `.claude/rules/`.
- Code, schemas, tests, manifests, scripts, and workflows are executable truth.
  Investigate and correct stale prose when sources disagree.

## Global safeguards

- Keep connector communication outbound-only. Container connectors remain
  read-only and socketless; host operations stay typed, locally constrained, and
  free of server-supplied commands or paths.
- Derive connector identity from credentials, never payload fields. Never transmit
  or log runner credentials, connector identity, JIT material, workload data, or
  private host details.
- Preserve protocol compatibility and explicit unavailable state. Never convert
  missing evidence to zero or infer workload truth from resource activity.
- Keep SQLite single-replica behind domain-specific storage interfaces. New
  databases, brokers, caches, credential boundaries, or remote operations require
  measured evidence and an accepted ADR.

## Delivery

- Push feature branches and deliver through pull requests. Follow
  `.github/genesis-delivery.json`, the owning workflows, and repository hooks.
- Run targeted checks while iterating; complete frontend, test, installer,
  container, and hosted evidence belongs to configured CI.
- Before delivery, run
  [review-changes](.github/skills/review-changes/SKILL.md) and disclose missing
  evidence, assumptions, and deliberately deferred work.
