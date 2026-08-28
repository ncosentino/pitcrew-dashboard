import { useEffect, useId, useRef, useState } from 'react';
import { CheckIcon, CopyIcon } from 'lucide-react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';

interface OneTimeValueProps {
  readonly title: string;
  readonly value: string;
  readonly description: string;
  readonly onClear: () => void;
}

/** Focuses, copies, and explicitly clears one-time credential material. */
export function OneTimeValue({ title, value, description, onClear }: OneTimeValueProps) {
  const titleId = useId();
  const container = useRef<HTMLElement>(null);
  const previousValue = useRef<string | null>(null);
  const [copyResult, setCopyResult] = useState<{
    readonly value: string;
    readonly status: 'copied' | 'failed';
  } | null>(null);
  const copyStatus = copyResult?.value === value ? copyResult.status : 'idle';

  useEffect(() => {
    if (previousValue.current === value) return;
    previousValue.current = value;
    container.current?.focus();
  }, [value]);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopyResult({ value, status: 'copied' });
    } catch {
      setCopyResult({ value, status: 'failed' });
    }
  };

  return (
    <section
      ref={container}
      aria-labelledby={titleId}
      className="grid gap-3 rounded-lg border border-status-caution-foreground/30 bg-status-caution p-4 text-status-caution-foreground outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
      tabIndex={-1}
    >
      <div>
        <h3 id={titleId} className="text-sm font-semibold">
          {title}
        </h3>
        <p className="mt-1 text-xs">{description}</p>
      </div>
      <code className="break-all rounded bg-background p-3 text-xs text-foreground">{value}</code>
      <div className="flex flex-wrap gap-2">
        <Button type="button" size="sm" variant="outline" onClick={() => void copy()}>
          {copyStatus === 'copied' ? (
            <CheckIcon className="size-4" aria-hidden="true" />
          ) : (
            <CopyIcon className="size-4" aria-hidden="true" />
          )}
          {copyStatus === 'copied' ? 'Copied' : 'Copy value'}
        </Button>
        <ConfirmActionDialog
          trigger={
            <Button type="button" size="sm" variant="ghost">
              Clear one-time value
            </Button>
          }
          title="Clear this one-time value?"
          description="Clear it only after storing the value somewhere appropriate. Dashboard cannot display it again."
          confirmLabel="Clear value"
          details={
            <ConfirmationSummary
              identity={[{ label: 'One-time result', value: title }]}
              effects={[
                'The raw value is removed from this browser view.',
                'Dashboard cannot recover or display it again.',
              ]}
              prohibitedEffects={['This does not revoke or invalidate the issued value.']}
            />
          }
          onConfirm={onClear}
        />
      </div>
      {copyStatus === 'failed' ? (
        <p className="text-xs font-medium" role="alert">
          Copy failed. Select the value manually before clearing it.
        </p>
      ) : null}
      <span className="sr-only" role="status" aria-live="polite">
        {copyStatus === 'copied' ? 'One-time value copied.' : `${title} ready to copy.`}
      </span>
    </section>
  );
}
