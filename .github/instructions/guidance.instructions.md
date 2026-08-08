---
applyTo: "AGENTS.md,CLAUDE.md,.claude/CLAUDE.md,.github/copilot-instructions.md,.github/instructions/**,.github/agents/**,.github/skills/**,.github/genesis-guidance*.json,.claude/rules/generated/**,scripts/guidance/**,tests/Test-Guidance.ps1,docs/README.md,docs/adr/**,docs/impeccable-design.md,docs/ux-design.md"
---

# Guidance architecture

- Keep project identity and unscopable safeguards in `AGENTS.md`; move exact recurring
  rules to scoped instructions and rationale to maintained docs.
- Treat `.github/instructions/genesis/` as generated managed output. Express local
  specialization outside that subtree and refresh managed files only from the exact
  Genesis template and symbol shape.
- Keep `docs/README.md` as the canonical documentation map and list every maintained
  page in `.github/genesis-guidance.json`.
- Preserve accepted ADR reasoning. Material changes use a new record and explicit
  supersession links.
- The review skill owns procedure only. It derives the diff, instructions, docs,
  decisions, validation, and hosted evidence instead of maintaining another standards
  corpus.
- Keep the Impeccable skill and agents pinned as one project-owned design workflow.
  Hook and audit automation remain opt-in.
- `.github/instructions/` is the source for generated Claude rules. Regenerate
  `.claude/rules/` through the owning compiler and never edit mirrors
  manually.
- Enforce root, instruction, matched-context, documentation, ADR, review, Impeccable,
  and mirror contracts with deterministic positive and negative tests.
- Derive changing project, package, test, and workflow inventories from executable
  sources rather than copying them into guidance.

See [ADR-0006](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/adr/adr-0006-docs-first-agent-guidance.md).
