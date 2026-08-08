/**
 * Shared typography roles grounded in DESIGN.md's Hierarchy section. Body and
 * Control already match Tailwind's default `text-sm` size/line-height, so
 * they are not duplicated here; primitives compose `text-sm` directly.
 *
 * - `pageTitle` — Display: the single visible page H1 (DESIGN.md "The One
 *   Page Title Rule").
 * - `entityTitle` — Headline: a route-local entity or major section that
 *   does not repeat the page title.
 * - `sectionHeading` — Title: focused subsection headings within a page.
 * - `panelTitle` — Panel Title: card, evidence-panel, and table-region
 *   names (matches CardTitle's default styling).
 * - `body` — Body: the default operational reading size.
 * - `label` — Label: table headings, field labels, and short scan text.
 * - `metadata` — Label size at muted emphasis: secondary metadata that
 *   accompanies a primary value rather than naming it.
 * - `identifier` — Mono: stable identifiers, revisions, and operation
 *   names; wraps rather than overflows its container.
 * - `measurement` — Body size with tabular numerals for capacity, counts,
 *   and other sortable numeric evidence.
 */
export const typography = {
  pageTitle: 'text-page-title text-foreground',
  entityTitle: 'text-entity-title text-foreground',
  sectionHeading: 'text-section-heading text-foreground',
  panelTitle: 'text-panel-title text-foreground',
  body: 'text-sm text-foreground',
  label: 'text-label text-foreground',
  metadata: 'text-label text-muted-foreground',
  identifier: 'font-mono text-identifier text-muted-foreground break-all',
  measurement: 'text-sm text-foreground tabular-nums',
} as const;

export type TypographyRole = keyof typeof typography;
