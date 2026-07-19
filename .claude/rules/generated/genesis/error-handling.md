---
# AUTO-GENERATED from .github/instructions/genesis/error-handling.instructions.md — do not edit
paths:
  - "**/*.cs"
---
# .NET Error Handling — Result Pattern

- **Never throw exceptions unless you intend the application to terminate on the spot.** Throwing
  is reserved for unrecoverable programming errors (e.g. invalid configuration at startup), not
  runtime failures.
- Instead, use a **result pattern**: return successful state, or otherwise an error. The standard
  result types are `TriedEx<T>`, `TriedNullEx<T?>`, and `Exception?`, produced via the
  `Try.GetAsync` / `Try.Get` helpers (available in `NexusLabs.Framework`, or your codebase may
  have its own).
- Exception objects are acceptable error result types when **not** crossing a serialization
  boundary.
- Validation failures are **not** exceptions — use result-based validation patterns.
