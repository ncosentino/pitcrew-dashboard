import { useState, type ReactNode } from 'react';
import { Link, Outlet, useOutletContext, useParams } from 'react-router-dom';

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';
import {
  describeSubsystemHealth,
  summarizeManagerOperations,
  useFleet,
  type CapacityControlState,
  type FleetNode,
  type ManagerObservedState,
  type RecoveryControlState,
} from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { EntitySectionNavigation } from '../components/EntitySectionNavigation';
import { FleetHistoryPanel } from '../components/FleetHistoryPanel';
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

interface ProfileDetailContext {
  readonly tenantId: string;
  readonly node: FleetNode;
  readonly profile: ManagerObservedState;
  readonly capacityControl: CapacityControlState | null;
  readonly recoveryControl: RecoveryControlState | null;
  readonly canAdminister: boolean;
  readonly antiforgeryToken: string;
  readonly generatedAt: string;
  readonly refreshNow: () => Promise<void>;
}

interface OverviewDestinationProps {
  readonly title: string;
  readonly description: string;
  readonly path: string;
  readonly children: ReactNode;
  readonly testId: string;
}

function useProfileDetail(): ProfileDetailContext {
  return useOutletContext<ProfileDetailContext>();
}

function OverviewDestination({
  title,
  description,
  path,
  children,
  testId,
}: OverviewDestinationProps) {
  return (
    <Card className="gap-4 py-5" data-testid={testId}>
      <CardHeader className="px-5">
        <CardTitle>
          <Link className="text-primary underline-offset-4 hover:underline" to={path}>
            {title}
          </Link>
        </CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      <CardContent className="px-5">{children}</CardContent>
    </Card>
  );
}

function MutationMessage({
  error,
  busyMessage,
}: {
  readonly error: string | null;
  readonly busyMessage: string | null;
}) {
  return (
    <>
      {error ? (
        <div
          className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
          role="alert"
        >
          {error}
        </div>
      ) : null}
      {busyMessage ? (
        <p role="status" className="text-sm text-muted-foreground">
          {busyMessage}
        </p>
      ) : null}
    </>
  );
}

/** Provides shared profile identity, status, and route-level secondary navigation. */
export function ProfileDetailLayout() {
  const { tenantId = '', nodeId = '', profileId = '' } = useParams();
  const { session } = useSession();
  const { fleet, error, isLoading, refreshNow } = useFleet();
  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);
  const canAdminister = tenant?.role === 'administrator' || tenant?.role === 'owner';
  const node = fleet?.nodes.find((candidate) => candidate.nodeId === nodeId);
  const profile = node?.profiles.find((candidate) => candidate.profileId === profileId);
  const capacityControl =
    node?.capacityControls.find((candidate) => candidate.profileId === profileId) ?? null;
  const recoveryControl =
    node?.recoveryControls.find((candidate) => candidate.profileId === profileId) ?? null;

  if (isLoading && !fleet) return <p className="text-muted-foreground">Loading profile status…</p>;

  if (!fleet) {
    return (
      <div
        className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
        role="alert"
      >
        {error ?? 'Profile status is unavailable.'}
      </div>
    );
  }

  if (!node) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Node not found</CardTitle>
          <CardDescription>
            Node {nodeId} is not present in this tenant&apos;s current fleet.
          </CardDescription>
        </CardHeader>
      </Card>
    );
  }

  if (!profile) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Profile not found</CardTitle>
          <CardDescription>
            Profile {profileId} has not been reported by {node.displayName}.
          </CardDescription>
        </CardHeader>
      </Card>
    );
  }

  const basePath = `/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(node.nodeId)}/profiles/${encodeURIComponent(profile.profileId)}`;
  const navigation = [
    { label: 'Overview', path: basePath },
    { label: 'Capacity', path: `${basePath}/capacity` },
    { label: 'Workers', path: `${basePath}/workers` },
    { label: 'Diagnostics', path: `${basePath}/diagnostics` },
    { label: 'History', path: `${basePath}/history` },
    { label: 'Recovery', path: `${basePath}/recovery` },
  ];

  return (
    <section className="grid gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-sm font-medium">
            {node.displayName} · <span className="font-mono text-xs">{node.nodeId}</span>
          </p>
          <p className="mt-1 text-xs text-muted-foreground">
            {profile.scope} scope · generation {profile.generation} · manager contract{' '}
            {profile.managerContractVersion} · observed {formatTime(profile.observedAt)}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusBadge status={node.isRevoked ? 'revoked' : node.isOnline ? 'online' : 'offline'} />
          <StatusBadge status={profile.managerStatus} />
          <StatusBadge status={profile.desiredStateStatus} />
        </div>
      </div>

      {error ? (
        <div
          className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
          role="status"
        >
          Showing stale fleet data. {error}
        </div>
      ) : null}
      {profile.managerStatus === 'stale' || profile.managerStatus === 'stopped' ? (
        <div
          className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
          data-testid="profile-manager-unavailable"
        >
          The profile manager is {profile.managerStatus}; observations and slot state may not be
          current.
        </div>
      ) : null}

      <EntitySectionNavigation
        label={`${profile.profileId} profile navigation`}
        items={navigation}
      />
      <Outlet
        context={
          {
            tenantId,
            node,
            profile,
            capacityControl,
            recoveryControl,
            canAdminister,
            antiforgeryToken: session?.antiforgeryToken ?? '',
            generatedAt: fleet.generatedAt,
            refreshNow,
          } satisfies ProfileDetailContext
        }
      />
    </section>
  );
}

