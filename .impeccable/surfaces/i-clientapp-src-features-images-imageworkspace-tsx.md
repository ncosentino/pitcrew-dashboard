---
version: 1
status: "shipped"
slug: "i-clientapp-src-features-images-imageworkspace-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageWorkspace.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCandidatesPage.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCandidateDetail.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageBuildRequestForm.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageRecipesPage.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageRecipeRegistrationForm.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/imageWorkspaceContext.ts","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/imagesApi.ts","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/manifest.tsx"]
---

## Scope and mode

Runner-image build requests, immutable ready/failed candidates, qualification
evidence, frozen rollout campaigns, and trusted recipe registration backed by issues
#150 and #152. Single-profile rollout remains on its owning profile route under issue
#151. Visitor mode: Operate.

## Audience and job

An operator follows one trusted request from exact source authority through workflow
execution and qualification to immutable ready or failed evidence. Administrators
register reviewed recipes and request builds without gaining arbitrary workflow
dispatch authority.

## Hierarchy and interaction

- Lead with capability/readiness and active exceptions before complete candidate or
  target inventory.
- Keep stable tasks for candidate activity, frozen rollout campaigns, and
  lower-frequency recipe administration.
- Order request rows by blocked/failed state, active work, ready evidence, then
  ordinary history.
- Open one URL-stable request/candidate or recipe detail. Missing records show
  recovery and never substitute another identity.
- Candidate detail keeps immutable source, run, artifact, digest, and qualification
  proof together; raw logs remain linked and external.
- Build confirmation keeps exact source, registration version, workflow blob, effects,
  prohibited effects, idempotency identity, and acknowledgement together.
- Recipe registration and disablement remain secondary administration with exact
  GitHub identities and preserved audit history.
- Campaign rows lead blocked, partial, paused, awaiting-approval, and running work
  before complete history; one focused campaign keeps frozen targets, exclusions,
  canary/waves, progress, and explicit rollback planning together.

## Responsive behavior and states

At narrow widths, use readiness, task navigation, scan-first rows, and focused detail.
Cover empty recipes/requests/campaigns, requested, dispatching, building, qualifying,
ready, campaign draft/approval/running/paused/complete/partial/blocked/cancelled,
refresh failure with retained evidence, missing deep links, viewer/administrator
authority, bounded 100-record responses, long immutable identities, both themes,
zoom, forced colors, reduced motion, CJK, emoji, and RTL.

## Direction and anti-goals

Direction: qualification board. One exact source commit keeps its identity through
the request, GitHub run, artifact, report, digest, and qualification rows. The
memorable moment is proof without log archaeology: blocked evidence leads, while a
ready candidate exposes the immutable digest and every qualification in one focused
record. Campaigns extend that board with a frozen target ledger and explicit wave
gates rather than a deployment wizard. Avoid generic workflow dashboards, equal-weight
status-card walls, raw logs, arbitrary dispatch fields, inferred success, dynamic
target widening, automatic rollback, and command-shaped administration.
