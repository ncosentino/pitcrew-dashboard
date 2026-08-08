import type { ReactNode } from 'react';

import { typography } from './typography';

/** Props for the shared route page header. */
export interface PageHeaderProps {
  /** The route's single visible H1 (DESIGN.md "The One Page Title Rule"). */
  readonly title: string;
  /** Breadcrumb trail rendered above the title, when the route has one. */
  readonly breadcrumbs?: ReactNode;
  /** Optional supporting copy rendered directly under the title. */
  readonly description?: ReactNode;
  /** Page-level actions (for example, a primary action button) aligned to the title. */
  readonly actions?: ReactNode;
}

/**
 * Renders the one H1 a route owns, plus its breadcrumbs, description, and
 * actions. A page composes exactly one PageHeader; entity or section titles
 * within the page use EntityHeader or a section heading instead so the
 * document keeps a single H1.
 */
export function PageHeader({ title, breadcrumbs, description, actions }: PageHeaderProps) {
  return (
    <div className="grid gap-2">
      {breadcrumbs}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="grid min-w-0 gap-1">
          <h1 className={typography.pageTitle}>{title}</h1>
          {description ? <div className={typography.body}>{description}</div> : null}
        </div>
        {actions ? <div className="flex shrink-0 items-center gap-2">{actions}</div> : null}
      </div>
    </div>
  );
}
