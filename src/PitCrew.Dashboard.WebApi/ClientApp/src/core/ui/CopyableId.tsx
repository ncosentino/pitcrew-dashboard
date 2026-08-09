import { useCallback, useState, type ReactNode } from 'react';
import { CheckIcon, CopyIcon } from 'lucide-react';

import { Button } from '@/components/ui/button';

import { typography } from './typography';

/** Props for a stable identifier shown as secondary copyable metadata. */
export interface CopyableIdProps {
  /** The identifier value to display and copy. */
  readonly value: string;
  /** An accessible label describing what this ID represents. */
  readonly label: string;
  /** Optional metadata label rendered before the identifier. */
  readonly prefix?: ReactNode;
  /** Optional clipboard implementation for tests or non-browser hosts. */
  readonly copyText?: (value: string) => Promise<void>;
}

/**
 * Renders a stable identifier as secondary monospaced text with a copy-to-
 * clipboard button. IDs are secondary to human-readable names per DESIGN.md's
 * "The Human Name First Rule". The button announces success to assistive
 * technology through a transient aria-label change.
 */
export function CopyableId({ value, label, prefix, copyText }: CopyableIdProps) {
  const [copyState, setCopyState] = useState<{
    readonly value: string;
    readonly status: 'copied' | 'failed';
  } | null>(null);
  const status = copyState?.value === value ? copyState.status : 'idle';

  const copy = useCallback(async () => {
    try {
      await (copyText ?? ((nextValue: string) => navigator.clipboard.writeText(nextValue)))(value);
      setCopyState({ value, status: 'copied' });
    } catch {
      setCopyState({ value, status: 'failed' });
    }
  }, [copyText, value]);

  return (
    <span className="inline-flex min-w-0 items-center gap-1">
      {prefix ? <span className="text-xs text-muted-foreground">{prefix}</span> : null}
      <span className={typography.identifier} data-testid="copyable-id-value">
        {value}
      </span>
      <Button
        type="button"
        className="size-8 text-muted-foreground hover:text-foreground"
        variant="ghost"
        size="icon"
        aria-label={status === 'copied' ? `Copied ${label}` : `Copy ${label}`}
        onClick={() => void copy()}
      >
        {status === 'copied' ? (
          <CheckIcon className="size-3" aria-hidden="true" />
        ) : (
          <CopyIcon className="size-3" aria-hidden="true" />
        )}
      </Button>
      {status !== 'idle' ? (
        <span className="sr-only" role="status">
          {status === 'copied' ? `${label} copied.` : `Copy unavailable. Select ${label} manually.`}
        </span>
      ) : null}
    </span>
  );
}
