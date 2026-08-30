import { useState } from 'react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { ConfirmationSummary, type ConfirmationSummaryFact } from '@/core/ui/ConfirmationSummary';

interface ImageCampaignMutationAuthorizationProps {
  readonly triggerLabel: string;
  readonly pendingLabel: string;
  readonly title: string;
  readonly description: string;
  readonly disabled: boolean;
  readonly submitting: boolean;
  readonly identity: ReadonlyArray<ConfirmationSummaryFact>;
  readonly fences: ReadonlyArray<ConfirmationSummaryFact>;
  readonly effects: ReadonlyArray<string>;
  readonly prohibitedEffects: ReadonlyArray<string>;
  readonly acknowledgementLabel: string;
  readonly acknowledgementTestId: string;
  readonly variant?: 'default' | 'outline' | 'destructive';
  readonly onConfirm: () => Promise<boolean>;
}

/** Protects one campaign mutation with exact identity, fences, and consequences. */
export function ImageCampaignMutationAuthorization({
  triggerLabel,
  pendingLabel,
  title,
  description,
  disabled,
  submitting,
  identity,
  fences,
  effects,
  prohibitedEffects,
  acknowledgementLabel,
  acknowledgementTestId,
  variant = 'outline',
  onConfirm,
}: ImageCampaignMutationAuthorizationProps) {
  const [open, setOpen] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);

  return (
    <ConfirmActionDialog
      open={open}
      onOpenChange={(nextOpen) => {
        setOpen(nextOpen);
        if (!nextOpen) setAcknowledged(false);
      }}
      trigger={
        <Button disabled={disabled || submitting} type="button" variant={variant}>
          {submitting ? pendingLabel : triggerLabel}
        </Button>
      }
      title={title}
      description={description}
      confirmLabel={submitting ? pendingLabel : triggerLabel}
      confirmVariant={variant}
      confirmDisabled={!acknowledged || submitting}
      details={
        <ConfirmationSummary
          identity={identity}
          fences={fences}
          effects={effects}
          prohibitedEffects={prohibitedEffects}
          acknowledgement={{
            label: acknowledgementLabel,
            checked: acknowledged,
            onCheckedChange: setAcknowledged,
            testId: acknowledgementTestId,
          }}
        />
      }
      onConfirm={async () => {
        if (await onConfirm()) {
          setOpen(false);
          setAcknowledged(false);
        }
      }}
    />
  );
}
