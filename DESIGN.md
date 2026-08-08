---
name: PitCrew Dashboard
description: "The Pit Wall: calm, exception-first runner-fleet operations."
colors:
  pit-navy: "#071825"
  signal-orange: "#f33919"
  instrument-teal: "#13989f"
  instrument-teal-bright: "#27b7bd"
  instrument-ink: "#0b686e"
  cool-canvas: "#f7fafb"
  white-surface: "#ffffff"
  mist-surface: "#eef4f5"
  instrument-surface: "#e4f5f6"
  quiet-steel: "#536673"
  cool-line: "#d8e2e5"
  deep-console: "#0d2435"
  deep-console-muted: "#153044"
  deep-instrument: "#123c42"
  console-foreground: "#f8fafc"
  console-muted: "#a8bac4"
  console-instrument: "#c9f1f2"
  positive-soft: "oklch(95% 0.052 163.051)"
  positive-ink: "oklch(43.2% 0.095 166.913)"
  caution-soft: "oklch(96.2% 0.059 95.617)"
  caution-ink: "oklch(47.3% 0.137 46.201)"
  critical-soft: "oklch(93.6% 0.032 17.717)"
  critical-ink: "oklch(44.4% 0.177 26.899)"
  critical-action: "oklch(57.7% 0.245 27.325)"
typography:
  display:
    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", "Noto Sans", Arial, sans-serif'
    fontSize: "1.875rem"
    fontWeight: 700
    lineHeight: "2.25rem"
    letterSpacing: "-0.025em"
  headline:
    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", "Noto Sans", Arial, sans-serif'
    fontSize: "1.5rem"
    fontWeight: 700
    lineHeight: "2rem"
    letterSpacing: "-0.025em"
  title:
    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", "Noto Sans", Arial, sans-serif'
    fontSize: "1.25rem"
    fontWeight: 600
    lineHeight: "1.75rem"
    letterSpacing: "normal"
  panel-title:
    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", "Noto Sans", Arial, sans-serif'
    fontSize: "1rem"
    fontWeight: 600
    lineHeight: 1
    letterSpacing: "normal"
  body:
    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", "Noto Sans", Arial, sans-serif'
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: "1.25rem"
    letterSpacing: "normal"
  control:
    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", "Noto Sans", Arial, sans-serif'
    fontSize: "0.875rem"
    fontWeight: 500
    lineHeight: "1.25rem"
    letterSpacing: "normal"
  label:
    fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", "Noto Sans", Arial, sans-serif'
    fontSize: "0.75rem"
    fontWeight: 600
    lineHeight: "1rem"
    letterSpacing: "normal"
  mono:
    fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace'
    fontSize: "0.75rem"
    fontWeight: 400
    lineHeight: "1rem"
    letterSpacing: "normal"
rounded:
  sm: "0.375rem"
  md: "0.5rem"
  lg: "0.625rem"
  xl: "0.875rem"
  full: "9999px"
spacing:
  "1": "0.25rem"
  "1.5": "0.375rem"
  "2": "0.5rem"
  "3": "0.75rem"
  "4": "1rem"
  "5": "1.25rem"
  "6": "1.5rem"
  "8": "2rem"
