---
version: 1
slug: "api-clientapp-src-features-images-imageworkspace-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageWorkspace.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCandidatesPage.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCandidateDetail.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageRecipesPage.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCampaignsPage.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCampaignDetail.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/manifest.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ProfileImageRollout.tsx"]
---

## Scope and mode

Future runner-image candidate, trusted recipe, single-profile rollout, and fleet
campaign workflows backed only by capabilities delivered through issues #150, #151,
and #152. Visitor mode: Operate.

## Audience and job

An administrator qualifies one immutable runner image, proves why it is ready or
blocked, and deliberately moves that exact digest into one profile or a frozen fleet
campaign without guessing about eligibility, widening authority, or losing
attribution.

## Hierarchy and interaction

- Lead with capability/readiness and active exceptions before complete candidate or
  target inventory.
- Keep stable tasks for candidates, campaigns, and lower-frequency recipe
  administration.
- Order candidate and campaign rows by blocked/failed/indeterminate state, active
  work, ready/completed evidence, then ordinary history.
- Open one URL-stable request, candidate, rollout, or campaign detail. Missing records
  show recovery and never substitute another identity.
- Candidate detail keeps immutable source, run, artifact, digest, and qualification
  proof together; raw logs remain linked and external.
- Rollout confirmation keeps exact target, current/target digest, capability, fences,
  effects, preserved invariants, prohibited effects, and acknowledgement together.
- Campaign planning freezes eligible and excluded targets before approval. Canary and
  later waves remain explicit, attributable, and deep-linkable.

## Responsive behavior and states

At narrow widths, use readiness, task navigation, scan-first rows, and focused detail;
do not shrink campaign tables into the viewport. Pin selected records beyond bounded
mobile windows. Cover every lifecycle and authorization state in
`docs/ui/runner-image-workspaces.md`, including retryable GitHub unavailability,
offline/stale/incompatible/excluded targets, indeterminate operations, large
candidate/target sets, long immutable identities, both themes, zoom, forced colors,
reduced motion, CJK, emoji, and RTL.

## Direction and anti-goals

Direction: qualification board. One immutable digest keeps the same identity as it
moves from bounded build proof into an explicitly frozen rollout plan. The memorable
moment is eligibility without disappearance: every excluded target stays visible with
its bounded reason. Avoid generic workflow dashboards, equal-weight status-card
walls, hidden exclusions, label-only authority, raw logs, inferred success,
automatic widening, automatic rollback, and command-shaped controls.
