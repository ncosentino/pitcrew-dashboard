---
# AUTO-GENERATED from .github/instructions/genesis/workflow-eval-runners.instructions.md — do not edit
paths:
  - "**/*.Tests.Eval/**/*Runner.cs"
  - "**/*.Tests.Eval/**/*Harness.cs"
  - "**/*.Tests.Eval/**/*Fixture*.cs"
  - "**/*.Tests.Eval/**/*Experiment*.cs"
  - "**/*.Tests.Eval/**/*ResultCollector.cs"
  - "**/*.Tests.Eval/**/*QualityGate.cs"
  - "**/*.Tests.Eval/**/*Policy.cs"
---
# Agentic Workflow Eval Runner Rules

Runners and harnesses turn production workflow executions into complete, repeatable
evaluation evidence.

## Invoke production behavior

- Run the production workflow or pipeline through its real public seam.
- Keep conversion from a completed run to evaluator inputs separate from scoring and
  from gate policy.
- Preserve native structured messages, responses, tool calls, stage results, and
  diagnostics. Flatten to text only inside a grader that explicitly needs text.

## Isolate and repeat

- Create one fresh item scope, workspace, and output location per trial.
- Keep shared fixtures, judge hosts, and concurrency limiters thread-safe; keep mutable
  scenario state local to the trial.
- Bound concurrency across the whole eval process, not independently in every scenario.
- Release concurrency capacity before retry delays.
- Treat retries as additional attempts within the same trial, not as extra successful
  samples.

## Capture complete evidence

- Capture diagnostics for every workflow stage, including stages that throw.
- Record termination reason, tool sequence and failures, token usage, durations, warnings,
  outputs, and artifacts.
- Persist immutable, schema-versioned result snapshots instead of live SDK evaluator
  objects.
- Write each trial's artifacts before applying one final batch gate.
- Do not let one crash abort or erase the remaining trials; record it as a failed or
  infrastructure outcome and finish the batch.
- Make insufficient statistical evidence Inconclusive rather than forcing pass or fail.
