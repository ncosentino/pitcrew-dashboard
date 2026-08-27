import { NavLink } from 'react-router-dom';

import { cn } from '@/lib/utils';

/** One task-oriented destination inside a multi-workflow feature. */
export interface TaskNavigationItem {
  readonly label: string;
  readonly description: string;
  readonly path: string;
  readonly badge?: string;
}

export interface TaskNavigationProps {
  readonly label: string;
  readonly items: ReadonlyArray<TaskNavigationItem>;
}

/**
 * Keeps feature-local tasks in a stable reading order. It renders as a compact
 * horizontal strip until the workspace has room for a left-hand task rail.
 */
export function TaskNavigation({ label, items }: TaskNavigationProps) {
  return (
    <nav aria-label={label} className="min-w-0">
      <ul
        className={cn(
          'flex min-w-0 gap-2 overflow-x-auto pb-1',
          '[scrollbar-width:none] [-ms-overflow-style:none] [&::-webkit-scrollbar]:hidden',
          'lg:grid lg:overflow-visible lg:pb-0',
        )}
      >
        {items.map((item) => (
          <li className="shrink-0 lg:shrink" key={item.path}>
            <NavLink
              className={({ isActive }) =>
                cn(
                  'flex min-h-11 min-w-40 items-center justify-between gap-3 rounded-lg border border-transparent px-3 py-2 outline-none transition-colors',
                  'hover:bg-accent hover:text-accent-foreground focus-visible:ring-2 focus-visible:ring-ring',
                  'lg:min-w-0 lg:items-start',
                  isActive && 'border-border bg-card text-foreground shadow-sm',
                  !isActive && 'text-muted-foreground',
                )
              }
              end
              to={item.path}
            >
              <span className="min-w-0">
                <span className="block text-sm font-medium text-current">{item.label}</span>
                <span className="mt-0.5 hidden text-xs leading-4 text-muted-foreground lg:block">
                  {item.description}
                </span>
              </span>
              {item.badge ? (
                <span className="shrink-0 rounded-full bg-muted px-2 py-0.5 text-xs font-medium tabular-nums text-foreground">
                  {item.badge}
                </span>
              ) : null}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}
