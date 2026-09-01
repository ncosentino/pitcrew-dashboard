---
applyTo: "src/PitCrew.Dashboard.WebApi/ClientApp/src/**/*.test.tsx"
---
# Dashboard frontend tests
- Await observable request, effect, or rendered-state milestones before advancing fake
  time. Never switch global fake timers around lazy routes or enlarge timeouts.
- Treat full-suite-only failures as unresolved: remove scheduler races and bound worker concurrency instead of accepting an isolated pass or green rerun.
