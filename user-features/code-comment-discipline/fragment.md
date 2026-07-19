## Code Comment Discipline

### Public API surface

Document every public API — public types, methods, properties, events, and
exported functions — using the language's idiomatic doc-comment syntax (e.g.
XML `///` for C#, JSDoc for TypeScript, docstrings for Python). Focus on
**intent, contract, and non-obvious behavior**: what the caller needs to know,
not how the implementation works. Cover parameters, return value, thrown
exceptions or error results, and any constraints that aren't expressible in
the signature.

### In-code comments

Use in-code comments **very sparingly**. Code should explain itself through
naming and structure. Reserve comments for non-obvious logic, counter-intuitive
decisions, or constraints that aren't visible from the code alone — the kind
of thing a future reader would otherwise need to re-derive.

When writing a comment, NEVER:

- Refer to phases, steps, or identifiers from the current task's plan
  (e.g. `// in step 3.1 we add the check for X`).
- Refer to points-in-time or events that require external context
  (e.g. `// before the refactor`, `// will be removed next PR`,
  `// parallel to the queue work`).
- Restate the logic the code already expresses
  (e.g. `// increment i by 1` next to `i++`).
- Repeat a hardcoded value from the adjacent code
  (e.g. `// retry up to 5 times` next to `MaxRetries = 5`).

What changed, why it changed, or how a change fits into a sequence of work
belongs in the **commit message or PR description**, not the source file. If
you find yourself wanting to write that kind of comment, move the prose to
the commit instead.

### Example

```csharp
// BAD — restates code, references a plan step, repeats a literal
// Step 2: loop 3 times to retry the call
for (var i = 0; i < 3; i++) { ... }

// GOOD — explains a counter-intuitive choice the code can't convey on its own
// Retry only on transient errors; permanent failures bubble up immediately
// so the caller sees the real exception instead of a generic timeout.
for (var i = 0; i < MaxRetries; i++) { ... }
```
