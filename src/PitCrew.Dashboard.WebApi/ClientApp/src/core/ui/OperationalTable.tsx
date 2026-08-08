import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

import { ScrollableRegion } from './ScrollableRegion';

/** One column heading rendered by OperationalTable. */
export interface OperationalTableColumn {
  readonly key: string;
  readonly header: ReactNode;
  readonly align?: 'left' | 'right';
}

/** Props for the shared operational data table shell. */
export interface OperationalTableProps {
  /**
   * Names what the table enumerates and its scope. Rendered as the
   * table's visible `<caption>` and reused, unaltered, as the wrapping
   * ScrollableRegion's accessible `label` so the region and the table it
   * contains are named consistently rather than requiring a second,
   * possibly-divergent label.
   */
  readonly caption: string;
  readonly columns: ReadonlyArray<OperationalTableColumn>;
  /** Row markup (`<tr>` elements); OperationalTable owns the table shell, not row content. */
  readonly children: ReactNode;
  /** Overrides the default minimum table width that forces horizontal scroll instead of wrapping. */
  readonly minWidthClassName?: string;
  readonly className?: string;
}

/**
 * Owns the `<table>`/`<caption>`/`<thead>` shell and the min-width/overflow
 * contract for dense operational data: the table declares a minimum width so
 * columns never wrap into unreadable stacks, and the surrounding
 * ScrollableRegion owns the resulting horizontal overflow so the page itself
 * stays put. Callers own row markup and any row-level `data-testid`s.
 */
export function OperationalTable({
  caption,
  columns,
  children,
  minWidthClassName = 'min-w-5xl',
  className,
}: OperationalTableProps) {
  return (
    <ScrollableRegion className={cn('rounded-lg border bg-card', className)} label={caption}>
      <table className={cn('w-full text-left text-sm', minWidthClassName)}>
        <caption className="p-3 text-left text-sm font-semibold">{caption}</caption>
        <thead className="bg-muted/50 text-xs text-muted-foreground uppercase">
          <tr>
            {columns.map((column) => (
              <th
                className={cn('px-4 py-3 font-medium', column.align === 'right' && 'text-right')}
                key={column.key}
                scope="col"
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </ScrollableRegion>
  );
}
