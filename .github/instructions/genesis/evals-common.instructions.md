---
applyTo: "**/evals/**/PROMPT.md,**/evals/**/EVAL.ts,**/evals/**/EVAL.tsx,**/evals/**/package.json,**/evals/**/src/**,**/*.Tests.Eval/**/*.cs"
---

# Evaluation Principles

These rules apply to both custom-agent eval definitions and executable agentic-workflow
eval projects.

## Make failures diagnostic

- Define one objective and one capability or risk dimension per case.
- Derive cases from a real requirement, production signal, incident, or observed failure.
- Use pinned, reviewable inputs; never select a dynamic "latest" fixture.
- Include when behavior should occur and when it must not occur, plus risk-appropriate
  edge and adversarial cases.
- Keep capability, regression, and trust-and-safety sets distinct. Regression cases
  protect known behavior; capability cases remain difficult enough to show improvement;
  high-risk safety cases use hard gates.

## Separate case, trial, and attempt

- A case is one logical input and success contract.
- A trial is one independent execution of that case.
- An attempt is one infrastructure retry within a trial.
- Run repeated trials for nondeterministic behavior and report the distribution. Retries
  repair infrastructure failures; they must not hide agent failures.

## Grade evidence, then decide

- Verify the final outcome and environment state before judging narrative quality.
- Prefer deterministic checks for objective contracts. Use model judges only for
  genuinely semantic qualities.
- Require model judges to return specific evidence and an explicit Unknown or
  inconclusive result when evidence is insufficient.
- Keep scoring separate from gate policy. Evaluators produce metrics and evidence;
  harness or policy code decides Passed, Failed, or Inconclusive.
- Validate model judges against human-labelled hard and borderline cases and revalidate
  after changing the model or rubric.

## Preserve fair comparisons

- Run trials in clean, isolated state with no grader, expected answer, prior result,
  cache, or history leakage.
- Compare variants with the same cases, fixtures, graders, and environment while changing
  one variable at a time.
- Never use the subject model or client as its own judge. Use an independent judge, and
  pin the same judge when comparing subject models or harnesses.

## Report before gating

- Capture native structured responses, tool calls, diagnostics, termination, token usage,
  duration, and artifacts; do not flatten away evidence before evaluation.
- Record every trial, including crashes and failed stages, before applying the final gate.
- Persist immutable result snapshots with the case, subject, model, instructions, skills,
  tools, fixture, grader, environment, and source revision.
- Classify each failure as eval setup, infrastructure, or subject quality before acting.
