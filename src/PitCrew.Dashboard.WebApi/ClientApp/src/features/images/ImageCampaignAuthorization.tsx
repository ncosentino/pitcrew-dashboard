import { useState } from 'react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { StateBanner } from '@/core/ui/StateBanner';

import type { ImageCampaign } from './imageCampaignApi';
import { nextPendingWave } from './imageCampaignView';

interface ImageCampaignAuthorizationProps {
  readonly campaign: ImageCampaign;
  readonly disabled: boolean;
  readonly submitting: boolean;
  readonly mutationError: string | null;
  readonly mutationStatus: string | null;
  readonly onApprove: (idempotencyKey: string) => Promise<boolean>;
}

/** Keeps one exact wave approval and its prohibited effects in a protected confirmation. */
export function ImageCampaignAuthorization({
  campaign,
  disabled,
  submitting,
  mutationError,
  mutationStatus,
  onApprove,
}: ImageCampaignAuthorizationProps) {
  const [open, setOpen] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [idempotencyKey] = useState(() => globalThis.crypto.randomUUID());
  const wave = nextPendingWave(campaign);
  if (wave === undefined) return null;

  const targets = campaign.targets.filter((target) => target.waveNumber === wave.waveNumber);
  const authorityIdentity = campaign.candidate
    ? [
        { label: 'Candidate ID', value: campaign.candidate.candidateId },
        { label: 'Recipe', value: campaign.candidate.recipeId },
        { label: 'Target digest', value: campaign.candidate.targetDigest },
        { label: 'Platform', value: campaign.candidate.targetPlatform },
      ]
    : targets.flatMap((target, index) => [
        {
          label: `Rollback target ${index + 1}`,
          value: `${target.nodeDisplayName} · ${target.nodeId} · ${target.profileId}`,
        },
        {
          label: `Rollback authority ${index + 1}`,
          value: target.candidate
            ? `${target.candidate.candidateId} · ${target.candidate.recipeId} · ${target.candidate.targetDigest} · ${target.candidate.targetPlatform}`
            : 'Authority unavailable',
        },
      ]);
  const targetIdentity = targets.map((target, index) => ({
    label: `Target ${index + 1}`,
    value: `${target.nodeDisplayName} · ${target.nodeId} · ${target.profileId}`,
  }));
  const confirm = async () => {
    if (await onApprove(idempotencyKey)) {
      setOpen(false);
      setAcknowledged(false);
    }
  };

  return (
    <section className="grid gap-3 border-t pt-4" aria-labelledby="campaign-approval-heading">
      <div>
        <h3 className="text-base font-semibold" id="campaign-approval-heading">
          Approve {wave.waveNumber === 0 ? 'canary' : `wave ${wave.waveNumber}`}
        </h3>
        <p className="mt-1 max-w-[72ch] text-sm text-muted-foreground">
          Approval applies only to the frozen targets in this wave. Later waves remain pending.
        </p>
      </div>
      {mutationError ? <StateBanner tone="critical">{mutationError}</StateBanner> : null}
      {mutationStatus ? (
        <p className="text-sm text-muted-foreground" role="status">
          {mutationStatus}
        </p>
      ) : null}
      <div>
        <ConfirmActionDialog
          open={open}
          onOpenChange={(nextOpen) => {
            setOpen(nextOpen);
            if (!nextOpen) setAcknowledged(false);
          }}
          trigger={
            <Button disabled={disabled || submitting} type="button">
              {submitting ? 'Approving…' : 'Approve wave'}
            </Button>
          }
          title={`Approve ${wave.waveNumber === 0 ? 'canary' : `wave ${wave.waveNumber}`}?`}
          description="PitCrew will queue only the immutable profile-image commands in this frozen wave."
          confirmLabel={submitting ? 'Approving…' : 'Approve wave'}
          confirmDisabled={!acknowledged || submitting}
          details={
            <ConfirmationSummary
              identity={[
                { label: 'Campaign', value: campaign.campaignId },
                { label: 'Wave', value: String(wave.waveNumber) },
                { label: 'Targets', value: String(targets.length) },
                ...authorityIdentity,
                ...(campaign.candidate ? targetIdentity : []),
              ]}
              fences={[
                { label: 'Target set', value: campaign.targetSetHash },
                { label: 'Campaign revision', value: String(campaign.revision) },
                { label: 'Request key', value: idempotencyKey },
              ]}
              effects={[
                `Queue the frozen image authority for ${targets.length} node/profile target${targets.length === 1 ? '' : 's'}.`,
                'Existing compatible busy workers may remain on the prior revision while draining.',
              ]}
              prohibitedEffects={[
                'No newly discovered target joins this campaign.',
                'No job cancellation, capacity change, routing change, or credential replacement.',
                'No automatic later-wave approval, indeterminate retry, or rollback.',
              ]}
              acknowledgement={{
                label:
                  'I reviewed the exact target set, candidate authority, and current campaign revision.',
                checked: acknowledged,
                onCheckedChange: setAcknowledged,
                testId: 'image-campaign-wave-acknowledgement',
              }}
            />
          }
          onConfirm={confirm}
        />
      </div>
    </section>
  );
}
