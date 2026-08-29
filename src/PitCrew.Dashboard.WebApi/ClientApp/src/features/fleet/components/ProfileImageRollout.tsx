import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';

import { ApiError } from '@/core/api/httpClient';
import type { FleetNode, ManagerObservedState } from '@/core/fleet';
import {
  getImageCandidate,
  getImageCandidates,
  type ImageCandidate,
} from '@/core/images/imageCandidatesApi';
import { LoadingState } from '@/core/ui/LoadingState';
import { ReadinessSummary } from '@/core/ui/ReadinessSummary';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import {
  getProfileImageRollout,
  rollOutProfileImage,
  type ProfileImageRolloutControl,
} from '../imageRolloutApi';
import { ProfileImageCandidateList } from '../ProfileImageCandidateList';
import {
  ProfileImageChangeoverLane,
  ProfileImageRolloutCommandSummary,
  ProfileImageRolloutHistory,
} from '../ProfileImageRolloutEvidence';
import { describeCandidateCompatibility, formatImageRolloutState } from '../profileRolloutView';
import { ProfileImageRolloutAuthorization } from './ProfileImageRolloutAuthorization';
import { ProfileWorkerUpdateSummary } from './ProfileWorkerUpdateSummary';

interface ProfileImageRolloutProps {
  readonly tenantId: string;
  readonly node: FleetNode;
  readonly profile: ManagerObservedState;
  readonly canAdminister: boolean;
  readonly antiforgeryToken: string;
}

interface ProfileImageRolloutProjection {
  readonly control: ProfileImageRolloutControl | null;
  readonly candidates: ReadonlyArray<ImageCandidate>;
  readonly candidatesTruncated: boolean;
  readonly selectedCandidate: ImageCandidate | null;
  readonly selectedCandidateMissing: boolean;
}

