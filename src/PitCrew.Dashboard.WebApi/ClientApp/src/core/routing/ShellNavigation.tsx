import { Link, matchPath } from 'react-router-dom';

import { cn } from '@/lib/utils';

export interface ShellNavigationItem {
  readonly label: string;
  readonly path: string;
  readonly activePaths: ReadonlyArray<string>;
}

interface ShellNavigationProps {
  readonly items: ReadonlyArray<ShellNavigationItem>;
  readonly pathname: string;
  readonly onNavigate: () => void;
}

/** Renders the same authorized destinations in desktop and mobile shell surfaces. */
export function ShellNavigation({ items, pathname, onNavigate }: ShellNavigationProps) {
  return (
    <nav aria-label="Primary navigation">
      <ul className="grid gap-1">
        {items.map((item) => {
          const isActive = item.activePaths.some((path) =>
            matchPath({ path, end: true }, pathname),
          );
          return (
            <li key={`${item.label}-${item.path}`}>
              <Link
                className={cn(
                  'block rounded-md px-3 py-2 text-sm font-medium outline-none transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:ring-2 focus-visible:ring-sidebar-ring',
                  isActive && 'bg-sidebar-accent text-sidebar-accent-foreground',
                )}
                to={item.path}
                aria-current={isActive ? 'page' : undefined}
                onClick={onNavigate}
              >
                {item.label}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
