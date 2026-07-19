---
applyTo: "AGENTS.md,CLAUDE.md,.github/copilot-instructions.md"
---

# Agent Root File Discipline

AGENTS.md, CLAUDE.md, and `.github/copilot-instructions.md` load into every session
regardless of what file is being worked on. Anything expressible as a path-scoped
instruction (matched by file glob) belongs there instead — these three files should
hold only how the agent operates and behaves, never project-specific technical
guidance that a glob could target.

## Changing AGENTS.md requires the user's sign-off

AGENTS.md changes how the agent behaves on every task, not just files matching a
pattern. Propose the change and get explicit agreement before editing it — never edit
it autonomously as a side effect of an unrelated task.

## CLAUDE.md and copilot-instructions.md should only point to AGENTS.md

Their default content is a redirect and nothing else. Do not duplicate or restate
AGENTS.md content in either file — that content lives in exactly one place.

## Harness-specific content is the rare exception

Add content to CLAUDE.md or copilot-instructions.md beyond the redirect only for a
behavior genuinely unique to that harness's mechanics (for example, Claude Code hooks
or settings, or Copilot CLI slash commands) that AGENTS.md cannot express because it
doesn't apply to the other harness. Never use it to restate general guidance or
preferences that belong in AGENTS.md or in a path-scoped instruction file.
