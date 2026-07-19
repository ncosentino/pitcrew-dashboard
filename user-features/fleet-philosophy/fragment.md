## Fleet-Based Decomposition

For non-trivial tasks that involve research, architectural decisions, or multi-axis analysis:

- Prefer fleet-based decomposition: spin up multiple parallel sub-agents to investigate different aspects simultaneously, then synthesize results before acting.
- Each sub-agent should have a focused, well-scoped question — not a vague exploration.
- After collecting sub-agent results, synthesize into a cohesive recommendation with clear tradeoffs before presenting to the user.
- Do not start implementing until the research phase has produced a clear plan with evidence.
- For simple, well-understood tasks, skip the fleet — direct execution is fine.
