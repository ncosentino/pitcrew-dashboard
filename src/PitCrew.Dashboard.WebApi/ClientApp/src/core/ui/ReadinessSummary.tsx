import { useId, type ReactNode } from 'react';

import { cn } from '@/lib/utils';

export interface ReadinessSummaryItem {
  readonly label: string;
  readonly value: ReactNode;
  readonly detail?: ReactNode;
}

export interface ReadinessSummaryProps {
  readonly title: string;
  readonly description: ReactNode;
  readonly status?: ReactNode;
  readonly items: ReadonlyArray<ReadinessSummaryItem>;
  /** Uses two compact metric columns below the shared small-screen breakpoint. */
  readonly narrowColumns?: 1 | 2;
  readonly className?: string;
}

/** Presents the minimum evidence needed to decide whether a workflow can proceed. */
export function ReadinessSummary({
  title,
  description,
  status,
  items,
  narrowColumns = 1,
  className,
}: ReadinessSummaryProps) {
  const titleId = useId();

  return (
    <section
      aria-labelledby={titleId}
      className={cn('rounded-xl border bg-card p-4 shadow-sm sm:p-5', className)}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 id={titleId} className="text-base font-semibold">
            {title}
          </h2>
          <div className="mt-1 max-w-[72ch] text-sm text-muted-foreground">{description}</div>
        </div>
        {status ? <div className="min-w-0 max-w-full">{status}</div> : null}
      </div>
      <dl
        className={cn(
          'mt-4 grid gap-3 border-t pt-4 sm:grid-cols-2 xl:grid-cols-4',
          narrowColumns === 2 && 'grid-cols-2',
        )}
      >
        {items.map((item) => (
          <div className="min-w-0" key={item.label}>
            <dt className="text-xs font-medium text-muted-foreground">{item.label}</dt>
            <dd className="mt-1 min-w-0 [overflow-wrap:anywhere] text-sm font-semibold text-foreground">
              {item.value}
            </dd>
            {item.detail ? (
              <dd className="mt-0.5 min-w-0 [overflow-wrap:anywhere] text-xs text-muted-foreground">
                {item.detail}
              </dd>
            ) : null}
          </div>
        ))}
      </dl>
    </section>
  );
}
