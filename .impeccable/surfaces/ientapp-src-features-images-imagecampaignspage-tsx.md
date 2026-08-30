---
version: 1
status: "shipped"
slug: "ientapp-src-features-images-imagecampaignspage-tsx"
primary_target: "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCampaignsPage.tsx"
related_targets: ["src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCampaignDetail.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCampaignAuthorization.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCampaignMutationAuthorization.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCampaignTargetList.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/imageCampaignApi.ts","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/imageCampaignView.ts","src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageCampaignsPage.test.tsx","src/PitCrew.Dashboard.WebApi/ClientApp/e2e/image-campaigns.spec.ts"]
---

## Scope and mode

One ready registry candidate planned across one frozen tenant fleet, then applied
through explicit canary and wave approvals. The surface consumes candidate evidence
from Runner images and profile execution from typed rollout commands. It does not add
dynamic targeting, label authorization, automatic wave progression, automatic retry,
automatic rollback, job cancellation, registry fields, paths, or command controls.
Visitor mode: Operate.

## Audience and job

A tenant administrator needs to see exactly which node/profile targets are eligible
or excluded, freeze that plan, choose one canary and a bounded wave size, approve each
step, and follow mixed per-target convergence without reconstructing the campaign from
individual profile pages. Viewers need the same immutable plan and progress without
mutation controls.

## Hierarchy and interaction

- Keep the shared Image readiness summary and stable Candidates, Campaigns, and
  Recipes task navigation above the campaign task.
- Lead with attention-ordered campaign rows: blocked, partial, indeterminate, paused,
  awaiting approval, running, then complete history.
- Open one URL-stable campaign detail. A missing campaign never substitutes another.
- For a draft, make the frozen plan the focal evidence: candidate identity, target-set
  hash, eligible targets, and every visible exclusion reason before canary selection.
- Keep canary and wave configuration beside unchanged authority. Configuration is
  one-time; a mistaken plan is cancelled and recreated rather than silently rewritten.
- During execution, lead with the current approval gate or active wave, then mixed
  target progress. Distinguish queued, claimed, applying, rolling, complete, blocked,
  failed, indeterminate, and cancelled.
- Confirmation keeps campaign and wave identity, target count and hash, candidate or
  per-target rollback authority, effects, prohibited effects, idempotency identity,
  and acknowledgement together.
- Pause, resume, and cancel copy states that existing profile commands continue.
  Rollback opens a new draft campaign and never appears as an automatic recovery.
- Link candidate qualification to Candidates and individual target evidence to the
  owning profile route rather than duplicating complete records.

## Responsive behavior and states

At wide widths, campaign rows and the focused detail form a bounded list/detail
workspace. At narrow widths and 200% zoom, rows precede the selected detail in DOM
order; target records become compact stacked rows rather than a squeezed table.
Excluded targets remain visible through a dedicated section, never a hidden count.

Cover no candidates, no campaigns, target-limit rejection, draft, blocked with zero
eligible targets, awaiting canary approval, running canary, awaiting later wave,
running wave, paused, complete, partial, cancelled, rollback draft, mixed target
states, unavailable prior authority, viewer/administrator roles, retained refresh
failure, failed mutations, long names and digests, both themes, keyboard confirmation
and focus return, forced colors, reduced motion, CJK, emoji, and RTL.

## Direction and anti-goals

Direction contract:

- **THESIS:** One frozen rollout ledger replaces the category-default deployment
  wizard and never hides exclusions or mutable authority behind progress theater.
- **OWN-WORLD:** The established Pit Wall operating board uses restrained tonal
  surfaces, status typography, attention-ranked rows, exact evidence, and protected
  confirmation summaries.
- **STORY:** The operator proves candidate authority, freezes the fleet, grants one
  bounded wave, and follows convergence or adverse evidence without reconstructing
  state from profile pages.
- **FIRST VIEWPORT:** Shared readiness and task navigation lead into campaign
  planning, an attention-ranked campaign queue, and one focused frozen detail with
  target-set identity before any host authority.
- **FORM:** Qualification-board extension inside the approved Runner Images world.
  This bounded task did not replace the visual world, so it inherits the parent
  surface direction rather than claiming a separate concept-roll seed.
- **FINISH:** unreviewed and undocumented is unfinished; this build ends with the
  finish review, the verdict, and DESIGN.md.

Direction: frozen rollout board. The memorable moment is an administrator seeing one
immutable candidate, one exact target-set hash, one named canary, and every exclusion
in the same frame before granting any host authority. During execution, the board
reads as a controlled handoff from canary to approved waves, with current and stale
workers remaining explicit per target.

Avoid a generic deployment wizard, progress theater, equal-weight metric cards,
label-driven audience builders, hidden exclusions, dynamic target counts, optimistic
completion, dense desktop tables on mobile, automatic next-wave controls, automatic
rollback, or any field that resembles a registry, command, path, or credential.
