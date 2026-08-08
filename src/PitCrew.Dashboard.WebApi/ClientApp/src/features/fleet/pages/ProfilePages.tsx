import { useState, type ReactNode } from 'react';
import { Link, Outlet, useOutletContext, useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { useSession } from '@/core/auth';
import {
  buildDiagnosticsContext,
  describeSubsystemHealth,
  describeWorkerUpdate,
  serializeDiagnosticsContext,
  summarizeManagerOperations,
  useFleet,
  type CapacityControlState,
  type FleetNode,
  type ManagerObservedState,
  type RecoveryControlState,
  type SubsystemHealthSummary,
} from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { SectionNavigation } from '@/core/ui/SectionNavigation';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ActiveIncidentSummary } from '../components/ActiveIncidentSummary';
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
import { ProfileWorkerUpdateSummary } from '../components/ProfileWorkerUpdateSummary';
import { recoverManager, setCapacityMaximum } from '../fleetApi';
import { downloadDiagnosticsContext } from '../diagnosticsDownload';
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

interface OverviewMetricProps {
  readonly label: string;
  readonly value: ReactNode;
  readonly description: string;
  readonly status?: string;
  readonly testId: string;
}

function useProfileDetail(): ProfileDetailContext {
  return useOutletContext<ProfileDetailContext>();
}

function OverviewMetric({ label, value, description, status, testId }: OverviewMetricProps) {
  return (
    <Card className="gap-3 py-5 shadow-control-lift" data-testid={testId}>
      <CardHeader className="px-5">
        <CardDescription>{label}</CardDescription>
        <CardTitle as="p" className="text-2xl font-semibold tabular-nums">
          {value}
        </CardTitle>
        {status ? (
          <CardAction>
            <StatusBadge status={status} />
          </CardAction>
        ) : null}
      </CardHeader>
      <CardContent className="px-5 text-xs text-muted-foreground">{description}</CardContent>
    </Card>
  );
}

interface HealthSummaryRowProps {
  readonly label: string;
  readonly description: string;
  readonly status: string;
  readonly testId: string;
}

function HealthSummaryRow({ label, description, status, testId }: HealthSummaryRowProps) {
  return (
    <div
      className="flex items-start justify-between gap-4 py-3 first:pt-0 last:pb-0"
      data-testid={testId}
    >
      <div className="min-w-0">
        <div className="text-sm font-medium">{label}</div>
        <div className="mt-0.5 text-xs text-muted-foreground">{description}</div>
      </div>
      <StatusBadge status={status} />
    </div>
  );
}

