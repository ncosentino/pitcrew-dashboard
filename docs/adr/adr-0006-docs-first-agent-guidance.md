---
title: "ADR-0006: Docs-first agent guidance with managed Genesis defaults"
status: "Accepted"
date: "2026-08-08"
authors: ["Nick Cosentino"]
tags: ["architecture", "documentation", "agents", "design"]
supersedes: ""
superseded_by: ""
---

# Context and scope

PitCrew Dashboard combines ASP.NET Core, Carter, Needlr, SQLite, a React/Vite
frontend, two container images, outbound connectors, typed host operations, and
versioned protocol compatibility. Its root agent file had grown to 95 lines and
4,623 UTF-8 bytes while repeating architecture, workflow, and delivery rules already
owned by documentation, accepted ADRs, tests, manifests, and CI.

The repository was generated from the Genesis `aspnet-webapi` template with an
embedded `react-vite-ts` frontend. Its Genesis-managed instruction tree had drifted
from the current explicit template shape: 96 files loaded as much as 996 lines and
45,799 bytes for a representative SQLite test. The current generated shape reduces
that population to 557 lines and 25,678 bytes, below the 600-line and 32-KiB hard
ceiling but still above the preferred target.

The repository also had generated Claude mirrors but no repository-local review
skill, documentation map, guidance contract, or structural guidance gate. The
current Genesis template provides a pinned project-local Impeccable design skill and
four supporting agents for web UI work.

Verified facts:

- `.genesis/applied-components.json` records the `aspnet-webapi` template.
- `.genesis/applied-frontends.json` records the embedded React frontend and its
  feature-plugin, HTTP-client, and shadcn components.
- Existing accepted ADRs govern connector operations, workload attribution,
  admission pause, and connector-health replay.
- `.github/genesis-delivery.json` and the owning workflows govern pull-request
  delivery.

# Decision drivers

- Keep always-loaded guidance bounded.
- Preserve Dashboard-specific security, protocol, persistence, and deployment
  boundaries.
- Retain a safely replaceable Genesis-managed instruction layer.
- Keep local specialization outside the managed subtree.
- Give frontend work the current Genesis-supported Impeccable design workflow.
- Preserve one source for Copilot instructions and generate Claude mirrors from it.
- Derive review and validation from executable repository sources.
- Make temporary context exceptions explicit and mechanically bounded.

# Decision

PitCrew Dashboard adopts a layered, docs-first guidance architecture.

## Root and documentation

`AGENTS.md` contains only project identity, trusted-source routing, exceptional
cross-cutting safeguards, and delivery routing. It remains below 60 lines and 3,072
UTF-8 bytes.

`docs/README.md` is the canonical documentation map. Existing project documentation
and ADRs remain authoritative and are indexed rather than replaced by generic pages.
Accepted ADR reasoning remains immutable.

## Managed and project-owned instructions

`.github/instructions/genesis/` is regenerated only from the explicit
`aspnet-webapi` plus React template and symbol shape. Local rules live in separate
project-owned instruction files and never modify the managed subtree.

Representative C# tests, production C# files, and frontend feature components retain
temporary migration exceptions above the preferred 300-line or 16-KiB target. Every
exception remains below the hard ceiling, records an owner and reason, and is
re-measured when Genesis guidance changes.

## Review and structural validation

The repository-local `review-changes` skill resolves the actual diff, applicable
instructions, documentation and ADR context, package and project manifests,
workflows, delivery metadata, and hosted evidence. It owns procedure rather than a
second standards corpus.

Repository-owned PowerShell validation enforces root budgets, documentation
reachability, ADR lifecycle, instruction metadata and context limits, review wiring,
Impeccable surfaces, and generated mirror provenance. The existing CI build job runs
that inexpensive gate.

## Impeccable

The Genesis-provided Impeccable design skill, four agents, workflow guidance, and UX
documentation are installed as project-owned guidance. The post-edit hook and advisory
pull-request audit remain disabled. They add latency or CI execution to every relevant
change and are not required for the design workflow.

## Claude mirrors

`.github/instructions/**/*.instructions.md` remains the source of truth.
`.claude/rules/` is regenerated through the Genesis
Copilot-to-Claude compiler and is never edited manually.

# Alternatives considered

## Keep the current root and managed tree

This avoids migration but leaves the root over budget, no docs map or review skill,
and a representative instruction stack above the hard context ceiling.

## Replace managed guidance with only project-owned files

This would permit exact local tuning but fork reusable Genesis guidance and lose safe
managed refreshes. It is rejected because the repository has trustworthy template and
frontend provenance.

## Defer the managed refresh until every Genesis instruction is ideal

This avoids a later second refresh but retains the current hard-ceiling violation and
blocks supported Impeccable installation. The current exact shape is a material
improvement and can be refreshed again after upstream guidance evolves.

## Copy only the Impeccable payload

Manual copying would bypass the Genesis migration ownership and backup contract for a
large vendored surface. The supported generated-guidance migration is used instead.

## Enable the Impeccable hook and audit immediately

Both are advisory and opt-in. Their additional edit latency and CI execution are not
required to gain the skill, agents, detector, and documented design workflow, so they
remain disabled.

# Consequences

## Positive

- Root context becomes bounded.
- The managed instruction stack falls below its hard ceiling.
- Existing Dashboard architecture and ADRs remain authoritative.
- Frontend work gains pinned Impeccable design and review tooling.
- Copilot and Claude consume one instruction source.
- Guidance drift becomes deterministic CI evidence.

## Negative

- Some managed C# and frontend populations remain above the preferred context target.
- The repository owns tailored review and structural validation files that future
  Genesis migrations must explicitly adopt.
- The large vendored Impeccable payload becomes a maintained project surface.
- Managed guidance will need another measured refresh after upstream improvements.

# Confirmation

The decision is confirmed when:

- root and redirect budgets pass;
- every maintained document is reachable from `docs/README.md`;
- all accepted ADRs appear in the ADR index;
- managed and project-owned instructions satisfy their declared limits or approved
  exceptions;
- the Impeccable skill and four agents are present;
- Claude mirrors correspond one-to-one with Copilot instructions;
- targeted guidance tests pass locally;
- complete build, frontend, test, installer, and container evidence remains with the
  configured pull-request workflows.

# References

- `.genesis/applied-components.json` and `.genesis/applied-frontends.json` establish
  the exact generated repository shape.
- `.github/genesis-delivery.json` identifies the required checks and draft/full
  validation behavior that review must preserve.
- Existing ADRs 0001 through 0005 define the security and operating boundaries this
  guidance architecture routes rather than restates.
- Genesis issue
  [#408](https://github.com/ncosentino/genesis/issues/408) tracks capability-aware
  refinements to reusable managed instructions.
