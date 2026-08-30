import type {
  ImageCampaign,
  ImageCampaignStatus,
  ImageCampaignSummary,
  ImageCampaignTarget,
  ImageCampaignTargetStatus,
} from './imageCampaignApi';

export type CampaignTone = 'positive' | 'caution' | 'critical' | 'neutral';

/** Formats one campaign protocol state as operator-facing sentence case. */
export function formatCampaignState(value: string): string {
  const words = value.replaceAll('-', ' ');
  return `${words.charAt(0).toUpperCase()}${words.slice(1)}`;
}

/** Returns the semantic tone for one campaign lifecycle state. */
export function campaignStatusTone(status: ImageCampaignStatus): CampaignTone {
  switch (status) {
    case 'complete':
      return 'positive';
    case 'draft':
    case 'awaiting-approval':
    case 'running':
    case 'paused':
      return 'caution';
    case 'partial':
    case 'blocked':
      return 'critical';
    case 'cancelled':
      return 'neutral';
  }
}

/** Returns the semantic tone for one campaign target state. */
export function campaignTargetTone(status: ImageCampaignTargetStatus): CampaignTone {
  switch (status) {
    case 'complete':
    case 'eligible':
      return 'positive';
    case 'queued':
    case 'claimed':
    case 'applying':
    case 'rolling':
      return 'caution';
    case 'failed':
    case 'blocked':
    case 'indeterminate':
      return 'critical';
    case 'excluded':
    case 'cancelled':
      return 'neutral';
  }
}

/** Orders campaigns by operator attention before ordinary terminal history. */
export function campaignAttentionRank(campaign: ImageCampaignSummary): number {
  switch (campaign.status) {
    case 'blocked':
    case 'partial':
      return 0;
    case 'awaiting-approval':
    case 'paused':
      return 1;
    case 'running':
      return 2;
    case 'draft':
      return 3;
    case 'cancelled':
      return 4;
    case 'complete':
      return 100;
  }
}

/** Orders active/adverse targets before eligible, excluded, and completed records. */
export function campaignTargetAttentionRank(target: ImageCampaignTarget): number {
  switch (target.status) {
    case 'failed':
    case 'blocked':
    case 'indeterminate':
      return 0;
    case 'claimed':
    case 'applying':
    case 'rolling':
      return 1;
    case 'queued':
      return 2;
    case 'eligible':
      return 3;
    case 'excluded':
      return 4;
    case 'cancelled':
      return 5;
    case 'complete':
      return 100;
  }
}

/** Returns the next pending wave awaiting explicit approval. */
export function nextPendingWave(campaign: ImageCampaign) {
  return campaign.waves
    .filter((wave) => wave.status === 'pending')
    .sort((left, right) => left.waveNumber - right.waveNumber)[0];
}

/** Returns whether the campaign can create an explicit rollback draft. */
export function canCreateRollback(campaign: ImageCampaign): boolean {
  return (
    (campaign.status === 'complete' || campaign.status === 'partial') &&
    campaign.targets.some(
      (target) =>
        target.status === 'complete' &&
        target.previousCandidateId !== null &&
        target.previousRecipeId !== null &&
        target.previousImageDigest !== null &&
        target.previousWorkerRevision !== null,
    )
  );
}
