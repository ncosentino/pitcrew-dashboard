---
applyTo: "**/evals/**/PROMPT.md"
---

# Vercel Agent Eval Prompt Rules

`PROMPT.md` is the exact task passed to the coding agent by Vercel Agent Eval.

- Test one specific task the agent should complete in the supplied project fixture.
- Make the request realistic, complete, and unambiguous enough that qualified reviewers
  agree on what success means.
- State every requirement a real user would know, but do not reveal hidden grader logic
  or coach the agent toward the expected implementation.
- Keep expected outcomes and executable grading in `EVAL.ts` or `EVAL.tsx`, which the
  harness withholds from the agent.
- Never include production credentials, customer data, or sensitive internal content.