components:
  button-primary:
    backgroundColor: "{colors.pit-navy}"
    textColor: "{colors.white-surface}"
    typography: "{typography.control}"
    rounded: "{rounded.md}"
    padding: "0.5rem 1rem"
    height: "2.25rem"
  button-outline:
    backgroundColor: "{colors.cool-canvas}"
    textColor: "{colors.pit-navy}"
    typography: "{typography.control}"
    rounded: "{rounded.md}"
    padding: "0.5rem 1rem"
    height: "2.25rem"
  input:
    backgroundColor: "{colors.cool-canvas}"
    textColor: "{colors.pit-navy}"
    typography: "{typography.body}"
    rounded: "{rounded.md}"
    padding: "0 0.75rem"
    height: "2.25rem"
  button-destructive:
    backgroundColor: "{colors.critical-action}"
    textColor: "{colors.white-surface}"
    typography: "{typography.control}"
    rounded: "{rounded.md}"
    padding: "0.5rem 1rem"
    height: "2.25rem"
  card:
    backgroundColor: "{colors.white-surface}"
    textColor: "{colors.pit-navy}"
    rounded: "{rounded.xl}"
    padding: "1.5rem"
  status-positive:
    backgroundColor: "{colors.positive-soft}"
    textColor: "{colors.positive-ink}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    padding: "0.25rem 0.5rem"
    height: "1.5rem"
  status-caution:
    backgroundColor: "{colors.caution-soft}"
    textColor: "{colors.caution-ink}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    padding: "0.25rem 0.5rem"
    height: "1.5rem"
  status-critical:
    backgroundColor: "{colors.critical-soft}"
    textColor: "{colors.critical-ink}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    padding: "0.25rem 0.5rem"
    height: "1.5rem"
  navigation-item-active:
    backgroundColor: "{colors.instrument-surface}"
    textColor: "{colors.instrument-ink}"
    typography: "{typography.control}"
    rounded: "{rounded.md}"
    padding: "0.5rem 0.75rem"
  alert-dialog:
    backgroundColor: "{colors.cool-canvas}"
    textColor: "{colors.pit-navy}"
    rounded: "{rounded.lg}"
    padding: "1.5rem"
    width: "min(32rem, calc(100% - 2rem))"
  sheet:
    backgroundColor: "{colors.cool-canvas}"
    textColor: "{colors.pit-navy}"
    padding: "1.25rem"
    width: "min(20rem, 85vw)"
---

# Design System: PitCrew Dashboard

## Overview

**Creative North Star: "The Pit Wall"**

The Pit Wall turns live runner-fleet evidence into a calm, exception-first operating view. It is precise and calm, compact but breathable, and familiar enough to use under time pressure. Hierarchy, evidence state, and safe action order provide the personality; ornament does not.

Pit Navy, Signal Orange, and Instrument Teal preserve the established mascot and product identity. Cool Canvas and Deep Console create light and dark operating environments through tonal surfaces, precise borders, and restrained ambient color. Brand colors remain distinct from semantic text and action roles so artwork can stay recognizable without weakening WCAG 2.2 AA.

The interface uses one workhorse system UI family, compact controls, explicit status language, and data typography that separates human-readable identity from machine evidence. Operational surfaces lead with material exceptions and focused evidence, then expose inventory and dense tables inside deliberate containment.

**Key Characteristics:**
- Precise, calm, exception-first hierarchy.
- Compact controls with a breathable 4px-based spacing rhythm.
- Restrained navy, orange, and teal identity separated from semantic status roles.
- Light and dark tonal layers with precise borders and earned soft elevation.
- Familiar controls, explicit evidence states, and narrowly safe actions.
- One system UI workhorse family with monospaced identifiers and tabular numbers.

## Colors

The palette behaves like an instrument panel: navy establishes authority, orange marks the brand signal, teal carries instrumentation, and semantic colors communicate operational state.

### Primary
- **Pit Navy** (`colors.pit-navy`): shell identity, high-emphasis foreground, and the accessible light-theme primary action surface.
- **Signal Orange** (`colors.signal-orange`): mascot and logo detail, chart emphasis, and large accent moments; it is not a normal-text or default action color.

### Secondary
- **Instrument Teal** (`colors.instrument-teal`): focus identity, chart series, and brand instrumentation.
- **Instrument Ink** (`colors.instrument-ink`): the darker light-theme teal role for normal text on pale instrument surfaces.
- **Bright Instrument Teal** (`colors.instrument-teal-bright`): dark-theme focus rings and chart lines.

### Tertiary
- **Positive** (`colors.positive-soft`, `colors.positive-ink`): healthy, current, connected, running, and resolved evidence.
- **Caution** (`colors.caution-soft`, `colors.caution-ink`): partial, pending, stale, draining, and acknowledged evidence.
- **Critical** (`colors.critical-soft`, `colors.critical-ink`, `colors.critical-action`): failed, unavailable, blocked, revoked, and destructive actions.

