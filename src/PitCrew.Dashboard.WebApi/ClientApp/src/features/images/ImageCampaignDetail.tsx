import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { FormField } from '@/core/ui/FormField';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import {
  approveImageCampaignWave,
  cancelImageCampaign,
  configureImageCampaign,
  createImageCampaignRollback,
  maximumImageCampaignWaveSize,
  type ImageCampaign,
  pauseImageCampaign,
  resumeImageCampaign,
} from './imageCampaignApi';
import { ImageCampaignAuthorization } from './ImageCampaignAuthorization';
import { ImageCampaignMutationAuthorization } from './ImageCampaignMutationAuthorization';
import { ImageCampaignTargetList } from './ImageCampaignTargetList';
import {
  campaignStatusTone,
  canCreateRollback,
  formatCampaignState,
  nextPendingWave,
} from './imageCampaignView';

interface ImageCampaignDetailProps {
  readonly tenantId: string;
  readonly campaign: ImageCampaign;
  readonly canAdminister: boolean;
  readonly antiforgeryToken: string;
  readonly refreshBlocked: boolean;
  readonly onCampaignChanged: (campaign: ImageCampaign) => void;
}

/** Composes one frozen campaign plan, approval gates, waves, and target evidence. */
export function ImageCampaignDetail({
  tenantId,
  campaign,
  canAdminister,
  antiforgeryToken,
  refreshBlocked,
  onCampaignChanged,
}: ImageCampaignDetailProps) {
  const eligibleTargets = campaign.targets.filter((target) => target.exclusionCategory === null);
  const [canaryTargetId, setCanaryTargetId] = useState(eligibleTargets[0]?.targetId ?? '');
  const [waveSize, setWaveSize] = useState(Math.max(1, Math.min(10, eligibleTargets.length - 1)));
  const [submitting, setSubmitting] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [mutationStatus, setMutationStatus] = useState<string | null>(null);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [cancelAcknowledged, setCancelAcknowledged] = useState(false);
  const [keys] = useState(() => ({
    configure: globalThis.crypto.randomUUID(),
    approve: globalThis.crypto.randomUUID(),
    pause: globalThis.crypto.randomUUID(),
    resume: globalThis.crypto.randomUUID(),
    cancel: globalThis.crypto.randomUUID(),
    rollback: globalThis.crypto.randomUUID(),
  }));
  const currentWave = campaign.waves.find(
    (wave) => wave.status === 'approved' || wave.status === 'running',
  );
  const pendingWave = nextPendingWave(campaign);
  const selectedCanary = eligibleTargets.find((target) => target.targetId === canaryTargetId);
  const summary = useMemo(
    () => ({
      eligible: eligibleTargets.length,
      excluded: campaign.targets.length - eligibleTargets.length,
      complete: campaign.targets.filter((target) => target.status === 'complete').length,
      adverse: campaign.targets.filter((target) =>
        ['failed', 'blocked', 'indeterminate'].includes(target.status),
      ).length,
    }),
    [campaign.targets, eligibleTargets.length],
  );
  const campaignIdentity = [
    { label: 'Campaign', value: campaign.campaignId },
    { label: 'Current state', value: formatCampaignState(campaign.status) },
  ];
  const mutationFences = (requestKey: string) => [
    { label: 'Target set', value: campaign.targetSetHash },
    { label: 'Campaign revision', value: String(campaign.revision) },
    { label: 'Request key', value: requestKey },
  ];

  const mutate = async (operation: () => Promise<ImageCampaign>, success: string) => {
    setSubmitting(true);
    setMutationError(null);
    setMutationStatus(null);
    try {
      const updated = await operation();
      onCampaignChanged(updated);
      setMutationStatus(success);
      return true;
    } catch (caught) {
      setMutationError(caught instanceof Error ? caught.message : 'Campaign mutation failed.');
      return false;
    } finally {
      setSubmitting(false);
    }
  };

  const configure = async () =>
    await mutate(
      () =>
        configureImageCampaign(
          tenantId,
          campaign.campaignId,
          {
            canaryTargetId: eligibleTargets.length === 1 ? null : canaryTargetId,
            waveSize,
            expectedRevision: campaign.revision,
            expectedTargetSetHash: campaign.targetSetHash,
          },
          keys.configure,
          antiforgeryToken,
        ),
      'Canary and wave assignment are frozen.',
    );

  const approve = async (idempotencyKey: string) => {
    const wave = nextPendingWave(campaign);
    if (wave === undefined) return false;
    return await mutate(
      () =>
        approveImageCampaignWave(
          tenantId,
          campaign.campaignId,
          wave.waveNumber,
          {
            expectedRevision: campaign.revision,
            expectedTargetSetHash: campaign.targetSetHash,
          },
          idempotencyKey,
          antiforgeryToken,
        ),
      `${wave.waveNumber === 0 ? 'Canary' : `Wave ${wave.waveNumber}`} approved.`,
    );
  };

  const changeState = async (action: 'pause' | 'resume' | 'cancel') => {
    const operation = {
      pause: pauseImageCampaign,
      resume: resumeImageCampaign,
      cancel: cancelImageCampaign,
    }[action];
    const successMessage = {
      pause: 'Pause recorded. Existing profile commands continue to terminal evidence.',
      resume: 'Resume recorded. Existing profile commands have continued to terminal evidence.',
      cancel: 'Cancellation recorded. Existing profile commands continue to terminal evidence.',
    }[action];
    return await mutate(
      () =>
        operation(
          tenantId,
          campaign.campaignId,
          {
            expectedRevision: campaign.revision,
            expectedTargetSetHash: campaign.targetSetHash,
          },
          keys[action],
          antiforgeryToken,
        ),
      successMessage,
    );
  };

  const createRollback = async () =>
    await mutate(
      () =>
        createImageCampaignRollback(tenantId, campaign.campaignId, keys.rollback, antiforgeryToken),
      'A separate rollback campaign draft was created.',
    );

  const mutationsDisabled = refreshBlocked || submitting;
  return (
    <div className="grid min-w-0 gap-5">
      <section className="grid min-w-0 gap-4" aria-labelledby="campaign-detail-heading">
        <div className="flex flex-wrap items-start justify-between gap-3 border-b pb-4">
          <div className="min-w-0">
            <div className="flex min-w-0 flex-wrap items-center gap-2">
              <h2
                className="min-w-0 [overflow-wrap:anywhere] text-lg font-semibold"
                id="campaign-detail-heading"
              >
                {campaign.kind === 'forward'
                  ? `${campaign.candidate?.recipeId ?? 'Image'} campaign`
                  : 'Rollback campaign'}
              </h2>
              <StatusBadge
                status={formatCampaignState(campaign.status)}
                tone={campaignStatusTone(campaign.status)}
              />
            </div>
            <p className="mt-1 max-w-[72ch] text-sm text-muted-foreground">
              Frozen target set {campaign.targetSetHash.slice(0, 12)} · revision {campaign.revision}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {campaign.candidate ? (
              <Link
                className="text-sm text-link underline-offset-4 hover:underline"
                to={`/tenants/${encodeURIComponent(tenantId)}/images/candidates?candidate=${encodeURIComponent(campaign.candidate.candidateId)}`}
              >
                Candidate evidence
              </Link>
            ) : null}
            {canAdminister &&
            (campaign.status === 'running' || campaign.status === 'awaiting-approval') ? (
              <ImageCampaignMutationAuthorization
                triggerLabel="Pause"
                pendingLabel="Pausing…"
                title="Pause future campaign dispatch?"
                description="PitCrew will stop granting new target dispatch leases for this campaign."
                disabled={mutationsDisabled}
                submitting={submitting}
                identity={campaignIdentity}
                fences={mutationFences(keys.pause)}
                effects={['Pause future target dispatch for the current campaign wave.']}
                prohibitedEffects={[
                  'Does not withdraw or stop an existing profile-image command.',
                  'Does not approve another wave, cancel the campaign, or create rollback work.',
                ]}
                acknowledgementLabel="I understand existing profile commands continue to terminal evidence."
                acknowledgementTestId="image-campaign-pause-acknowledgement"
                onConfirm={() => changeState('pause')}
              />
            ) : null}
            {canAdminister && campaign.status === 'paused' ? (
              <ImageCampaignMutationAuthorization
                triggerLabel="Resume"
                pendingLabel="Resuming…"
                title="Resume campaign dispatch?"
                description="PitCrew will allow the current approved wave to continue dispatching."
                disabled={mutationsDisabled}
                submitting={submitting}
                identity={campaignIdentity}
                fences={mutationFences(keys.resume)}
                effects={['Resume future target dispatch for the current approved wave.']}
                prohibitedEffects={[
                  'Does not approve a pending later wave.',
                  'Does not retry adverse targets or change the frozen target set.',
                ]}
                acknowledgementLabel="I reviewed the current campaign state and frozen target set."
                acknowledgementTestId="image-campaign-resume-acknowledgement"
                onConfirm={() => changeState('resume')}
              />
            ) : null}
          </div>
        </div>

        {refreshBlocked ? (
          <StateBanner tone="caution">
            Showing retained campaign evidence. Mutations remain disabled until refresh succeeds.
          </StateBanner>
        ) : null}
        {!canAdminister ? (
          <StateBanner tone="caution">
            Viewer access is read-only. Campaign mutation controls are not shown.
          </StateBanner>
        ) : null}
        {['running', 'awaiting-approval', 'paused'].includes(campaign.status) ? (
          <p className="text-sm text-muted-foreground">
            Pause and resume change future campaign dispatch only. Existing profile commands
            continue to terminal evidence.
          </p>
        ) : null}
        {mutationError ? <StateBanner tone="critical">{mutationError}</StateBanner> : null}
        {mutationStatus ? (
          <p className="text-sm text-muted-foreground" role="status">
            {mutationStatus}
          </p>
        ) : null}

        <section className="grid gap-3" aria-labelledby="campaign-authority-heading">
          <div>
            <h3 className="text-base font-semibold" id="campaign-authority-heading">
              Frozen campaign authority
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              These identities and fences do not change as fleet evidence evolves.
            </p>
          </div>
          <dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <CampaignFact label="Campaign ID" value={campaign.campaignId} />
            <CampaignFact label="Target set" value={campaign.targetSetHash} />
            <CampaignFact label="Campaign revision" value={String(campaign.revision)} />
            {campaign.sourceCampaignId ? (
              <CampaignFact label="Source campaign" value={campaign.sourceCampaignId} />
            ) : null}
            {campaign.candidate ? (
              <>
                <CampaignFact label="Candidate ID" value={campaign.candidate.candidateId} />
                <CampaignFact label="Recipe" value={campaign.candidate.recipeId} />
                <CampaignFact label="Target digest" value={campaign.candidate.targetDigest} />
                <CampaignFact label="Platform" value={campaign.candidate.targetPlatform} />
              </>
            ) : null}
          </dl>
        </section>

        <section className="grid gap-3" aria-labelledby="campaign-progress-heading">
          <h3 className="text-base font-semibold" id="campaign-progress-heading">
            Campaign progress
          </h3>
          <dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <CampaignFact label="Eligible targets" value={String(summary.eligible)} />
            <CampaignFact label="Excluded targets" value={String(summary.excluded)} />
            <CampaignFact label="Complete targets" value={String(summary.complete)} />
            <CampaignFact label="Adverse targets" value={String(summary.adverse)} />
            <CampaignFact
              label="Current wave"
              value={currentWave ? String(currentWave.waveNumber) : 'None active'}
            />
            <CampaignFact
              label="Next approval"
              value={pendingWave ? `Wave ${pendingWave.waveNumber}` : 'None'}
            />
            <CampaignFact
              label="Wave size"
              value={campaign.waveSize === null ? 'Not configured' : String(campaign.waveSize)}
            />
            <CampaignFact label="Requested by" value={campaign.requestedByGitHubUserId} />
          </dl>
        </section>

        {campaign.status === 'draft' && eligibleTargets.length > 0 ? (
          <section
            className="grid gap-4 border-t pt-4"
            aria-labelledby="campaign-configure-heading"
          >
            <div>
              <h3 className="text-base font-semibold" id="campaign-configure-heading">
                Freeze canary and waves
              </h3>
              <p className="mt-1 max-w-[72ch] text-sm text-muted-foreground">
                This assignment is immutable. Cancel and recreate the draft to choose a different
                plan.
              </p>
            </div>
            {canAdminister ? (
              <>
                <div className="grid gap-4 sm:grid-cols-2">
                  <FormField label="Canary target">
                    <select
                      className="h-11 min-w-0 rounded-md border bg-background px-3 text-sm"
                      value={canaryTargetId}
                      onChange={(event) => setCanaryTargetId(event.target.value)}
                    >
                      {eligibleTargets.map((target) => (
                        <option key={target.targetId} value={target.targetId}>
                          {target.nodeDisplayName} · {target.profileId}
                        </option>
                      ))}
                    </select>
                  </FormField>
                  <FormField
                    label="Later wave size"
                    hint="The canary remains a one-target wave. Every later wave requires approval."
                  >
                    <input
                      className="h-11 min-w-0 rounded-md border bg-background px-3 text-sm"
                      type="number"
                      min={1}
                      max={maximumImageCampaignWaveSize}
                      value={waveSize}
                      onChange={(event) => setWaveSize(Number(event.target.value))}
                    />
                  </FormField>
                </div>
                <div>
                  <ImageCampaignMutationAuthorization
                    triggerLabel="Freeze campaign waves"
                    pendingLabel="Freezing…"
                    title="Freeze canary and wave assignment?"
                    description="This immutable assignment requires a new campaign if the canary or wave size must change."
                    disabled={
                      mutationsDisabled ||
                      canaryTargetId === '' ||
                      waveSize < 1 ||
                      waveSize > maximumImageCampaignWaveSize
                    }
                    submitting={submitting}
                    identity={[
                      ...campaignIdentity,
                      {
                        label: 'Canary',
                        value: selectedCanary
                          ? `${selectedCanary.nodeDisplayName} · ${selectedCanary.nodeId} · ${selectedCanary.profileId}`
                          : 'Unavailable',
                      },
                      { label: 'Later wave size', value: String(waveSize) },
                    ]}
                    fences={mutationFences(keys.configure)}
                    effects={[
                      'Freeze one canary and deterministic later-wave membership for every eligible target.',
                    ]}
                    prohibitedEffects={[
                      'Does not approve the canary or queue any profile-image command.',
                      'Does not add newly discovered targets or remove retained exclusions.',
                    ]}
                    acknowledgementLabel="I reviewed the immutable canary, wave size, target set, and campaign revision."
                    acknowledgementTestId="image-campaign-configure-acknowledgement"
                    onConfirm={configure}
                  />
                </div>
              </>
            ) : null}
          </section>
        ) : null}

        {canAdminister && campaign.status === 'awaiting-approval' ? (
          <ImageCampaignAuthorization
            key={`${campaign.campaignId}:${campaign.revision}`}
            campaign={campaign}
            disabled={refreshBlocked}
            submitting={submitting}
            mutationError={null}
            mutationStatus={null}
            onApprove={approve}
          />
        ) : null}

        <section className="grid gap-3 border-t pt-4" aria-labelledby="campaign-waves-heading">
          <div>
            <h3 className="text-base font-semibold" id="campaign-waves-heading">
              Canary and waves
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              Every later wave remains pending until the previous wave completes and an
              administrator approves it.
            </p>
          </div>
          {campaign.waves.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              Configure this draft to create the immutable wave sequence.
            </p>
          ) : (
            <OperationalList label="Campaign waves">
              {campaign.waves.map((wave) => (
                <OperationalRow
                  key={wave.waveNumber}
                  title={wave.waveNumber === 0 ? 'Canary' : `Wave ${wave.waveNumber}`}
                  description={`${wave.targetCount} target${wave.targetCount === 1 ? '' : 's'}`}
                  status={<StatusBadge status={formatCampaignState(wave.status)} />}
                  metadata={
                    wave.approvedAt ? (
                      <span className="text-xs text-muted-foreground">
                        Approved by {wave.approvedByGitHubUserId}
                      </span>
                    ) : null
                  }
                />
              ))}
            </OperationalList>
          )}
        </section>

        <ImageCampaignTargetList tenantId={tenantId} targets={campaign.targets} />

        {canAdminister ? (
          <section className="flex flex-wrap items-center gap-2 border-t pt-4">
            {canCreateRollback(campaign) ? (
              <ImageCampaignMutationAuthorization
                triggerLabel="Create rollback draft"
                pendingLabel="Creating…"
                title="Create a separate rollback campaign draft?"
                description="PitCrew will freeze a new campaign from proven per-target prior image authority."
                disabled={mutationsDisabled}
                submitting={submitting}
                identity={[
                  ...campaignIdentity,
                  {
                    label: 'Rollback source targets',
                    value: String(
                      campaign.targets.filter(
                        (target) =>
                          target.previousCandidateId !== null &&
                          target.previousImageDigest !== null &&
                          target.previousWorkerRevision !== null,
                      ).length,
                    ),
                  },
                ]}
                fences={mutationFences(keys.rollback)}
                effects={[
                  'Create and open a distinct rollback campaign draft with a new frozen target set.',
                ]}
                prohibitedEffects={[
                  'Does not approve a rollback wave or queue any profile-image command.',
                  'Does not mutate, reopen, or erase this source campaign.',
                ]}
                acknowledgementLabel="I understand this creates reviewable rollback work and does not execute it."
                acknowledgementTestId="image-campaign-rollback-acknowledgement"
                onConfirm={createRollback}
              />
            ) : null}
            {!['complete', 'partial', 'blocked', 'cancelled'].includes(campaign.status) ? (
              <ConfirmActionDialog
                open={cancelOpen}
                onOpenChange={(open) => {
                  setCancelOpen(open);
                  if (!open) setCancelAcknowledged(false);
                }}
                trigger={
                  <Button disabled={mutationsDisabled} type="button" variant="destructive">
                    Cancel future dispatch
                  </Button>
                }
                title="Cancel future campaign dispatch?"
                description="Targets without a durable profile command will be cancelled. Existing commands continue."
                confirmLabel={submitting ? 'Cancelling…' : 'Cancel future dispatch'}
                confirmVariant="destructive"
                confirmDisabled={!cancelAcknowledged || submitting}
                details={
                  <ConfirmationSummary
                    identity={campaignIdentity}
                    fences={mutationFences(keys.cancel)}
                    effects={[
                      'Cancel every frozen target that does not yet have a durable profile command.',
                    ]}
                    prohibitedEffects={[
                      'Does not withdraw queued, claimed, applying, rolling, or indeterminate profile commands.',
                      'Does not roll back any target or cancel GitHub work.',
                    ]}
                    acknowledgement={{
                      label:
                        'I understand existing profile commands continue to terminal evidence.',
                      checked: cancelAcknowledged,
                      onCheckedChange: setCancelAcknowledged,
                      testId: 'image-campaign-cancel-acknowledgement',
                    }}
                  />
                }
                onConfirm={async () => {
                  if (await changeState('cancel')) {
                    setCancelOpen(false);
                    setCancelAcknowledged(false);
                  }
                }}
              />
            ) : null}
          </section>
        ) : null}
      </section>
    </div>
  );
}

function CampaignFact({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-1 min-w-0 [overflow-wrap:anywhere] text-sm font-semibold">{value}</dd>
    </div>
  );
}
