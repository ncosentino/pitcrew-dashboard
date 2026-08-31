import type { ImageCandidate } from '@/core/images/imageCandidatesApi';

import type { ImageRolloutCommandStatus, ProfileImageRolloutControl } from './imageRolloutApi';

export interface ProfileImageCandidateCompatibility {
  readonly eligible: boolean;
  readonly alreadyCurrent: boolean;
  readonly reasons: ReadonlyArray<string>;
}

/** Evaluates one candidate against the connector's current rollout authority. */
export function describeCandidateCompatibility(
  candidate: ImageCandidate,
  control: ProfileImageRolloutControl | null,
  nodeOnline: boolean,
): ProfileImageCandidateCompatibility {
  const reasons: string[] = [];

  if (candidate.outcome !== 'ready') {
    reasons.push('The candidate did not complete qualification successfully.');
  }
  if (candidate.outputMode !== 'registry') {
    reasons.push('Only registry-backed candidates can be rolled out.');
  }
  if (candidate.digest === null || candidate.immutableReference === null) {
    reasons.push('The candidate does not include a qualified immutable registry identity.');
  }
  if (control === null) {
    reasons.push('The connector does not advertise rollout authority for this profile.');
    return { eligible: false, alreadyCurrent: false, reasons };
  }

  const alreadyCurrent =
    candidate.digest !== null &&
    (candidate.digest === control.currentImageDigest ||
      candidate.digest === control.currentLocalImageId ||
      candidate.immutableReference === control.currentImageReference);

  if (alreadyCurrent) {
    reasons.push('The selected candidate is already current for this profile.');
  }
  if (candidate.platform !== control.architecture) {
    reasons.push(
      `Candidate platform ${candidate.platform} does not match profile architecture ${control.architecture}.`,
    );
  }
  const candidateRecipeId = candidate.recipeId.toLowerCase();
  if (!control.allowedRecipeIds.some((recipeId) => recipeId.toLowerCase() === candidateRecipeId)) {
    reasons.push('Connector-local policy does not allow this candidate recipe.');
  }
  if (!nodeOnline) {
    reasons.push('The connector node is offline.');
  }
  if (!control.localSchemaSupported) {
    reasons.push('The connector cannot reconstruct this profile with the supported schema.');
  }
  if (control.localFailureCategory !== null) {
    reasons.push(
      `The connector reports ${formatImageRolloutState(control.localFailureCategory).toLowerCase()}.`,
    );
  } else if (!control.rolloutAllowed) {
    reasons.push('Connector-local policy does not allow image rollout for this profile.');
  }
  if (!control.observedStateFresh) {
    reasons.push('Current profile evidence is stale.');
  }
  if (control.operationActive) {
    reasons.push('Another capacity, recovery, or image operation is active for this profile.');
  }

  return {
    eligible: reasons.length === 0,
    alreadyCurrent,
    reasons,
  };
}

/** Formats a protocol state as sentence-case operator copy. */
export function formatImageRolloutState(value: string): string {
  const words = value.replaceAll('-', ' ');
  return `${words.charAt(0).toUpperCase()}${words.slice(1)}`;
}

/** Returns the semantic badge tone for one durable rollout command state. */
export function imageRolloutStatusTone(
  status: ImageRolloutCommandStatus,
): 'positive' | 'caution' | 'critical' | 'neutral' {
  switch (status) {
    case 'succeeded':
      return 'positive';
    case 'queued':
    case 'claimed':
    case 'started':
      return 'caution';
    case 'rejected':
    case 'failed':
    case 'expired':
    case 'indeterminate':
      return 'critical';
  }
}

/** Orders active and uncertain rollout commands before settled history. */
export function commandAttentionRank(status: ImageRolloutCommandStatus): number {
  switch (status) {
    case 'queued':
    case 'claimed':
    case 'started':
      return 0;
    case 'indeterminate':
      return 1;
    case 'rejected':
    case 'failed':
    case 'expired':
      return 2;
    case 'succeeded':
      return 100;
  }
}

/** Produces a compact display identity while preserving the full value in callers. */
export function shortImageIdentity(value: string | null): string {
  if (value === null) {
    return 'Unavailable';
  }
  const identity = value.startsWith('sha256:') ? value.slice(7) : value;
  return identity.slice(0, 12);
}
