---
# AUTO-GENERATED from .github/instructions/upgrade-compatibility.instructions.md — do not edit
paths:
  - "src/PitCrew.Protocol/**/*.cs"
  - "src/PitCrew.Connector.Features.Sync/**/*.cs"
  - "src/PitCrew.Connector.Features.Sync.Tests/**/*.cs"
  - "src/Adapters/PitCrew.Dashboard.Adapters.Sqlite/**/*.cs"
  - "src/Adapters/PitCrew.Dashboard.Adapters.Sqlite.Tests/**/*.cs"
  - "scripts/Enable-PitCrewCapacityOperations.ps1"
  - "tests/Test-CapacityOperationsInstaller.ps1"
---
# Upgrade and compatibility safety

- Every fix or change on an installed upgrade path must include a regression that
  reproduces the previously failing persisted or wire state. Fresh-install and
  complete-evidence happy paths are not sufficient upgrade evidence.
- Give protocol properties containing acronyms or initialisms an explicit wire name.
  Test canonical manager JSON deserialization and connector-request reserialization,
  asserting both the required property name and absence of incompatible casing.
- Bind every SQL parameter on every command branch that references it. Inventory tests
  cover empty and non-empty `complete`, `partial`, and `unavailable` states without
  treating incomplete inventory as proof of removal.
- A migration ledger entry is not proof that its schema exists. Migrations that create
  named tables, indexes, or triggers declare a startup-validated schema contract, and
  upgrade tests remove those objects from an applied-ledger fixture to prove repair or
  actionable failure.
- Keep accepted Dashboard synchronization independent from observation completeness.
  Partial evidence may remain degraded while advancing accepted-sync evidence;
  installer gates must accept that state without relabeling it healthy.
- Release qualification for connector, installer, protocol, or schema changes must
  exercise an upgrade from the previous published state, including partial evidence
  and damaged-but-integrity-valid database fixtures where applicable.
