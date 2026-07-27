import { useState } from 'react';
import { useParams } from 'react-router-dom';

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';
import { useFleet } from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ProfileCapacityEvidence } from '../components/ProfileCapacityEvidence';
import { ProfileCapacitySummary } from '../components/ProfileCapacitySummary';
import { ProfileManagerRecovery } from '../components/ProfileManagerRecovery';
import { ProfileOperationJournal } from '../components/ProfileOperationJournal';
import { ProfileResourcePolicy } from '../components/ProfileResourcePolicy';
import { ProfileResourceTelemetry } from '../components/ProfileResourceTelemetry';
import { ProfileSlotsTable } from '../components/ProfileSlotsTable';
import { ProfileSubsystemHealth } from '../components/ProfileSubsystemHealth';
import { ProfileTargetsTable } from '../components/ProfileTargetsTable';
import { recoverManager, setCapacityMaximum } from '../fleetApi';
import { isRecoveryCommandActive, type RecoveryFences } from '../managerRecovery';

/** Renders one profile from the shared tenant fleet projection. */
export default function ProfileDetailPage() {
  const { tenantId = '', nodeId = '', profileId = '' } = useParams();
  const { session } = useSession();
  const { fleet, error, isLoading, refreshNow } = useFleet();
  const [mutation, setMutation] = useState<'capacity' | 'recovery' | null>(null);
  const [mutationError, setMutationError] = useState<string | null>(null);

  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);
  const canAdminister = tenant?.role === 'administrator' || tenant?.role === 'owner';
  const node = fleet?.nodes.find((candidate) => candidate.nodeId === nodeId);
  const profile = node?.profiles.find((candidate) => candidate.profileId === profileId);
  const capacityControl =
    node?.capacityControls.find((candidate) => candidate.profileId === profileId) ?? null;
  const recoveryControl =
    node?.recoveryControls.find((candidate) => candidate.profileId === profileId) ?? null;
  const isMutating = mutation !== null;
  const recoveryActive = isRecoveryCommandActive(recoveryControl?.latestCommand ?? null);

  const queueCapacityMaximum = async (maximum: number) => {
    if (!session) return;
    setMutation('capacity');
    setMutationError(null);
    try {
      await setCapacityMaximum(tenantId, nodeId, profileId, maximum, session.antiforgeryToken);
      await refreshNow();
    } catch (caught) {
      setMutationError(
        caught instanceof Error ? caught.message : 'Capacity maximum could not be queued.',
      );
    } finally {
      setMutation(null);
    }
  };

  const queueManagerRecovery = async (fences: RecoveryFences) => {
    if (!session) return;
    setMutation('recovery');
    setMutationError(null);
    try {
      await recoverManager(tenantId, nodeId, profileId, fences, session.antiforgeryToken);
      await refreshNow();
    } catch (caught) {
      setMutationError(
        caught instanceof Error ? caught.message : 'Manager recovery could not be queued.',
      );
    } finally {
      setMutation(null);
    }
  };

  return (
    <section className="grid gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-sm font-medium">
            {node ? `${node.displayName} · ${node.nodeId}` : `Node ${nodeId}`}
          </p>
          {profile ? (
            <p className="mt-1 text-xs text-muted-foreground">
              {profile.scope} scope · generation {profile.generation} · manager contract{' '}
              {profile.managerContractVersion} · observed {formatTime(profile.observedAt)}
            </p>
          ) : null}
        </div>
        {node && profile ? (
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge
              status={node.isRevoked ? 'revoked' : node.isOnline ? 'online' : 'offline'}
            />
            <StatusBadge status={profile.managerStatus} />
            <StatusBadge status={profile.desiredStateStatus} />
          </div>
        ) : null}
      </div>

      {(mutationError ?? error) ? (
        <div className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100">
          {mutationError ?? error}
        </div>
      ) : null}
      {isMutating ? (
        <p role="status" className="text-sm text-muted-foreground">
          {mutation === 'recovery' ? 'Queuing manager recovery…' : 'Queuing capacity change…'}
        </p>
      ) : null}
      {isLoading ? <p className="text-muted-foreground">Loading profile status…</p> : null}

      {!isLoading && fleet && !node ? (
        <Card>
          <CardHeader>
            <CardTitle>Node not found</CardTitle>
            <CardDescription>
              Node {nodeId} is not present in this tenant&apos;s current fleet.
            </CardDescription>
          </CardHeader>
        </Card>
      ) : null}

      {!isLoading && node && !profile ? (
        <Card>
          <CardHeader>
            <CardTitle>Profile not found</CardTitle>
            <CardDescription>
              Profile {profileId} has not been reported by {node.displayName}.
            </CardDescription>
          </CardHeader>
        </Card>
      ) : null}

      {node && profile ? (
        <Card className="overflow-hidden">
          <CardContent className="grid gap-0 p-0">
            {!node.isOnline || node.isRevoked ? (
              <div
                className="border-b border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
                data-testid="profile-node-unavailable"
              >
                Capacity changes are unavailable while this node is{' '}
                {node.isRevoked ? 'revoked' : 'offline'}.
              </div>
            ) : null}
            {profile.managerStatus === 'stale' || profile.managerStatus === 'stopped' ? (
              <div
                className="border-b border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
                data-testid="profile-manager-unavailable"
              >
                The profile manager is {profile.managerStatus}; observations and slot state may not
                be current.
              </div>
            ) : null}
            <ProfileCapacitySummary
              profile={profile}
              control={capacityControl}
              canAdminister={canAdminister}
              disabled={isMutating || recoveryActive || !node.isOnline || node.isRevoked}
              onSetMaximum={queueCapacityMaximum}
            />
            <ProfileCapacityEvidence profile={profile} />
            <ProfileSubsystemHealth profile={profile} />
            <ProfileManagerRecovery
              tenantId={tenantId}
              node={node}
              profile={profile}
              control={recoveryControl}
              capacityCommand={capacityControl?.latestCommand ?? null}
              canAdminister={canAdminister}
              generatedAt={fleet?.generatedAt ?? ''}
              isMutating={isMutating}
              onRecover={queueManagerRecovery}
            />
            <ProfileResourcePolicy profile={profile} />
            <ProfileTargetsTable profile={profile} />
            <ProfileResourceTelemetry profile={profile} />
            <ProfileSlotsTable profile={profile} />
            <ProfileOperationJournal profile={profile} />
          </CardContent>
        </Card>
      ) : null}
    </section>
  );
}
