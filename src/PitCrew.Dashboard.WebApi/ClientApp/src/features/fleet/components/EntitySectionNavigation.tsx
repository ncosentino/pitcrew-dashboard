import { NavLink } from 'react-router-dom';

import { cn } from '@/lib/utils';

/** One route-level destination in an entity's secondary navigation. */
export interface EntitySectionNavigationItem {
  /** Short label that identifies the destination. */
  readonly label: string;
  /** Absolute tenant-scoped path for the destination. */
  readonly path: string;
}

interface EntitySectionNavigationProps {
  readonly label: string;
  readonly items: ReadonlyArray<EntitySectionNavigationItem>;
}

/** Renders responsive secondary navigation between route-level entity destinations. */
export function EntitySectionNavigation({ label, items }: EntitySectionNavigationProps) {
  return (
    <nav
      aria-label={label}
      className="overflow-x-auto border-b"
      data-testid="entity-section-navigation"
    >
      <ul className="flex min-w-max items-stretch gap-6 px-1">
        {items.map((item) => (
          <li key={item.path}>
            <NavLink
              className={({ isActive }) =>
                cn(
                  '-mb-px flex h-11 items-center border-b-2 border-transparent px-0.5 text-sm font-medium text-muted-foreground outline-none transition-colors hover:border-muted-foreground/40 hover:text-foreground focus-visible:rounded-sm focus-visible:ring-2 focus-visible:ring-ring',
                  isActive && 'border-primary text-foreground',
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
