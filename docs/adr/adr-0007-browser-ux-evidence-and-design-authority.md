---
title: "ADR-0007: Browser UX evidence and durable design authority"
status: "Accepted"
date: "2026-08-08"
authors: ["Nick Cosentino"]
tags: ["architecture", "frontend", "testing", "accessibility", "agents", "design"]
supersedes: ""
superseded_by: ""
---

# Context and scope

PitCrew Dashboard has strong application, feature-boundary, lint, format,
type-check, and unit-test gates. Its project-owned UX instructions also require
semantic headings, responsive containment, accessible state, reduced motion, and
confirmation for consequential actions.

The current frontend nevertheless contains failures those prose and unit-test
surfaces did not prevent: document-level overflow on dense routes, shared color
pairings below WCAG AA, duplicate or identifier-led page hierarchy, visual section
titles without semantic headings, and inconsistent confirmation for diagnostic
credential operations.

The repository includes the project-local Impeccable workflow, but its detector is
advisory and can report legitimate table headings, compact data cells, or tab
underlines as anti-patterns. Subjective detector output therefore cannot be the
merge authority.

This decision governs durable product and design authority, browser-level UX
evidence, deterministic merge gates, and the role of agent judgment for substantial
web UI changes. It does not choose page-specific compositions, add product
capabilities, replace the frontend stack, or make AI-generated critique scores a
required check.

# Decision drivers

- Make recurring UX requirements executable instead of relying on prose alone.
- Preserve the product's evidence-first, read-only-by-default operating model.
- Detect responsive, semantic, theme, focus, and interaction regressions in rendered
  routes.
- Keep fixtures credential-free, sanitized, deterministic, and compatible with
  production schemas.
- Give agents durable product and design authority before they generate UI.
- Separate deterministic failures from advisory design judgment.
- Keep complete browser work on configured CI rather than normal workstation loops.
- Avoid a hosted visual-testing service or another external repository trust
  boundary.

# Decision

`PRODUCT.md` owns durable product truth. `DESIGN.md` and its
`.impeccable/design.json` sidecar own the reusable visual system. Focused
Impeccable surface briefs own route-specific task, hierarchy, responsive, state, and
anti-goal decisions. Application code remains executable truth when a documented
token or component no longer matches the shipped implementation, and the design
authority must then be refreshed deliberately.

The frontend will add a repository-owned browser evidence harness using Playwright
and axe-core as development-only dependencies. The harness will serve the actual
React application against sanitized API fixtures validated by the same runtime
schemas used by production clients.

The representative route matrix will cover authenticated and unauthenticated
surfaces, material roles and permissions, light and dark themes, narrow and wide
viewports, loading and failure states, stale or unavailable evidence, active
incidents, and consequential operations.

The required deterministic UX boundary will cover:

- frontend build, lint, formatting, feature boundaries, and tests;
- serious or critical automated accessibility findings;
- document-level horizontal overflow;
- one descriptive H1 and the declared heading relationships;
- accessible names and keyboard behavior for controls, dialogs, and route focus;
- required viewport, theme, and state evidence artifacts.

Screenshot differences, Impeccable detector findings, finish-reviewer findings, and
critique scores remain review evidence. They require classification and disclosure
but do not fail CI merely because a subjective warning or pixel difference exists.
Local Impeccable post-edit hooks remain opt-in.

Substantial UI work must load Impeccable context, follow the matching approved
surface brief, reuse or deliberately extend the shared system, run the affected
browser matrix, classify detector findings, complete the Impeccable finish review,
and run the repository `review-changes` procedure before delivery.

# Alternatives considered

## Rely on instructions and unit tests

This has low tooling cost and keeps the current CI fast. It is rejected as the sole
boundary because the existing instructions already name responsive containment,
heading, motion, and confirmation requirements that the rendered application still
violates.

## Make the Impeccable detector required

The detector is inexpensive and catches useful implementation smells. It is rejected
as merge authority because verified findings include false positives for tab
underlines, table headings, compact data cells, and a single workhorse UI font that
is appropriate for Operate mode.

## Require manually attached screenshots only

Manual screenshots are useful review evidence and make visual intent inspectable.
They are insufficient alone because they do not assert overflow, accessibility
relationships, keyboard behavior, data-contract validity, or missing state coverage.

## Maintain a custom Chrome DevTools harness

A small Chrome DevTools Protocol script avoids a new package dependency. It is
rejected as the durable harness because module resolution, browser discovery,
network interception, multi-browser behavior, and screenshot orchestration become
repository-specific maintenance.

## Use a hosted visual regression service

A hosted service could provide polished baselines and review tools. It is rejected
for now because it adds cost, external availability, uploaded artifacts, credentials,
and another trust boundary before the repository has exhausted local CI evidence.

# Consequences

## Positive

- Product and design intent become explicit inputs to agent work.
- Browser-only failures become deterministic before merge.
- Sanitized fixtures make material states repeatable without a live connector or
  GitHub OAuth.
- Accessibility and responsive evidence become part of normal frontend quality.
- Advisory design tools remain useful without allowing false positives to block
  delivery.
- Future features inherit shared tokens, components, states, and review expectations.

## Negative

- Playwright browsers and axe-core add development dependencies, CI download cost,
  and maintenance.
- Route fixtures must evolve with production schemas.
- Screenshot and browser tests can become brittle if selectors and assertions target
  incidental presentation rather than user-visible contracts.
- Required browser evidence increases pull-request execution time.
- Human or agent judgment remains necessary for hierarchy, cognitive load, tone, and
  product specificity.

## Neutral constraints

- Browser evidence does not replace unit, integration, protocol, or authorization
  tests.
- Critique scores remain milestone evidence rather than deterministic quality metrics.
- Physical-device and screen-reader checks remain release evidence where browser
  automation is not equivalent.

# Confirmation

The decision is confirmed when:

- Impeccable context resolves `PRODUCT.md`, `DESIGN.md`, and the matching surface
  brief for representative frontend routes;
- the browser harness validates sanitized fixtures through production schemas;
- the route matrix produces desktop, narrow-screen, light, dark, accessibility, and
  overflow evidence;
- deterministic failures are required by the frontend pull-request gate;
- detector and screenshot findings are reported as classified advisory evidence;
- substantial UI delivery requires finish review and `review-changes`; and
- guidance contract tests preserve the authority, instruction, review, and generated
  mirror relationships.

# References

- ADR-0006 establishes the docs-first, project-owned guidance layer, the Impeccable
  workflow, and generated Claude mirrors that this decision refines.
- `.github/actions/frontend-gate/action.yml` demonstrates that the current required
  frontend boundary owns build and test execution but no rendered browser evidence.
- `.github/instructions/frontend-architecture.instructions.md` and
  `.github/instructions/web-ux-resilience.instructions.md` demonstrate that
  consequential-action, heading, responsive, state, and motion requirements already
  exist as prose.
- Issue #82 owns the professional-grade UX roadmap. Issues #83 and #84 own design
  authority and browser evidence implementation respectively.
