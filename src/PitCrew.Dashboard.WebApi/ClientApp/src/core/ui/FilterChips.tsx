import { X } from 'lucide-react';
import type { ReactNode } from 'react';

import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

/** One dismissible active filter chip. */
export interface FilterChipDescriptor {
  readonly key: string;
  readonly label: string;
  readonly value: string;
  readonly onRemove?: () => void;
}

/** Props for the active-filter summary row shown below a filter toolbar. */
export interface FilterChipsProps {
  readonly chips: ReadonlyArray<FilterChipDescriptor>;
  readonly resultSummary: ReactNode;
  readonly onClearAll?: () => void;
  readonly clearAllLabel?: string;
  readonly className?: string;
}

/**
 * Summarizes the current result count and any active filters with dismissible
 * chips plus an optional clear-all action.
 */
export function FilterChips({
  chips,
  resultSummary,
  onClearAll,
  clearAllLabel = 'Clear all filters',
  className,
}: FilterChipsProps) {
  return (
    <div
      className={cn(
        'flex min-w-0 flex-wrap items-center gap-2 text-sm text-muted-foreground',
        className,
      )}
    >
      <span className="font-medium text-foreground">{resultSummary}</span>
      {chips.map((chip) => (
        <span
          className="inline-flex min-w-0 items-center gap-1 rounded-full border bg-card px-3 py-1 text-foreground"
          key={chip.key}
        >
          <span className="text-muted-foreground">{chip.label}:</span>
          <span className="min-w-0 break-all">{chip.value}</span>
          {chip.onRemove ? (
            <Button
              type="button"
              className="size-7 rounded-full text-muted-foreground hover:text-foreground"
              size="icon"
              variant="ghost"
              aria-label={`Remove filter ${chip.label}: ${chip.value}`}
              onClick={chip.onRemove}
            >
              <X className="size-3" aria-hidden="true" />
            </Button>
          ) : null}
        </span>
      ))}
      {chips.length > 0 && onClearAll ? (
        <Button type="button" size="sm" variant="ghost" onClick={onClearAll}>
          {clearAllLabel}
        </Button>
      ) : null}
    </div>
  );
}
