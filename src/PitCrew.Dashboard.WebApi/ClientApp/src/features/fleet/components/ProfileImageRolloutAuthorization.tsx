import { useState } from 'react';
import { Link } from 'react-router-dom';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import type { FleetNode, ManagerObservedState } from '@/core/fleet';
import type { ImageCandidate } from '@/core/images/imageCandidatesApi';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { StateBanner } from '@/core/ui/StateBanner';

import type { ProfileImageRolloutControl } from '../imageRolloutApi';
import { ProfileImageInvariantLedger } from '../ProfileImageRolloutEvidence';

interface ProfileImageRolloutAuthorizationProps {
  readonly tenantId: string;
  readonly node: FleetNode;
  readonly profile: ManagerObservedState;
  readonly control: ProfileImageRolloutControl | null;
  readonly candidate: ImageCandidate | null;
  readonly canAdminister: boolean;
  readonly canSubmit: boolean;
  readonly submitting: boolean;
  readonly reasons: ReadonlyArray<string>;
  readonly mutationError: string | null;
  readonly mutationStatus: string | null;
  readonly onConfirm: (idempotencyKey: string) => Promise<boolean>;
}

/** Keeps preserved invariants and exact consequential confirmation in one task column. */
export function ProfileImageRolloutAuthorization({
  tenantId,
  node,
  profile,
  control,
  candidate,
  canAdminister,
  canSubmit,
  submitting,
  reasons,
  mutationError,
  mutationStatus,
  onConfirm,
}: ProfileImageRolloutAuthorizationProps) {
  const [open, setOpen] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [idempotencyKey] = useState(() => globalThis.crypto.randomUUID());

  const confirm = async () => {
    if (await onConfirm(idempotencyKey)) {
      setOpen(false);
      setAcknowledged(false);
    }
  };

  return (
    <div className="grid min-w-0 content-start gap-4">
      <ProfileImageInvariantLedger control={control} />
      <section className="rounded-xl border bg-card p-4 sm:p-5">
        <h2 className="text-base font-semibold">Authorize changeover</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Confirmation is bound to the selected candidate and the exact current profile fences.
        </p>
        {!canAdminister ? (
          <StateBanner className="mt-4" tone="caution">
            Viewer access is read-only. A tenant administrator must authorize the rollout.
          </StateBanner>
        ) : null}
        {reasons.length > 0 ? (
          <ul className="mt-4 grid gap-2 text-sm text-muted-foreground">
            {reasons.map((reason) => (
              <li key={reason}>{reason}</li>
            ))}
          </ul>
        ) : null}
        {mutationError ? (
          <StateBanner className="mt-4" tone="critical">
            {mutationError}
          </StateBanner>
        ) : null}
        {mutationStatus ? (
          <p className="mt-4 text-sm text-muted-foreground" role="status">
            {mutationStatus}
          </p>
        ) : null}
        <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t pt-4">
          <Link
            className="text-sm text-link underline-offset-4 hover:underline"
            to={`/tenants/${encodeURIComponent(tenantId)}/images/candidates`}
          >
            Review all candidate evidence
          </Link>
          <ConfirmActionDialog
            open={open}
            onOpenChange={(nextOpen) => {
              setOpen(nextOpen);
              if (!nextOpen) setAcknowledged(false);
            }}
            trigger={
              <Button disabled={!canSubmit} type="button">
                {submitting ? 'Queueing…' : 'Roll out image'}
              </Button>
            }
            title={`Roll out ${candidate?.recipeId ?? 'selected image'}?`}
            description="PitCrew will apply one immutable image to this profile through the locally constrained host connector."
            confirmLabel={submitting ? 'Queueing…' : 'Roll out image'}
            confirmDisabled={!acknowledged || submitting}
            details={
              control && candidate ? (
                <ConfirmationSummary
                  identity={[
                    { label: 'Node', value: node.displayName },
                    { label: 'Profile', value: profile.profileId },
                    { label: 'Candidate', value: candidate.recipeId },
                    {
                      label: 'Target digest',
                      value: (
                        <span className="break-all font-mono text-xs">{candidate.digest}</span>
                      ),
                    },
                  ]}
                  fences={[
                    {
                      label: 'Current digest',
                      value: control.currentImageDigest ?? 'Unavailable',
                    },
                    {
                      label: 'Worker revision',
                      value: control.currentWorkerRevision ?? 'Unavailable',
                    },
                    { label: 'Static profile', value: control.staticFingerprint },
                    {
                      label: 'Preserved configuration',
                      value: control.preservedConfigurationFingerprint,
                    },
                    { label: 'Routing', value: control.routingFingerprint },
                    {
                      label: 'Desired state',
                      value: `${control.desiredGeneration} · ${control.desiredStateHash ?? 'Unavailable'}`,
                    },
                    { label: 'Request key', value: idempotencyKey },
                  ]}
                  effects={[
                    'Apply only the approved digest-qualified image to this existing profile.',
                    'Report durable started, terminal, current-worker, and stale-worker evidence.',
                    'Allow compatible busy workers to finish naturally on their prior revision.',
                  ]}
                  prohibitedEffects={[
                    'No job cancellation, capacity change, routing change, or credential replacement.',
                    'No arbitrary registry, command, path, workflow, or automatic retry after started.',
                    'No automatic rollback or fleet campaign.',
                  ]}
                  acknowledgement={{
                    label:
                      'I verified the exact candidate, profile, and current fences for this image-only change.',
                    checked: acknowledged,
                    onCheckedChange: setAcknowledged,
                    testId: 'profile-image-rollout-acknowledgement',
                  }}
                />
              ) : null
            }
            onConfirm={confirm}
          />
        </div>
      </section>
    </div>
  );
}