/** Runs one candidate-to-profile changeover from exact readiness through convergence evidence. */
export function ProfileImageRollout({
  tenantId,
  node,
  profile,
  canAdminister,
  antiforgeryToken,
}: ProfileImageRolloutProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedCandidateId = searchParams.get('candidate');
  const [projection, setProjection] = useState<ProfileImageRolloutProjection | null>(null);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [mutationError, setMutationError] = useState<{
    readonly identity: string;
    readonly message: string;
  } | null>(null);
  const [mutationStatus, setMutationStatus] = useState<{
    readonly identity: string;
    readonly message: string;
  } | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    let requestVersion = 0;
    const load = async () => {
      const version = requestVersion + 1;
      requestVersion = version;
      try {
        const [control, candidateList] = await Promise.all([
          loadRolloutControl(tenantId, node.nodeId, profile.profileId, controller.signal),
          getImageCandidates(tenantId, controller.signal),
        ]);
        let selectedCandidate =
          candidateList.candidates.find(
            (candidate) => candidate.candidateId === selectedCandidateId,
          ) ?? null;
        let selectedCandidateMissing = false;
        if (selectedCandidateId !== null && selectedCandidate === null) {
          try {
            selectedCandidate = await getImageCandidate(
              tenantId,
              selectedCandidateId,
              controller.signal,
            );
          } catch (caught) {
            if (caught instanceof ApiError && caught.status === 404) {
              selectedCandidateMissing = true;
            } else {
              throw caught;
            }
          }
        }
        if (controller.signal.aborted || requestVersion !== version) return;
        setProjection({
          control,
          candidates: candidateList.candidates,
          candidatesTruncated: candidateList.truncated,
          selectedCandidate,
          selectedCandidateMissing,
        });
        setRefreshError(null);
      } catch (caught) {
        if (controller.signal.aborted || requestVersion !== version) return;
        setRefreshError(
          caught instanceof Error ? caught.message : 'Profile image rollout could not be loaded.',
        );
      }
    };
    void load();
    const timer = globalThis.setInterval(() => void load(), 8_000);
    return () => {
      globalThis.clearInterval(timer);
      controller.abort();
    };
  }, [node.nodeId, profile.profileId, refreshVersion, selectedCandidateId, tenantId]);

  useEffect(() => {
    if (projection === null || selectedCandidateId !== null) return;
    const first =
      projection.candidates.find(
        (candidate) =>
          describeCandidateCompatibility(candidate, projection.control, node.isOnline).eligible,
      ) ??
      projection.candidates.find(
        (candidate) =>
          candidate.outcome === 'ready' &&
          candidate.outputMode === 'registry' &&
          candidate.digest !== null,
      );
    if (!first) return;
    setSearchParams({ candidate: first.candidateId }, { replace: true });
  }, [node.isOnline, projection, selectedCandidateId, setSearchParams]);

  const control = projection?.control ?? null;
  const candidate = projection?.selectedCandidate ?? null;
  const compatibility =
    candidate === null
      ? { eligible: false, alreadyCurrent: false, reasons: ['Select a ready candidate.'] }
      : describeCandidateCompatibility(candidate, control, node.isOnline);
  const authorizationReasons =
    refreshError === null
      ? compatibility.reasons
      : [...compatibility.reasons, 'Current rollout evidence could not be refreshed.'];
  const authorityIdentity = useMemo(
    () =>
      control && candidate
        ? JSON.stringify([
            tenantId,
            node.nodeId,
            profile.profileId,
            candidate.candidateId,
            control.currentImageReference,
            control.currentImageDigest,
            control.currentLocalImageId,
            control.currentWorkerRevision,
            control.staticFingerprint,
            control.preservedConfigurationFingerprint,
            control.routingFingerprint,
            control.desiredGeneration,
            control.desiredStateHash,
            control.latestCommand?.commandId ?? null,
          ])
        : '',
    [candidate, control, node.nodeId, profile.profileId, tenantId],
  );
  if (projection === null && refreshError === null) {
    return <LoadingState label="Loading profile image rollout" />;
  }

  const readiness = describeReadiness(
    control,
    candidate,
    authorizationReasons,
    node.isOnline,
    refreshError !== null,
  );
  const latestCommand = control?.latestCommand ?? null;
  const previousCommands =
    control?.recentCommands.filter((command) => command.commandId !== latestCommand?.commandId) ??
    [];
  const canSubmit =
    canAdminister &&
    compatibility.eligible &&
    refreshError === null &&
    !submitting &&
    control !== null &&
    candidate !== null;

  const submit = async (idempotencyKey: string): Promise<boolean> => {
    if (!canSubmit || control === null || candidate === null) {
      return false;
    }
    setSubmitting(true);
    setMutationError(null);
    setMutationStatus(null);
    try {
      const response = await rollOutProfileImage(
        tenantId,
        {
          nodeId: node.nodeId,
          profileId: profile.profileId,
          candidateId: candidate.candidateId,
          expectedCurrentImageReference: control.currentImageReference,
          expectedCurrentImageDigest: control.currentImageDigest,
          expectedCurrentLocalImageId: control.currentLocalImageId,
          expectedCurrentWorkerRevision: control.currentWorkerRevision,
          expectedStaticFingerprint: control.staticFingerprint,
          expectedPreservedConfigurationFingerprint: control.preservedConfigurationFingerprint,
          expectedRoutingFingerprint: control.routingFingerprint,
          expectedDesiredGeneration: control.desiredGeneration,
          expectedDesiredStateHash: control.desiredStateHash,
        },
        idempotencyKey,
        antiforgeryToken,
      );
      setMutationStatus({
        identity: authorityIdentity,
        message: `Rollout command ${response.commandId} is queued.`,
      });
      setRefreshVersion((current) => current + 1);
      return true;
    } catch (caught) {
      setMutationError({
        identity: authorityIdentity,
        message:
          caught instanceof Error
            ? caught.message
            : 'The profile image rollout could not be queued.',
      });
      return false;
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <section className="grid min-w-0 gap-4" aria-labelledby="profile-image-rollout-heading">
      <div>
        <h2 className="text-lg font-semibold" id="profile-image-rollout-heading">
          Profile image rollout
        </h2>
        <p className="mt-1 max-w-[72ch] text-sm text-muted-foreground">
          Apply one qualified immutable image while preserving this profile's routing, capacity,
          admission, runtime, and credential boundaries.
        </p>
      </div>

      <ReadinessSummary
        title="Rollout readiness"
        description="Current connector capability, local policy, architecture, and shared-operation evidence."
        status={<StatusBadge status={readiness.label} tone={readiness.tone} />}
        items={[
          {
            label: 'Connector support',
            value: control
              ? control.localSchemaSupported
                ? 'Supported'
                : 'Unavailable'
              : 'Not advertised',
            detail: control
              ? control.localFailureCategory
                ? formatImageRolloutState(control.localFailureCategory)
                : 'Typed protocol v11 capability'
              : 'Connector did not advertise rollout for this profile',
          },
          {
            label: 'Observation',
            value: control
              ? `${control.observedStateAgeSeconds}s old`
              : refreshError
                ? 'Unavailable'
                : 'Not advertised',
            detail: control?.observedStateFresh ? 'Fresh rollout evidence' : 'Not current',
          },
          {
            label: 'Candidate',
            value: candidate?.recipeId ?? 'Not selected',
            detail: candidate?.digest ?? 'Select a ready registry candidate',
          },
          {
            label: 'Operation slot',
            value: control?.operationActive ? 'In use' : control ? 'Available' : 'Unavailable',
            detail: 'Shared with capacity and manager recovery',
          },
        ]}
      />

      {refreshError ? (
        <StateBanner tone={projection ? 'caution' : 'critical'}>
          {projection
            ? `Showing retained rollout evidence because refresh failed: ${refreshError}`
            : `Profile image rollout is unavailable: ${refreshError}`}
        </StateBanner>
      ) : null}
      {projection?.selectedCandidateMissing ? (
        <StateBanner tone="critical">
          The selected candidate is not present in this tenant's retained evidence. No substitute
          candidate was selected.
        </StateBanner>
      ) : null}
      {latestCommand ? (
        <ProfileImageRolloutCommandSummary tenantId={tenantId} command={latestCommand} />
      ) : null}

      <ProfileImageChangeoverLane tenantId={tenantId} control={control} candidate={candidate} />

      <div className="grid min-w-0 gap-4 xl:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
        <ProfileImageCandidateList
          tenantId={tenantId}
          nodeOnline={node.isOnline}
          control={control}
          candidates={projection?.candidates ?? []}
          selectedCandidateId={selectedCandidateId}
          truncated={projection?.candidatesTruncated ?? false}
        />
        <ProfileImageRolloutAuthorization
          key={authorityIdentity || 'rollout-unavailable'}
          tenantId={tenantId}
          node={node}
          profile={profile}
          control={control}
          candidate={candidate}
          canAdminister={canAdminister}
          canSubmit={canSubmit}
          submitting={submitting}
          reasons={authorizationReasons}
          mutationError={
            mutationError?.identity === authorityIdentity ? mutationError.message : null
          }
          mutationStatus={
            mutationStatus?.identity === authorityIdentity ? mutationStatus.message : null
          }
          onConfirm={submit}
        />
      </div>

      <ProfileWorkerUpdateSummary profile={profile} />

      <section className="grid gap-3" aria-labelledby="profile-image-history-heading">
        <div>
          <h2 className="text-base font-semibold" id="profile-image-history-heading">
            Previous rollout commands
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Bounded immutable command history; complete candidate evidence remains in Runner images.
          </p>
        </div>
        <ProfileImageRolloutHistory tenantId={tenantId} commands={previousCommands} />
      </section>
    </section>
  );
}

