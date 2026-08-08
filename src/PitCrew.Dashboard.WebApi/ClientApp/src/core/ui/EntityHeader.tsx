import type { ReactNode } from 'react';

import { typography } from './typography';

/** Props for the shared entity detail header. */
export interface EntityHeaderProps {
  /** Human-readable entity name (DESIGN.md "The Human Name First Rule"). */
  readonly title: string;
  /** Secondary monospaced identifier shown under the title, when the entity has a stable ID. */
  readonly identifier?: string;
  /** Heading level for the entity title. Defaults to h2, assuming the page's PageHeader owns the H1. */
  readonly headingLevel?: 'h2' | 'h3';
  /** Status badges, buttons, or other entity-level actions aligned to the title. */
  readonly actions?: ReactNode;
}

/**
 * Renders an entity's human-readable title and secondary identifier below a
 * route's PageHeader. Human-readable names own the hierarchy; identifiers
 * stay secondary, monospaced, and wrap instead of overflowing their region.
 */
export function EntityHeader({
  title,
  identifier,
  headingLevel = 'h2',
  actions,
}: EntityHeaderProps) {
  const Heading = headingLevel;

  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0">
        <Heading className={typography.entityTitle}>{title}</Heading>
        {identifier ? <p className={typography.identifier}>{identifier}</p> : null}
      </div>
      {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
    </div>
  );
}
