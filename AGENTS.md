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

Pitcrew Dashboard is an optional read-only fleet control plane for Pitcrew
GitHub Actions runner pools. It supports a loopback-only local deployment and
outbound-only connectors reporting multiple remote servers to one dashboard.

## Architecture

- ASP.NET Core and Carter expose connector and fleet APIs through Needlr.
- React is built into the ASP.NET image and served from the same origin.
- SQLite is accessed through `IFleetStore`; the adapter is single-replica.
- `PitCrew.Protocol` is a Needlr-free source-generated JSON contract assembly.
- One connector process reads a Pitcrew state root read-only and calls outward.
- Connector identity comes from node credentials, never request payload fields.

## Conventions

- Keep manager observations credential-free and versioned.
- Keep the connector free of Docker socket and GitHub runner credentials.
- Do not introduce PostgreSQL, brokers, caches, or remote commands without a
  measured requirement and an explicit architecture decision.
- Treat dashboard and connector image size and idle footprint as release gates.
- Use domain-specific storage interfaces rather than generic repositories.

Stack-specific conventions and the exact build/test/lint commands are provided as
path-scoped instructions under `.github/instructions/` and load automatically for the
files they match (for example, C# error-handling rules on `*.cs`, the npm quality gate on
`package.json` / `*.ts`). Consult them when working in a given stack.

## Commit Workflow

Before every `git commit`, complete this procedure:

1. **Build and test.** Run the build and tests for this project's stack(s) and record the exact output — pass/fail/skip and warning/error counts. The exact commands (and any stack-specific caveats) live in the path-scoped instructions under `.github/instructions/`.
2. **Self-assess.** Write an honest one-line note for each — HIGH: omitted behavior, implementation gaps, test results; MEDIUM: tech debt, missing coverage, weak assertions; LOW: assumptions.
3. **Share and gate.** Share the self-assessment with the user. Fix any HIGH issue before committing; for any MEDIUM issue, stop and get the user's acknowledgment before proceeding.
4. **Commit.** The pre-commit hook blocks the first attempt by design. Acknowledge and commit:

   ```sh
   GENESIS_PRECOMMIT_ACK=true git commit -m "type: description"
   ```

   On Windows (PowerShell): set `$env:GENESIS_PRECOMMIT_ACK = "true"`, then run `git commit`.
5. **Share evidence.** After the commit succeeds, report exact test counts, what they verified, build warning/error counts, and files changed. Do not say "all tests pass" — show the numbers.

## Out of Scope

- Remote runner-capacity changes or arbitrary node commands.
- Shipping workflow logs or workload data.
- Horizontal dashboard replicas while SQLite is the active adapter.
- Adding dashboard dependencies or image pulls to normal Pitcrew setup.