async function loadRolloutControl(
  tenantId: string,
  nodeId: string,
  profileId: string,
  signal: AbortSignal,
): Promise<ProfileImageRolloutControl | null> {
  try {
    return await getProfileImageRollout(tenantId, nodeId, profileId, signal);
  } catch (caught) {
    if (
      caught instanceof ApiError &&
      caught.status === 404 &&
      caught.code === 'image_rollout_profile_not_found'
    ) {
      return null;
    }
    throw caught;
  }
}

function describeReadiness(
  control: ProfileImageRolloutControl | null,
  candidate: ImageCandidate | null,
  reasons: ReadonlyArray<string>,
  nodeOnline: boolean,
  refreshFailed: boolean,
): { readonly label: string; readonly tone: 'positive' | 'caution' | 'critical' | 'neutral' } {
  if (!nodeOnline) return { label: 'Node offline', tone: 'caution' };
  if (control === null) return { label: 'Rollout unavailable', tone: 'neutral' };
  if (!control.observedStateFresh) return { label: 'Stale evidence', tone: 'critical' };
  if (refreshFailed) return { label: 'Refresh failed', tone: 'caution' };
  if (control.operationActive) return { label: 'Operation active', tone: 'caution' };
  if (candidate === null) return { label: 'Candidate required', tone: 'neutral' };
  if (reasons.length > 0) return { label: 'Not ready', tone: 'critical' };
  return { label: 'Ready to authorize', tone: 'positive' };
}
