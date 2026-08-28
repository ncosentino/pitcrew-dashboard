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
  readonly className?: string;
}

/** Presents the minimum evidence needed to decide whether a workflow can proceed. */
export function ReadinessSummary({
  title,
  description,
  status,
  items,
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
      <dl className="mt-4 grid gap-3 border-t pt-4 sm:grid-cols-2 xl:grid-cols-4">
        {items.map((item) => (
          <div className="min-w-0" key={item.label}>
            <dt className="text-xs font-medium text-muted-foreground">{item.label}</dt>
            <dd className="mt-1 text-sm font-semibold text-foreground">{item.value}</dd>
            {item.detail ? (
              <dd className="mt-0.5 text-xs text-muted-foreground">{item.detail}</dd>
            ) : null}
          </div>
        ))}
      </dl>
    </section>
  );
}
