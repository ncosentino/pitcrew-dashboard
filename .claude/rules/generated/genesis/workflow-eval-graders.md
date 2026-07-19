---
# AUTO-GENERATED from .github/instructions/genesis/workflow-eval-graders.instructions.md — do not edit
paths:
  - "**/*.Tests.Eval/**/*Check.cs"
  - "**/*.Tests.Eval/**/*Evaluator.cs"
---
# Agentic Workflow Eval Grader Rules

Use code-based checks for objective workflow evidence and model evaluators only for
semantic qualities.

## Deterministic checks

Files ending `Check.cs` verify artifacts, metadata, diagnostics, or environment state
without an LLM call.

- Implement one invariant per check.
- Make the check a pure, thread-safe function of explicit inputs.
- Include specific evidence such as counts, identifiers, paths, and quoted values.
- Verify output produced by the workflow, not validity already present in the seed.
- For tool-using workflows, cover unknown or failed tools, unauthorized side effects, and
  unsafe instructions entering through retrieved content when relevant.
- Return a recorded not-applicable or inconclusive result when required evidence is
  absent; do not silently pass.

## Model evaluators

Files ending `Evaluator.cs` judge one semantic quality that deterministic code cannot
establish.

- Use the supplied judge configuration; never construct the subject-under-test client as
  the judge.
- Pass structured evaluator inputs as native objects or serialized data, not ambiguous
  `ToString()` output.
- Require quoted evidence and populate the result's interpretation, failure state, and
  reason.
- Keep evaluator instances immutable and safe for concurrent trials.

Checks and evaluators return evidence and metrics. They never assert or decide the final
batch gate.
