import { NavLink } from 'react-router-dom';

import { cn } from '@/lib/utils';

/** One route-level destination in an entity's secondary navigation. */
export interface SectionNavigationItem {
  /** Short label that identifies the destination. */
  readonly label: string;
  /** Absolute tenant-scoped path for the destination. */
  readonly path: string;
}

interface SectionNavigationProps {
  /** Accessible name for the navigation region, naming the entity it belongs to. */
  readonly label: string;
  readonly items: ReadonlyArray<SectionNavigationItem>;
}

/**
 * Renders the horizontal secondary route strip DESIGN.md's Navigation
 * section describes: a two-pixel active underline, 1.5rem item gaps, and
 * contained horizontal overflow rather than a raw browser scrollbar
 * (DESIGN.md: "Horizontal overflow is contained and must not expose an
 * unstyled browser scrollbar as part of the visual language"). The
 * scrollbar itself is hidden (`scrollbar-width: none` /
 * `::-webkit-scrollbar { display: none }`) while native touch, wheel, and
 * keyboard scrolling (via the tabbable `NavLink`s) keep working;
 * `overflow-y-hidden` stops the two-pixel active underline or focus ring
 * from ever opening a vertical scrollbar, and `overscroll-x-contain` keeps
 * an at-the-edge scroll gesture from bubbling to the page.
 */
export function SectionNavigation({ label, items }: SectionNavigationProps) {
  return (
    <nav
      aria-label={label}
      className={cn(
        'overflow-x-auto overflow-y-hidden overscroll-x-contain border-b',
        '[scrollbar-width:none] [-ms-overflow-style:none] [&::-webkit-scrollbar]:hidden',
      )}
      data-testid="section-navigation"
    >
      <ul className="flex min-w-max items-stretch gap-6 px-1">
        {items.map((item) => (
          <li key={item.path}>
            <NavLink
              className={({ isActive }) =>
                cn(
                  '-mb-px flex h-11 items-center border-b-2 border-transparent px-0.5 text-sm font-medium text-muted-foreground outline-none transition-colors hover:border-muted-foreground/40 hover:text-foreground focus-visible:rounded-sm focus-visible:ring-2 focus-visible:ring-ring',
                  isActive && 'border-ring text-foreground',
                )
              }
              end
              to={item.path}
            >
              {item.label}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}
