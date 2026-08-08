import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

/** Props for the shared horizontal-overflow-owning wrapper. */
export interface ScrollableRegionProps {
  /**
   * Accessible name for the scrollable region, announced by assistive
   * technology alongside its `role="region"` — required because an
   * unlabeled region is indistinguishable from every other region on the
   * page. Name it after what the region contains (e.g. "Fleet nodes for
   * the active tenant"), not the fact that it scrolls.
   */
  readonly label: string;
  readonly children: ReactNode;
  readonly className?: string;
}

/**
 * Owns horizontal overflow for wide content (tables, dense grids) so the
 * page itself never scrolls sideways. Compose this around any content whose
 * natural width can exceed the viewport instead of adding `overflow-x-auto`
 * ad hoc at each call site.
 *
 * `min-w-0 max-w-full` keep the region itself from stretching a flex/grid
 * ancestor to the width of its (wider) content — the failure mode that lets
 * overflow wrappers still widen the document — while `overflow-x-auto` and
 * `overscroll-x-contain` scope the resulting scroll to the region instead of
 * the page. `role="region"` plus the required `label` names the region for
 * assistive technology, and `tabIndex={0}` makes it independently reachable
 * so keyboard and non-pointer users can scroll it without a mouse.
 */
export function ScrollableRegion({ children, className, label }: ScrollableRegionProps) {
  return (
    <div
      aria-label={label}
      className={cn(
        'min-w-0 max-w-full overflow-x-auto overscroll-x-contain',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2',
        className,
      )}
      role="region"
      tabIndex={0}
    >
      {children}
    </div>
  );
}