### Neutral
- **Cool Canvas** (`colors.cool-canvas`): the light operating canvas.
- **White Surface** (`colors.white-surface`): raised cards and popovers in the light theme.
- **Mist Surface** (`colors.mist-surface`): secondary and muted control surfaces.
- **Instrument Surface** (`colors.instrument-surface`): selected navigation and low-emphasis teal context.
- **Quiet Steel** (`colors.quiet-steel`): secondary light-theme text.
- **Cool Line** (`colors.cool-line`): borders, inputs, and dividers.
- **Deep Console** (`colors.deep-console`): raised cards and popovers on the dark canvas.
- **Deep Console Muted** (`colors.deep-console-muted`): dark secondary and muted surfaces.
- **Deep Instrument** (`colors.deep-instrument`): dark selected and instrument surfaces.
- **Console Foreground, Muted, and Instrument** (`colors.console-foreground`, `colors.console-muted`, `colors.console-instrument`): dark-theme text roles.

The global canvas may carry only the existing low-opacity teal and orange corner washes. They should read as atmosphere, never as luminous glow.

**The Brand Is Not Semantics Rule.** Pit Navy, Signal Orange, and Instrument Teal may remain normative for artwork, charts, and large accents; normal text and controls must use role pairings that meet WCAG 2.2 AA.

**The Exception Color Rule.** Emerald, amber, and red are reserved for evidence state and consequence. Always pair color with explicit text.

## Typography

**Display Font:** system UI sans (`-apple-system`, `BlinkMacSystemFont`, `Segoe UI`, `Roboto`, and platform fallbacks)

**Body Font:** the same system UI sans family

**Label/Mono Font:** system UI sans for labels; system monospace for identifiers and operation evidence

**Character:** The typography is direct, familiar, and compact. A single workhorse family keeps Operate mode fast and coherent; weight, spacing, case, and numeric alignment create hierarchy without a decorative display face.

### Hierarchy
- **Display** (700, 1.875rem/2.25rem, -0.025em): the single visible page title.
- **Headline** (700, 1.5rem/2rem, -0.025em): a major route-local section when it does not repeat the page title.
- **Title** (600, 1.25rem/1.75rem): focused subsection headings.
- **Panel Title** (600, 1rem/1): card, evidence-panel, and table-region names.
- **Body** (400, 0.875rem/1.25rem): the default operational reading size.
- **Control** (500, 0.875rem/1.25rem): buttons, navigation, selects, and field labels.
- **Label** (600, 0.75rem/1rem): table headings, metric labels, metadata, and status chips; uppercase is reserved for short scan labels.
- **Mono** (400, 0.75rem/1rem): stable identifiers, image revisions, operation names, and compact machine evidence.

Use tabular numerals for capacity, counts, resource values, and sortable numeric columns.

**The One Workhorse Rule.** The single system UI family is intentional in this Operate system. Do not add a display family merely to create variety.

**The Human Name First Rule.** Human-readable task and entity names own hierarchy; stable identifiers are secondary monospaced evidence.

**The One Page Title Rule.** Every route has one visible H1. Do not repeat it immediately as a second H2.

## Layout

The authenticated shell becomes a two-column frame at the medium breakpoint (48rem): a fixed 17rem navigation rail and a minmax content column. Below that breakpoint, identity remains in a 4rem mobile header and navigation moves into a bounded left sheet.

Main content is centered to a maximum width of 80rem. It uses 1rem horizontal padding and 1.5rem vertical padding on narrow screens, increasing to 2rem at 40rem and above. The 4px base scale produces the recurring rhythm: 0.5rem inside tight groups, 0.75rem between related controls, 1rem between operational blocks, 1.5rem between major sections, and 2rem for the roomiest page spacing.

Controls are generally 2.25rem high. Filters and evidence summaries use responsive grids that collapse to one column before widening to two, four, or task-specific columns. Inventory tables may establish a useful internal minimum width, but the scroll container must own that width and remain inside the viewport.