/** Renders scan-level profile triage with links to focused operational destinations. */
export function ProfileOverviewPage() {
  const { tenantId, node, profile, recoveryControl } = useProfileDetail();
  const basePath = `/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(node.nodeId)}/profiles/${encodeURIComponent(profile.profileId)}`;
  const configured =
    profile.configuredSlots ?? profile.autoscaling?.maximumSlots ?? profile.desiredSlots;
  const target = profile.autoscaling?.targetSlots ?? profile.desiredSlots;
  const busyWorkers = profile.slots.filter((slot) => slot.activity === 'busy').length;
  const dockerHealth = describeSubsystemHealth(profile.subsystemHealth?.docker, 'Docker');
  const githubHealth = describeSubsystemHealth(profile.subsystemHealth?.github, 'GitHub');
  const operations = summarizeManagerOperations(profile.operationJournal);
  const recoveryStatus =
    recoveryControl?.latestCommand?.status ?? (recoveryControl ? 'not requested' : 'read only');

  return (
    <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      <OverviewDestination
        title="Capacity"
        description="Activation targets, local supply, GitHub evidence, and maximum control."
        path={`${basePath}/capacity`}
        testId="profile-overview-capacity"
      >
        <dl className="grid grid-cols-2 gap-3 text-sm">
          <div>
            <dt className="text-xs text-muted-foreground uppercase">Maximum</dt>
            <dd className="mt-1 font-semibold tabular-nums">{configured}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground uppercase">Target</dt>
            <dd className="mt-1 font-semibold tabular-nums">{target}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground uppercase">Local</dt>
            <dd className="mt-1 font-semibold tabular-nums">{profile.activeSlots}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground uppercase">GitHub eligible</dt>
            <dd className="mt-1 font-semibold tabular-nums">
              {profile.eligibleSlots ?? 'Unknown'}
            </dd>
          </div>
        </dl>
      </OverviewDestination>

      <OverviewDestination
        title="Workers"
        description="Current worker identities, lifecycle state, exits, resources, and I/O."
        path={`${basePath}/workers`}
        testId="profile-overview-workers"
      >
        <div className="flex flex-wrap items-center gap-3 text-sm">
          <span>
            <strong className="tabular-nums">{profile.slots.length}</strong>{' '}
            {profile.slots.length === 1 ? 'worker' : 'workers'}
          </span>
          <span>
            <strong className="tabular-nums">{busyWorkers}</strong> busy
          </span>
          <span>
            <strong className="tabular-nums">{profile.drainingSlots}</strong> draining
          </span>
        </div>
      </OverviewDestination>

      <OverviewDestination
        title="Diagnostics"
        description="Current subsystem outcomes, resource utilization, and manager operations."
        path={`${basePath}/diagnostics`}
        testId="profile-overview-diagnostics"
      >
        <div className="flex flex-wrap items-center gap-2 text-xs">
          <span className="flex items-center gap-1">
            Docker <StatusBadge status={dockerHealth.status} />
          </span>
          <span className="flex items-center gap-1">
            GitHub <StatusBadge status={githubHealth.status} />
          </span>
          <span className="flex items-center gap-1">
            Resources <StatusBadge status={profile.resourceTelemetry?.status ?? 'unavailable'} />
          </span>
          {operations.adverseCount > 0 ? (
            <span className="text-amber-700 dark:text-amber-300">{operations.label}</span>
          ) : null}
        </div>
      </OverviewDestination>

      <OverviewDestination
        title="History"
        description="Bounded telemetry trends, deficit changes, and durable manager operations."
        path={`${basePath}/history`}
        testId="profile-overview-history"
      >
        <p className="text-sm text-muted-foreground">
          Choose a retained range without loading historical charts into every profile view.
        </p>
      </OverviewDestination>

      <OverviewDestination
        title="Recovery"
        description="Fenced manager-only recovery, current command progress, and immutable outcomes."
        path={`${basePath}/recovery`}
        testId="profile-overview-recovery"
      >
        <div className="flex items-center gap-2 text-sm">
          <StatusBadge status={recoveryStatus} />
          <span>
            {recoveryControl?.latestCommand
              ? 'Latest recovery command'
              : recoveryControl
                ? 'No recovery requested'
                : 'Connector is read-only'}
          </span>
        </div>
      </OverviewDestination>
    </section>
  );
}

