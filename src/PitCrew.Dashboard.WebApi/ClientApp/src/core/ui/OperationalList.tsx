import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

export interface OperationalListProps {
  readonly label: string;
  readonly children: ReactNode;
  readonly className?: string;
}

/** A scan-first alternative to equal-weight card grids for operational records. */
export function OperationalList({ label, children, className }: OperationalListProps) {
  return (
    <div className={cn('overflow-hidden rounded-xl border bg-card', className)}>
      <ul aria-label={label} className="divide-y">
        {children}
      </ul>
    </div>
  );
}

export interface OperationalRowProps {
  readonly testId?: string;
  readonly title: ReactNode;
  readonly description?: ReactNode;
  readonly metadata?: ReactNode;
  readonly status?: ReactNode;
  readonly actions?: ReactNode;
  readonly selected?: boolean;
  readonly children?: ReactNode;
}

/** Renders one full-width record with stable identity, state, evidence, and trailing actions. */
export function OperationalRow({
  testId,
  title,
  description,
  metadata,
  status,
  actions,
  selected = false,
  children,
}: OperationalRowProps) {
  return (
    <li
      data-testid={testId}
      className={cn('min-w-0 px-4 py-3 transition-colors sm:px-5', selected && 'bg-accent/60')}
    >
      <article className="grid min-w-0 gap-3 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-center">
        <div className="min-w-0">
          <div className="flex min-w-0 flex-wrap items-center gap-2">
            <h3 className="min-w-0 text-sm font-semibold text-foreground">{title}</h3>
            {status}
          </div>
          {description ? (
            <div className="mt-1 [overflow-wrap:anywhere] text-sm text-muted-foreground">
              {description}
            </div>
          ) : null}
          {metadata ? <div className="mt-2 min-w-0">{metadata}</div> : null}
          {children ? <div className="mt-2 min-w-0">{children}</div> : null}
        </div>
        {actions ? (
          <div className="flex flex-wrap items-center gap-2 lg:justify-end">{actions}</div>
        ) : null}
      </article>
    </li>
  );
}
