import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

export interface DetailPanelProps {
  readonly title: string;
  readonly description?: ReactNode;
  readonly status?: ReactNode;
  readonly actions?: ReactNode;
  readonly children: ReactNode;
  readonly className?: string;
}

/** Contains the single operational record currently selected for deeper investigation. */
export function DetailPanel({
  title,
  description,
  status,
  actions,
  children,
  className,
}: DetailPanelProps) {
  return (
    <section
      aria-labelledby="detail-panel-title"
      className={cn('min-w-0 rounded-xl border bg-card p-4 shadow-sm sm:p-5', className)}
    >
      <div className="flex flex-wrap items-start justify-between gap-3 border-b pb-4">
        <div className="min-w-0">
          <div className="flex min-w-0 flex-wrap items-center gap-2">
            <h2 id="detail-panel-title" className="text-lg font-semibold">
              {title}
            </h2>
            {status}
          </div>
          {description ? (
            <div className="mt-1 max-w-[72ch] text-sm text-muted-foreground">{description}</div>
          ) : null}
        </div>
        {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
      </div>
      <div className="min-w-0 pt-4">{children}</div>
    </section>
  );
}
