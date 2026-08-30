---
# AUTO-GENERATED from .github/instructions/frontend-testing.instructions.md — do not edit
paths:
  - "src/PitCrew.Dashboard.WebApi/ClientApp/src/**/*.test.tsx"
---
# Dashboard frontend tests
- Await observable request, effect, or rendered-state milestones for deferred or
  polled surfaces. Never switch global fake timers around lazy route trees or
  enlarge timeouts to mask scheduling races.
