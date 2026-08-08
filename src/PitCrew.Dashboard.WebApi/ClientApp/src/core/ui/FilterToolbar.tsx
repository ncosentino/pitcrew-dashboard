import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

/** Props for the shared filter/sort/density control toolbar. */
export interface FilterToolbarProps {
  /**
   * Accessible name for the toolbar landmark. Omit it when the toolbar's
   * purpose is already conveyed by a preceding heading or PageHeader/
   * EntityHeader — an unlabeled `section`/`role="region"` is an
   * unannounced landmark, so FilterToolbar renders a plain `div` instead
   * of a landmark unless a label is supplied.
   */
  readonly label?: string;
  /** FormField (or other labeled control) elements, one per filter. */
  readonly children: ReactNode;
  readonly className?: string;
}

/**
 * Renders the shared responsive grid for a row of filter/sort/density
 * controls above an operational collection. Reach for FormField inside it
 * for each control so labels stay associated and consistently styled.
 */
export function FilterToolbar({ children, className, label }: FilterToolbarProps) {
  const Container = label ? 'section' : 'div';

  return (
    <Container
      aria-label={label}
      className={cn(
        'grid gap-3 rounded-lg border bg-card p-4 sm:grid-cols-2 xl:grid-cols-4',
        className,
      )}
    >
      {children}
    </Container>
  );
}
