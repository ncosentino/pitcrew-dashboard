---
applyTo: "**/evals/**/EVAL.ts,**/evals/**/EVAL.tsx"
---

# Vercel Agent Eval Grader Rules

`EVAL.ts` or `EVAL.tsx` is the hidden Vitest grader used by Vercel Agent Eval.

## Grade observable behavior

- Verify the final outcome and environment state, not merely the agent's final message.
- Use deterministic assertions for files, content, builds, tests, schemas, and other
  objective outcomes.
- Read `__agent_eval__/results.json` for transcript-derived behavior such as commands,
  files, tools, turns, and errors when that process is part of the contract.
- Do not require exact prose when multiple semantically correct responses are valid.

## Agentic model judges

Use `environment` or `transcript` judge assertions only for qualities deterministic
Vitest checks cannot establish:

- Grade one dimension with a concrete rubric and structured result.
- Pin an independent judge model when comparing subjects; do not rely on self-grading.
- Keep criteria focused because every judge assertion adds an agent run.

## Reliable execution

- Keep `EVAL.ts` / `EVAL.tsx` unavailable to the agent under test.
- Keep generated results, transcripts, and reports outside the committed definition tree.