**The Exception-First Rule.** Lead with a human-readable entity or task title and material exceptions, then focused evidence and safe actions; inventory follows rather than leads.

**The Containment Rule.** At narrow widths and 200% zoom, long identifiers, tables, code values, and secondary navigation scroll or wrap inside their own region. The document itself must not overflow horizontally.

**The Not a Card Wall Rule.** Use grids only when comparison is the task. Do not turn unrelated metrics or sections into an equal-weight card field.

## Elevation & Depth

Depth is tonal first and structural second. Canvas, card, muted, and instrument surfaces establish layers; a one-pixel cool border defines most boundaries. Soft shadows are present only for controls, genuinely raised evidence containers, navigation sheets, dialogs, and interruptive surfaces.

### Shadow Vocabulary
- **Control Lift** (`0 1px 2px 0 rgb(0 0 0 / 0.05)`): compact buttons and outlined controls.
- **Raised Surface** (`0 1px 3px 0 rgb(0 0 0 / 0.1), 0 1px 2px -1px rgb(0 0 0 / 0.1)`): cards or evidence panels that must sit above the canvas.
- **Interruptive Surface** (`0 10px 15px -3px rgb(0 0 0 / 0.1), 0 4px 6px -4px rgb(0 0 0 / 0.1)`): sheets and confirmation dialogs above a 50% black overlay.

**The Earned Elevation Rule.** Borders and tonal changes carry ordinary grouping. A shadow must indicate a real layer or interruption, never decoration.

## Shapes

The form language is gently compact rather than pill-heavy: controls use a 0.5rem radius, filter panels and dialogs use 0.625rem, and raised cards use 0.875rem. Status chips and small count badges may be fully rounded because their compact silhouette communicates state. One-pixel borders remain the default edge.

**The Familiar Geometry Rule.** Keep controls conventional and decisive. Reserve full pills for status, and do not introduce hard offset shadows, novelty silhouettes, or decorative clipping.

## Components

### Buttons
- **Shape:** compact rounded rectangle (0.5rem) at 2rem, 2.25rem, or 2.5rem high for small, default, and large sizes.
- **Primary:** Pit Navy with white text in the established accessible light pairing; default padding is 0.5rem by 1rem.
- **Secondary / Outline / Ghost / Link:** Mist Surface or Cool Canvas with Pit Navy, Instrument Surface on hover, and underlining only for the link variant.
- **Destructive:** the established critical action red with white text; use only for explicit destructive consequences.
- **Hover / Focus / Disabled:** 150ms state transitions, a visible three-pixel Instrument Teal focus ring, and 50% opacity when disabled.

Do not inherit the current Signal Orange and white normal-text pairing into the authority. Dark-theme actions need an accessible semantic pairing grounded in the existing deep instrument and console text roles.

### Cards / Containers
- **Corner Style:** gently raised (0.875rem) for the reusable card; compact operational panels commonly use 0.625rem.
- **Background:** White Surface in light mode and Deep Console in dark mode.
- **Border:** one-pixel Cool Line or the dark translucent border role.
- **Internal Padding:** 1.5rem by default, with 1rem or 1.25rem compact evidence variants.
- **Shadow Strategy:** Raised Surface only when the grouping is materially above the canvas.

Cards group one coherent task or evidence set. They are not the default page layout and must not create equal visual weight for unequal operational priorities.

### Inputs / Fields
- **Style:** 2.25rem high, 0.5rem radius, one-pixel border, canvas background, and 0.75rem horizontal padding.
- **Labels:** visible text labels are required; placeholders provide examples or format hints only.
- **Focus:** the Instrument Teal ring is visible without changing layout.
- **Error / Disabled:** error text and border use the critical family; disabled controls retain readable labels and explain blocked action where needed.

### Status Badges
- **Style:** fully rounded, 0.25rem by 0.5rem internal padding, 0.75rem semibold type, and sentence-readable state text.
- **State:** positive, caution, critical, and neutral roles map many domain states into a small consistent vocabulary.
- **Evidence:** status color never stands alone; nearby copy distinguishes current, stale, retained, unavailable, missing, inferred, and measured zero.

