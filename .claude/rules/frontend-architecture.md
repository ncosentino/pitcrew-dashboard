---
# AUTO-GENERATED from .github/instructions/frontend-architecture.instructions.md — do not edit
paths:
  - "src/PitCrew.Dashboard.WebApi/ClientApp/{package.json,src/**/*.{ts,tsx},scripts/**/*.mjs}"
---
# Dashboard frontend architecture

- Keep features below `src/features/<feature-id>/`. Features may import shared core
  code but never another feature's internals.
- `src/features.registry.ts` is the only production composition boundary that imports
  feature internals.
- Keep one tenant-scoped fleet polling loop in `FleetProvider`. Feature pages consume
  that projection and request a shared refresh after typed mutations.
- Keep browser authorization as presentation logic only. Server APIs remain the final
  tenant and role authority.
- Consequential operations require accessible confirmation with current identity,
  fences, expected effects, prohibited effects, and explicit acknowledgement.
- Represent loading, error, empty, offline, unavailable, and stale evidence
  truthfully. Do not render missing measurements as zero or acknowledged incidents as
  resolved.
- Preserve keyboard access, visible focus, semantic landmarks, textual equivalents
  for charts, and responsive behavior for long operational identifiers.
- Use the project-local Impeccable skill for substantial UI shaping, audit, polish,
  and design documentation. Detector findings remain evidence to review, not automatic
  implementation requirements.
- Preserve the complete frontend gate owned by `package.json`: build, lint,
  formatting, feature-boundary checks, and tests.
- Run Impeccable context loader before editing established UI surfaces; use `shape` for new surfaces or materially changed flows.
- Use only approved shared primitives and design tokens.
- Cover all relevant states and viewports (320, 390, 768, 1280, 1440 CSS px).
- Never use a raw identifier as a title when a human-readable display name exists.
- Never introduce a new table without a documented narrow-screen strategy.
- Never add a consequential action without confirmation or approved reversibility.
- Primary workflows must work with keyboard only, at 200% zoom, under forced-colors,
  and with `prefers-reduced-motion`. Long content (40% expansion), CJK, emoji, and
  RTL must not break containment.
- Performance budget: DOMContentLoaded <=1500ms, load <=3000ms, and production
  JavaScript transfer <=550KB (the measured 501,736-byte baseline plus headroom).
  Paginate or virtualize above 100 fleet nodes.

See [Frontend architecture](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/frontend-architecture.md)
and [Impeccable design workflow](https://github.com/ncosentino/pitcrew-dashboard/blob/main/docs/impeccable-design.md).
