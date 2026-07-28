# Agent Instructions

## Behavior

- Be unbiased. Do not optimize for agreement.
- When weighing options, always do a pros/cons analysis.
- Always compare the main plausible paths and explain tradeoffs.
- Do not blindly agree with the user; compare and contrast alternatives fairly.
- State uncertainty explicitly.
- Distinguish verified facts from assumptions.

### Coding Behavior

- Do NOT rely on your training data for latest language and tech stack versions. Research with web searches.
- Back important claims with concrete evidence from code, tests, outputs, docs, or measurements.

### Research Behavior

- Run multiple parallel sub agents to collect data.
- Analyze the results to form a consensus to present to the user.
- Back up any claims with concrete evidence and citations.

## Project Overview

Pitcrew Dashboard is an optional read-only-by-default fleet control plane for
Pitcrew GitHub Actions runner pools. It supports a loopback-only local
deployment, outbound-only connectors reporting multiple remote servers, and an
opt-in typed capacity operation for host-installed connectors.

## Architecture

- ASP.NET Core and Carter expose connector and fleet APIs through Needlr.
- React is built into the ASP.NET image and served from the same origin.
- SQLite is accessed through `IFleetStore`; the adapter is single-replica.
- `PitCrew.Protocol` is a Needlr-free source-generated JSON contract assembly.
- One connector process reads a Pitcrew state root and calls outward. Container
  deployments remain read-only; opt-in host-service mode may invoke Pitcrew's
  setup script for typed capacity operations.
- Connector identity comes from node credentials, never request payload fields.

## Conventions

- Keep manager observations credential-free and versioned.
- Keep the connector free of the Docker socket and never serialize, transmit,
  or log GitHub runner credentials. Host operator mode delegates credential
  reuse to the local Pitcrew setup process.
- Do not introduce PostgreSQL, brokers, caches, or remote commands without a
  measured requirement and an explicit architecture decision.
- Treat dashboard and connector image size and idle footprint as release gates.
- Use domain-specific storage interfaces rather than generic repositories.

Stack-specific conventions and the exact build/test/lint commands are provided as
path-scoped instructions under `.github/instructions/` and load automatically for the
files they match (for example, C# error-handling rules on `*.cs`, the npm quality gate on
`package.json` / `*.ts`). Consult them when working in a given stack.

## Pull Request Delivery

- Local commits are unrestricted checkpoints. Push only feature branches;
  direct updates or deletion of `main` are blocked by `.githooks/pre-push`.
- Run targeted checks while iterating. Before final validation, inspect the
  actual CI runner routing and validate locally enough to avoid repeated
  hosted-CI failures.
- Agent-initiated PRs default to draft. "Open a PR" and "publish a PR" mean
  ready for review; "open a draft PR" and "open a PR so I can review" mean
  draft.
- Genesis drafts run the frontend and analyzed .NET build subset and publish
  `Draft CI`. Moving a PR to ready starts fresh full validation and publishes
  the required `CI` and `Container image` checks.
- Ready Copilot-authored PRs require one trusted human approval on the current
  SHA when `GENESIS_REVIEW_POLICY=copilot-one-approval`.
- Public external fork workflows from all contributors require explicit
  maintainer approval before they run. Approval authorizes the complete
  proposed workflow, including any runner selection.
- Native merges use GitHub's branch auto-delete setting. The inactive private
  workflow-run template is retained only for repositories that cannot use
  protected native delivery.

Before opening a ready PR, publishing a draft, or pushing more commits to an
already-ready PR:

1. Confirm the PR title follows conventional commit semantics.
2. Record validation evidence and assess omitted behavior, implementation gaps,
   failing or missing tests, technical debt, missing coverage, weak assertions,
   and assumptions.
3. Fix every high-severity issue or keep the PR in draft. Disclose remaining
   medium- and low-severity findings in the PR body.

## Out of Scope

- Remote operations other than the typed existing-profile capacity maximum;
  arbitrary node commands remain prohibited.
- Shipping workflow logs or workload data.
- Horizontal dashboard replicas while SQLite is the active adapter.
- Adding dashboard dependencies or image pulls to normal Pitcrew setup.