function describeSubsystemOverview(summary: SubsystemHealthSummary | null | undefined): string {
  if (!summary) return 'No operation evidence reported.';
  if (summary.state === 'unknown') return 'No completed operation observed.';
  if (summary.consecutiveFailures === 0) return 'No consecutive failures reported.';
  return `${summary.consecutiveFailures} consecutive failures reported.`;
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
  const [diagnosticsPrepared, setDiagnosticsPrepared] = useState(false);
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
          <Button
            data-testid={`prepare-diagnostics-${node.nodeId}-${profile.profileId}`}
            type="button"
            size="sm"
            variant="outline"
            onClick={() => {
              const context = buildDiagnosticsContext(
                node,
                fleet.generatedAt,
                fleet.activeIncidents,
              );
              downloadDiagnosticsContext(node.nodeId, serializeDiagnosticsContext(context));
              setDiagnosticsPrepared(true);
            }}
          >
            Prepare diagnostics
          </Button>
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
      <ActiveIncidentSummary
        incidents={fleet.activeIncidents.filter(
          (incident) =>
            incident.nodeId === node.nodeId &&
            (incident.profileId == null || incident.profileId === profile.profileId),
        )}
        tenantId={tenantId}
        testId={`profile-active-incidents-${profile.profileId}`}
      />
      {!node.isOnline ? (
        <div
          className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
          data-testid="profile-node-offline"
          role="status"
        >
          This node is offline. Every profile, capacity, worker, resource, subsystem, and recovery
          value on these pages is last-known evidence observed {formatTime(profile.observedAt)}. The
          connector was last seen {formatTime(node.lastSeenAt)}.
        </div>
      ) : null}
      {diagnosticsPrepared ? (
        <p className="text-sm text-muted-foreground" role="status">
          Diagnostics context downloaded. Add the exact affected GitHub run or job before host
          collection.
        </p>
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

      <SectionNavigation label={`${profile.profileId} profile navigation`} items={navigation} />
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
  const recoveryDescription = recoveryControl?.latestCommand
    ? `Latest command requested ${formatTime(recoveryControl.latestCommand.requestedAt)}.`
    : recoveryControl
      ? 'No manager recovery has been requested.'
      : 'This connector exposes read-only profile evidence.';
  const telemetryStatus = profile.resourceTelemetry?.status ?? 'unavailable';
  const telemetryDescription = profile.resourceTelemetry
    ? `Sampled ${formatTime(profile.resourceTelemetry.sampledAt)}.`
    : 'No resource sample was reported.';

  return (
    <section className="grid gap-4" aria-labelledby="profile-overview-heading">
      <h2 className="sr-only" id="profile-overview-heading">
        Profile overview
      </h2>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <OverviewMetric
          label="Maximum"
          value={configured}
          description={
            profile.autoscaling ? 'Configured autoscaling ceiling.' : 'Configured fixed capacity.'
          }
          status={profile.autoscaling?.status ?? 'fixed'}
          testId={`profile-overview-maximum-${profile.profileId}`}
        />
        <OverviewMetric
          label="Target"
          value={target}
          description={
            node.isOnline
              ? 'Current manager activation target.'
              : 'Last-known manager activation target.'
          }
          testId={`profile-overview-target-${profile.profileId}`}
        />
        <OverviewMetric
          label="Local slots"
          value={profile.activeSlots}
          description="Worker containers reported by the manager."
          testId={`profile-overview-local-${profile.profileId}`}
        />
        <OverviewMetric
          label="GitHub eligible"
          value={profile.eligibleSlots ?? 'Unknown'}
          description={
            node.isOnline
              ? 'Current registration eligibility evidence.'
              : 'Last-known registration eligibility evidence.'
          }
          testId={`profile-overview-eligible-${profile.profileId}`}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="gap-0">
          <CardHeader>
            <CardTitle>Operational health</CardTitle>
            <CardDescription>
              Latest manager-reported subsystem, resource, and operation evidence.
            </CardDescription>
          </CardHeader>
          <CardContent className="divide-y">
            <HealthSummaryRow
              label="Docker operations"
              description={describeSubsystemOverview(profile.subsystemHealth?.docker)}
              status={dockerHealth.status}
              testId={`profile-overview-docker-${profile.profileId}`}
            />
            <HealthSummaryRow
              label="GitHub operations"
              description={describeSubsystemOverview(profile.subsystemHealth?.github)}
              status={githubHealth.status}
              testId={`profile-overview-github-${profile.profileId}`}
            />
            <HealthSummaryRow
              label="Resource telemetry"
              description={telemetryDescription}
              status={telemetryStatus}
              testId={`profile-overview-resources-${profile.profileId}`}
            />
            <HealthSummaryRow
              label="Worker image rollout"
              description={describeWorkerUpdate(profile)}
              status={profile.update?.status ?? 'unavailable'}
              testId={`profile-overview-worker-update-${profile.profileId}`}
            />
            <HealthSummaryRow
              label="Manager operations"
              description={
                operations.adverseCount > 0
                  ? operations.label
                  : `${operations.eventCount} retained ${operations.eventCount === 1 ? 'event' : 'events'}`
              }
              status={operations.status}
              testId={`profile-overview-operations-${profile.profileId}`}
            />
          </CardContent>
          <CardFooter className="border-t">
            <Button asChild size="sm" variant="outline">
              <Link to={`${basePath}/diagnostics`}>View diagnostics</Link>
            </Button>
          </CardFooter>
        </Card>

        <Card className="gap-0">
          <CardHeader>
            <CardTitle>Workers and recovery</CardTitle>
            <CardDescription>
              {node.isOnline ? 'Current' : 'Last-known'} worker activity and the latest fenced
              manager-recovery state.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4">
            <dl className="grid grid-cols-2 gap-4 rounded-md border bg-muted/20 px-3 py-3 text-sm sm:grid-cols-5">
              <div>
                <dt className="text-xs text-muted-foreground uppercase">Workers</dt>
                <dd className="mt-1 font-semibold tabular-nums">{profile.slots.length}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">Busy</dt>
                <dd className="mt-1 font-semibold tabular-nums">{busyWorkers}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">Running jobs</dt>
                <dd className="mt-1 font-semibold tabular-nums">
                  {profile.autoscaling?.runningJobs ?? 'Unknown'}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">Queued</dt>
                <dd className="mt-1 font-semibold tabular-nums">
                  {profile.autoscaling?.availableJobs ?? 'Unknown'}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">Draining</dt>
                <dd className="mt-1 font-semibold tabular-nums">{profile.drainingSlots}</dd>
              </div>
            </dl>
            {profile.slots.some((slot) => slot.currentJob != null) ? (
              <ul
                className="grid gap-2 text-sm"
                data-testid={`profile-current-jobs-${profile.profileId}`}
              >
                {profile.slots.flatMap((slot) =>
                  slot.currentJob
                    ? [
                        <li className="rounded border px-3 py-2" key={slot.currentJob.jobId}>
                          <a
                            className="font-semibold text-link underline-offset-4 hover:underline"
                            href={`${slot.currentJob.repository}/actions/runs/${slot.currentJob.workflowRunId}/job/${slot.currentJob.jobId}`}
                            rel="noreferrer"
                            target="_blank"
                          >
                            {slot.currentJob.displayName ?? `GitHub job ${slot.currentJob.jobId}`}
                          </a>
                          <div className="text-xs text-muted-foreground">
                            Started {formatTime(slot.currentJob.startedAt)} · open in GitHub to
                            inspect or cancel
                          </div>
                        </li>,
                      ]
                    : [],
                )}
              </ul>
            ) : null}
            <div className="flex items-start justify-between gap-4">
              <div>
                <div className="text-sm font-medium">Manager recovery</div>
                <div className="mt-0.5 text-xs text-muted-foreground">{recoveryDescription}</div>
              </div>
              <StatusBadge status={recoveryStatus} />
            </div>
          </CardContent>
          <CardFooter className="flex flex-wrap gap-2 border-t">
            <Button asChild size="sm" variant="outline">
              <Link to={`${basePath}/workers`}>View workers</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to={`${basePath}/recovery`}>View recovery</Link>
            </Button>
          </CardFooter>
        </Card>
      </div>
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

  const queueCapacityMaximum = async (maximum: number, resumeCommandId?: string) => {
    setIsMutating(true);
    setMutationError(null);
    try {
      await setCapacityMaximum(
        tenantId,
        node.nodeId,
        profile.profileId,
        maximum,
        antiforgeryToken,
        resumeCommandId ?? null,
      );
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
      <ProfileWorkerUpdateSummary profile={profile} />
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
      <ProfileWorkerUpdateSummary profile={profile} />
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