**The Evidence Has a State Rule.** Never render missing evidence as zero or let color imply certainty that the text does not state.

### Navigation
- **Primary:** 0.875rem medium labels, 0.75rem by 0.5rem padding, 0.5rem corners, and a pale Instrument Surface for hover and active state.
- **Secondary:** a 2.75rem-high horizontal route strip with a two-pixel active underline and 1.5rem item gaps.
- **Mobile:** the same authorized destinations move into a left sheet; identity and tenant context remain stable.

Navigation remains familiar and low-motion. Horizontal overflow is contained and must not expose an unstyled browser scrollbar as part of the visual language.

### Dialogs / Sheets
- **Dialog:** centered, bounded to 32rem, 1.5rem padded, 0.625rem corners, one-pixel border, and Interruptive Surface shadow.
- **Sheet:** edge-attached, bordered, and shadowed above the same 50% black overlay.
- **Motion:** 150-200ms fades, zooms, or directional slides clarify layer changes; reduced motion preserves the state change without spatial travel.
- **Actions:** cancel restores focus; the consequential action names the exact effect.

**The Confirm Consequence Rule.** Credential rotation, revocation, and other consequential mutations require an explicit confirmation surface before execution.

### Tables / Charts
- **Tables:** 0.875rem body text, 0.75rem muted uppercase headers, 0.75rem to 1rem cell padding, tabular numeric columns, and monospaced identifiers.
- **Containment:** wide tables scroll inside rounded bordered regions; sticky headers remain tonal rather than floating.
- **Charts:** two-pixel series lines use the semantic chart roles derived from Pit Navy, Signal Orange, Instrument Teal, and accessible supporting series; unavailable observations break the line, and an equivalent data table remains available.
- **Meaning:** charts and tables name observation time and never use resource activity as proof of workload identity.

### Brand Lockup

Preserve the mascot, logo, favicon, and PitCrew wordmark treatment. Pit Navy, Signal Orange, and Instrument Teal remain the artwork vocabulary; small supporting copy must move to an accessible semantic text role rather than inheriting a low-contrast brand color.

## Do's and Don'ts

### Do:
- **Do** lead every operational route with one human-readable title, material exceptions, and the freshest trustworthy evidence.
- **Do** preserve the mascot and navy/orange/teal identity while separating artwork colors from semantic text and action roles.
- **Do** state whether evidence is current, stale, retained, unavailable, missing, inferred, or measured zero.
- **Do** use the 4px spacing base, 2.25rem controls, and 1rem to 2rem section rhythm to stay compact but breathable.
- **Do** keep identifiers secondary, monospaced, selectable, and safely wrappable.
- **Do** contain table, code, and secondary-navigation overflow inside the owning component.
- **Do** provide visible labels, keyboard focus, text equivalents for charts, and AA contrast in light and dark themes.
- **Do** provide a reduced-motion alternative that preserves layer and state feedback.
- **Do** confirm consequential credential and enrollment actions before execution.

### Don't:
- **Don't** build generic equal-weight card grids, raw-data walls, neon operations theater, or decorative glass and blur.
- **Don't** use brand color alone to communicate health, severity, certainty, or action safety.
- **Don't** use hard offset shadows, ornamental motion, or novelty controls outside the familiar Operate vocabulary.

### Non-canonical drift — prohibited inheritance:
- **Don't** inherit small Instrument Teal text below AA contrast on light surfaces.
- **Don't** inherit white normal text on Signal Orange or Signal Orange normal text on dark cards when either pairing is below AA.
- **Don't** use raw UUIDs as page titles or repeat the shell H1 as a duplicate H2.
- **Don't** allow document-level horizontal overflow, a page-sized main focus outline, or raw browser scrollbars on secondary navigation.
- **Don't** use placeholder-only visible labels.
- **Don't** perform consequential credential actions without confirmation.
- **Don't** inherit the current 500ms navigation-sheet transition or motion without an intentional reduced-motion alternative.
- **Don't** hardcode unrelated chart hues when an established semantic chart role can carry the series.
