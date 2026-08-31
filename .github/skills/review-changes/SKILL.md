---
name: review-changes
description: >
  Review PitCrew Dashboard changes before commit, push, or pull-request delivery
  against applicable instructions, docs and ADRs, repository-declared validation,
  generated mirrors, Impeccable guidance, and existing CI evidence.
---

# Review PitCrew Dashboard changes

This skill owns review procedure, not project standards. Current instructions, docs,
ADRs, schemas, tests, manifests, and workflows remain authoritative.

## Review boundary

- Judge changed lines and their direct invariant blast radius.
- Report pre-existing divergence separately and exclude it from the verdict.
- Do not demand unrelated migration work because docs describe a target state.
- Do not invent findings for a clean diff.
- Review is read-only unless the user explicitly requests fixes.

## 1. Resolve the scope

Confirm the worktree and branch:

```powershell
git rev-parse --show-toplevel
git branch --show-current
git status --short
```

Select scope in this order:

1. Explicit refs, pull request, or paths supplied by the user.
2. All uncommitted changes, including individual untracked files.
3. Otherwise `git merge-base origin/main HEAD` through `HEAD`.

Use `git --no-pager diff`, `git --no-pager diff --cached`, and full reads for
untracked files. For a pull request, confirm the actual base and head with
`gh pr view` and `gh pr diff`.

State the selected scope and changed files.

## 2. Resolve governing sources

Read `.github/genesis-guidance.json`, then resolve every applicable instruction from
the changed-path array:

```powershell
$changedPaths = @('<path-1>', '<path-2>')
& scripts/guidance/Get-ApplicableInstructions.ps1 -Path $changedPaths
```

Read every returned instruction in full. Follow relevant links from
`docs/README.md`, the ADR index, changed docs, and matching instructions.

Genesis-managed instructions are defaults. Project-owned instructions and accepted
Dashboard ADRs specialize them. Never edit `.github/instructions/genesis/` or
`.claude/rules/generated/` directly.

For frontend design or UX changes, read `docs/ux-design.md`,
`docs/impeccable-design.md`, and the project-local Impeccable skill. Use Impeccable
only within the user's approved design scope.

## 3. Resolve validation

Inventory declared validation and build surfaces:

```powershell
& scripts/guidance/Get-ValidationInventory.ps1
```

Inspect the actual package scripts, solution and project files, reusable actions,
workflows, and delivery metadata before choosing commands.

- Run the smallest offline command that covers the changed behavior.
- Use focused .NET projects or tests and the frontend's declared package scripts.
- A focused pass does not supersede a disclosed full-suite failure. For timing-related
  failures, require a root-cause fix plus repeated evidence under the declared parallel
  command; do not accept a timeout increase or green rerun as the fix.
- Run `tests/Test-Guidance.ps1` for guidance, docs-map, ADR, Impeccable, or mirror
  changes.
- Do not invent a command or tool absent from repository code, docs, manifests, or
  workflows.
- Do not run complete test matrices, installer mutation tests, multi-architecture
  containers, live OAuth, browser, or host scenarios on a workstation.
- Pull-request CI owns complete frontend, analyzed build, test batching, Linux and
  Windows installer, and container evidence.
- For a pull request, inspect `gh pr checks` instead of reproducing heavy work.

Record commands, results, and required checks that were not run.

## 4. Review what gates do not prove

Read every changed file and inspect:

- outbound-only connector communication and socketless container mode;
- credential-derived node identity and tenant authorization;
- typed operation boundaries, local policy, fencing, expiry, and idempotency;
- at-most-once recovery and shared operation exclusion;
- protocol-version compatibility and unavailable-versus-zero semantics;
- SQLite single-replica, transaction, retention, and bounded-history contracts;
- feature-plugin imports, shared polling ownership, routes, accessibility, and stale
  response handling;
- container non-root execution, final-image contents, Compose ingress coordination,
  and release tags;
- delivery trust boundaries, required checks, and untrusted workflow execution;
- docs-map ownership, accepted ADR lifecycle, and generated mirror provenance;
- credentials, private host details, job output, untrusted inputs, and destructive
  actions.

Use the governing source for each exact rule. These categories are prompts, not a
second standards corpus.

## 5. Reflect on guidance

Recommend a guidance change only for one material misstep or repeated evidence of the
same avoidable mistake. The lesson must generalize and have the correct owner.

Prefer executable contracts for deterministic behavior, instructions for recurring
exact rules, docs for rationale, skills for procedures, and `AGENTS.md` only for
safeguards needed before any file is selected.

Review remains read-only. Report no guidance change when that threshold is not met.

## 6. Report

Open with:

- `Scope:` reviewed range or paths
- `Verdict:` `Approve`, `Approve with nits`, or `Request changes`
- `Validation:` observed, passed, failed, and not-run evidence
- `Guidance reflection:` `no change warranted` or one evidence-backed candidate

## 7. UI change evidence (when frontend paths are in scope)

When any file under `src/PitCrew.Dashboard.WebApi/ClientApp/src/` is changed:

- **Affected routes and states:** list every route and named state the change touches.
- **Browser results:** reference the latest successful `Browser UX` evidence for the
  reviewed head. State which routes/viewports passed and disclose any code-equivalent
  rather than exact-head evidence.
- **Keyboard and zoom disclosure:** confirm keyboard walkthrough of affected routes,
  or disclose which flows were not keyboard-tested and which viewports were not
  zoom-verified.
- **Localization disclosure:** confirm long-content, 40% expansion, CJK, emoji, and
  RTL containment, or disclose which cases were not exercised.
- **Finish-reviewer output:** when the change is a substantial UI surface or polish
  pass, include or reference the Impeccable finish-reviewer verdict.
- **Generated mirrors:** confirm `.claude/rules/` mirrors match current
  instructions via the copilot-to-claude-compiler, or flag drift.

Group introduced findings by severity:

- **Blocker** - broken behavior, security or destructive risk, failing required
  validation, or violation of an accepted architecture or delivery boundary.
- **Major** - clear correctness or contract defect that should be fixed before merge.
- **Minor** - bounded maintainability, coverage, documentation, or guidance defect.
- **Nit** - optional polish.

Every finding includes:

`severity - file:line - issue - governing source - concrete fix`

If there are no introduced findings, say so plainly. State uncertainty and missing
evidence instead of implying an unrun check passed.
