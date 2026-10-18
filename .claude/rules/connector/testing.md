---
# AUTO-GENERATED from .github/instructions/connector/testing.instructions.md — do not edit
paths:
  - "src/PitCrew.Connector.Features.Sync.Tests/ConnectorWorkerResilienceTests.cs"
  - "src/PitCrew.Connector.Features.Sync.Tests/ConnectorWorkerTestTimeProvider.cs"
---
# Connector retry timer assertions

- `Task.Delay` truncates positive delays to whole milliseconds before timer
  registration. Compare observed retries with that exact projection of the planned
  timestamp; do not use tick equality or arbitrary tolerances.
