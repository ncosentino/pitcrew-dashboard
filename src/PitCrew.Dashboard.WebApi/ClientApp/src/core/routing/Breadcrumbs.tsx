import { generatePath, Link } from 'react-router-dom';

import { formatRouteLabel, type MatchedRoutePresentation } from './routePresentation';

interface BreadcrumbsProps {
  readonly match: MatchedRoutePresentation;
}

/** Renders links from the current feature's route presentation. */
export function Breadcrumbs({ match }: BreadcrumbsProps) {
  if (match.presentation.breadcrumbs.length === 0) return null;

  return (
    <nav aria-label="Breadcrumb">
      <ol className="flex min-w-0 flex-wrap items-center gap-2 text-sm text-muted-foreground">
        {match.presentation.breadcrumbs.map((item, index) => {
          const label = formatRouteLabel(item.label, match.params);
          const path = item.path ? generatePath(item.path, match.params) : null;
          return (
            <li
              className="flex min-w-0 items-center gap-2"
              key={`${item.path ?? 'current'}-${item.label}`}
            >
              {index > 0 ? <span aria-hidden="true">/</span> : null}
              {path ? (
                <Link
                  className="truncate rounded-sm outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
                  to={path}
                >
                  {label}
                </Link>
              ) : (
                <span
                  className="truncate text-foreground"
                  aria-current={
                    index === match.presentation.breadcrumbs.length - 1 ? 'page' : undefined
                  }
                >
                  {label}
                </span>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
