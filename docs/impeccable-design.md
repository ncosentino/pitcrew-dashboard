# Impeccable design workflow

This project includes a pinned, project-local Impeccable skill and four GitHub Copilot
agents for designing, reviewing, documenting, and applying UI work. The payload is
vendored by Genesis from Impeccable skill 4.0.4; scaffolding does not run Impeccable's
network installer and does not add an application package dependency.

## Repository design authority

- [`PRODUCT.md`](../PRODUCT.md) owns durable users, jobs, product purpose,
  positioning, operating constraints, evidence, and product principles.
- [`DESIGN.md`](../DESIGN.md) and `.impeccable/design.json` own the reusable visual
  system and its machine-readable extensions.
- `.impeccable/surfaces/` owns focused route and workflow briefs. These briefs carry
  task hierarchy, responsive behavior, material states, and local anti-goals without
  duplicating global product or design truth.
- [UX terminology and status language](ux-terminology.md) owns operator-facing words
  whose distinctions affect evidence or action safety.

When these sources disagree with shipped behavior, application code remains executable
truth and the stale authority must be refreshed deliberately rather than ignored.

## Starting work

For an established interface, run the context loader named by the skill before editing.
It reports whether product or design context exists and preserves the incumbent
implementation during bounded refinement.

Use `shape` before a substantial new surface or interaction flow. Use `audit` and
`polish` as bounded readiness passes. Detector findings are evidence to classify, not
instructions to follow without checking project purpose, standards, framework output,
and the rendered result.

Genesis does not prefill `PRODUCT.md`, `DESIGN.md`, or project runtime state. Those
files must record actual product truth with the user rather than template assumptions.

## Optional automation

The scaffold controls three independent layers:

- `IncludeImpeccable` includes the skill, agents, this guide, and scoped workflow
  instruction. It defaults on for supported web UI templates.
- `IncludeImpeccableHook` adds the shared GitHub Copilot post-edit hook. It defaults
  off because every relevant edit incurs detector latency and may surface subjective
  findings.
- `IncludeImpeccableAudit` adds a non-required pull-request workflow that audits
  changed UI source with the bundled detector. Findings are advisory; missing,
  malformed, or incomplete execution fails the audit job.

Hook consent and private ignores belong in the gitignored
`.impeccable/config.local.json`. Shared detector policy belongs in
`.impeccable/config.json` when the hook component is selected.

## Network and privacy

Context loading, critique, audit, polish, and static detection operate locally.
Concept generation can contact `impeccable.style` for selection catalogs and reference
images. The evaluated requests sent selection metadata rather than repository files or
product/design documents. Optional choice telemetry honors `DO_NOT_TRACK` and
`IMPECCABLE_NO_TELEMETRY`.

This repository disables the vendored skill's independent update check through
`.impeccable/config.json`. Impeccable upgrades come only from the pinned Genesis
component so an agent never replaces the reviewed payload with `npx impeccable
update`.

Offline operation degrades concept-selection and reference-image features rather than
blocking local context, critique, audit, polish, or static detection.