/** Renders capacity state, deficit evidence, target evidence, and authorized maximum control. */
export function ProfileCapacityPage() {
  const {
    tenantId,
    node,
    profile,
    capacityControl,
    recoveryControl,
    canAdminister,
    antiforgeryToken,
    refreshNow,
  } = useProfileDetail();
  const [isMutating, setIsMutating] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const recoveryActive = isRecoveryCommandActive(recoveryControl?.latestCommand ?? null);

  const queueCapacityMaximum = async (maximum: number) => {
    setIsMutating(true);
    setMutationError(null);
    try {
      await setCapacityMaximum(tenantId, node.nodeId, profile.profileId, maximum, antiforgeryToken);
      await refreshNow();
    } catch (caught) {
      setMutationError(
        caught instanceof Error ? caught.message : 'Capacity maximum could not be queued.',
      );
    } finally {
      setIsMutating(false);
    }
  };

  return (
    <section className="grid gap-4">
      {!node.isOnline || node.isRevoked ? (
        <div
          className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
          data-testid="profile-node-unavailable"
        >
          Capacity changes are unavailable while this node is{' '}
          {node.isRevoked ? 'revoked' : 'offline'}.
        </div>
      ) : null}
      <MutationMessage
        error={mutationError}
        busyMessage={isMutating ? 'Queuing capacity change…' : null}
      />
      <ProfileCapacitySummary
        profile={profile}
        control={capacityControl}
        canAdminister={canAdminister}
        disabled={isMutating || recoveryActive || !node.isOnline || node.isRevoked}
        onSetMaximum={queueCapacityMaximum}
      />
      <ProfileCapacityEvidence profile={profile} />
      <ProfileTargetsTable profile={profile} />
    </section>
  );
}

/** Renders worker policy and the current worker-level diagnostic table. */
export function ProfileWorkersPage() {
  const { profile } = useProfileDetail();
  return (
    <section className="grid gap-4">
      <ProfileResourcePolicy profile={profile} />
      <ProfileSlotsTable profile={profile} />
    </section>
  );
}

/** Renders current subsystem, resource, and manager-operation evidence. */
export function ProfileDiagnosticsPage() {
  const { profile } = useProfileDetail();
  return (
    <section className="grid gap-4">
      <ProfileSubsystemHealth profile={profile} />
      <ProfileResourceTelemetry profile={profile} />
      <ProfileOperationJournal profile={profile} />
    </section>
  );
}

/** Renders bounded retained history for one profile without an outer disclosure. */
export function ProfileHistoryPage() {
  const { tenantId, node, profile } = useProfileDetail();
  return (
    <FleetHistoryPanel
      tenantId={tenantId}
      nodeId={node.nodeId}
      profileId={profile.profileId}
      presentation="page"
      testId="profile-history"
    />
  );
}

/** Renders fenced manager recovery, live progress, and immutable recovery history. */
export function ProfileRecoveryPage() {
  const {
    tenantId,
    node,
    profile,
    capacityControl,
    recoveryControl,
    canAdminister,
    antiforgeryToken,
    generatedAt,
    refreshNow,
  } = useProfileDetail();
  const [isMutating, setIsMutating] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);

  const queueManagerRecovery = async (fences: RecoveryFences) => {
    setIsMutating(true);
    setMutationError(null);
    try {
      await recoverManager(tenantId, node.nodeId, profile.profileId, fences, antiforgeryToken);
      await refreshNow();
    } catch (caught) {
      setMutationError(
        caught instanceof Error ? caught.message : 'Manager recovery could not be queued.',
      );
    } finally {
      setIsMutating(false);
    }
  };

  return (
    <section className="grid gap-4">
      <MutationMessage
        error={mutationError}
        busyMessage={isMutating ? 'Queuing manager recovery…' : null}
      />
      <ProfileManagerRecovery
        tenantId={tenantId}
        node={node}
        profile={profile}
        control={recoveryControl}
        capacityCommand={capacityControl?.latestCommand ?? null}
        canAdminister={canAdminister}
        generatedAt={generatedAt}
        isMutating={isMutating}
        onRecover={queueManagerRecovery}
      />
    </section>
  );
}
