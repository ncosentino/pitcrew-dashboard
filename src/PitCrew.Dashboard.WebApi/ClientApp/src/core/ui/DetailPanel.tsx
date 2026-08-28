import { useId, type ReactNode, type Ref } from 'react';

import { cn } from '@/lib/utils';

export interface DetailPanelProps {
  readonly title: string;
  readonly description?: ReactNode;
  readonly status?: ReactNode;
  readonly actions?: ReactNode;
  readonly children: ReactNode;
  readonly className?: string;
  /** Receives the focused title when a list selection moves into this panel. */
  readonly focusTitleRef?: Ref<HTMLHeadingElement>;
}

/** Contains the single operational record currently selected for deeper investigation. */
export function DetailPanel({
  title,
  description,
  status,
  actions,
  children,
  className,
  focusTitleRef,
}: DetailPanelProps) {
  const titleId = useId();

  return (
    <section
      aria-labelledby={titleId}
      className={cn('min-w-0 rounded-xl border bg-card p-4 shadow-sm sm:p-5', className)}
    >
      <div className="flex flex-wrap items-start justify-between gap-3 border-b pb-4">
        <div className="min-w-0">
          <div className="flex min-w-0 flex-wrap items-center gap-2">
            <h2
              ref={focusTitleRef}
              id={titleId}
              className="min-w-0 rounded-sm [overflow-wrap:anywhere] text-lg font-semibold outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
              tabIndex={focusTitleRef ? -1 : undefined}
            >
              {title}
            </h2>
            {status}
          </div>
          {description ? (
            <div className="mt-1 max-w-[72ch] [overflow-wrap:anywhere] text-sm text-muted-foreground">
              {description}
            </div>
          ) : null}
        </div>
        {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
      </div>
      <div className="min-w-0 pt-4">{children}</div>
    </section>
  );
}
