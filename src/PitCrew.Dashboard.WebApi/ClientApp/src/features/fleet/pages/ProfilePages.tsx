import { useState, type ReactNode } from 'react';
import { Link, Outlet, useOutletContext, useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { useSession } from '@/core/auth';
import {
  buildDiagnosticsContext,
  describeSubsystemHealth,
  describeWorkerUpdate,
  serializeDiagnosticsContext,
  summarizeManagerOperations,
  describeHostAdmission,
  buildSupportDiagnosticRequestPath,
  selectIncidentDiagnosticMode,
  useFleet,
  type CapacityControlState,
  type FleetNode,
  type ManagerObservedState,
  type RecoveryControlState,
  type SubsystemHealthSummary,
} from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { CopyableId } from '@/core/ui/CopyableId';
import { DetailPanel } from '@/core/ui/DetailPanel';
import { EntityHeader } from '@/core/ui/EntityHeader';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { ReadinessSummary } from '@/core/ui/ReadinessSummary';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { TaskWorkspace } from '@/core/ui/TaskWorkspace';

import { ActiveIncidentSummary } from '../components/ActiveIncidentSummary';
import { FleetHistoryPanel } from '../components/FleetHistoryPanel';
import { ProfileCapacityEvidence } from '../components/ProfileCapacityEvidence';
import { ProfileCapacitySummary } from '../components/ProfileCapacitySummary';
import { ProfileHostAdmission } from '../components/ProfileHostAdmission';
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
import { summarizeProfileAttention, summarizeProfileWorkload } from '../profileWorkspace';

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

function useProfileDetail(): ProfileDetailContext {
  return useOutletContext<ProfileDetailContext>();
}

function formatProfileDisplayName(profileId: string): string {
  const words = profileId
    .split(/[-_]+/u)
    .filter((segment) => segment.length > 0)
    .map((segment) => segment.charAt(0).toUpperCase() + segment.slice(1));
  return words.length > 0 ? words.join(' ') : profileId;
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
      {error ? <StateBanner tone="critical">{error}</StateBanner> : null}
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

  if (isLoading && !fleet) return <LoadingState label="Loading profile status" />;

  if (!fleet) {
    return <StateBanner tone="critical">{error ?? 'Profile status is unavailable.'}</StateBanner>;
  }

  if (!node) {
    return (
      <DetailPanel
        title="Node not found"
        description={`Node ${nodeId} is not present in this tenant's current fleet.`}
      >
        <StateBanner tone="critical">
          Return to the fleet and select a node from the current projection.
        </StateBanner>
      </DetailPanel>
    );
  }

  if (!profile) {
    return (
      <DetailPanel
        title="Profile not found"
        description={`Profile ${profileId} has not been reported by ${node.displayName}.`}
      >
        <StateBanner tone="critical">
          Return to the node overview and select a reported profile.
        </StateBanner>
      </DetailPanel>
    );
  }

  const basePath = `/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(node.nodeId)}/profiles/${encodeURIComponent(profile.profileId)}`;
  const configured =
    profile.configuredSlots ?? profile.autoscaling?.maximumSlots ?? profile.desiredSlots;
  const profileIncidents = fleet.activeIncidents.filter(
    (incident) =>
      incident.nodeId === node.nodeId &&
      (incident.profileId == null || incident.profileId === profile.profileId),
  );
  const workload = summarizeProfileWorkload(profile);
  const attention = summarizeProfileAttention(profile, profileIncidents);
  const navigation = [
    {
      label: 'Overview',
      description: 'Readiness and current attention',
      path: basePath,
      badge: profileIncidents.length > 0 ? String(profileIncidents.length) : undefined,
    },
    {
      label: 'Capacity',
      description: 'Configured, desired, and eligible slots',
      path: `${basePath}/capacity`,
    },
    {
      label: 'Workers',
      description: 'Current worker and job evidence',
      path: `${basePath}/workers`,
      badge:
        workload.confirmedBusyWorkers > 0
          ? String(workload.confirmedBusyWorkers)
          : workload.unknownActivityWorkers > 0
            ? 'Unknown'
            : undefined,
    },
    {
      label: 'Diagnostics',
      description: 'Subsystem, resource, and operation evidence',
      path: `${basePath}/diagnostics`,
    },
    {
      label: 'History',
      description: 'Retained profile observations',
      path: `${basePath}/history`,
    },
    {
      label: 'Recovery',
      description: 'Fenced manager recovery workflow',
      path: `${basePath}/recovery`,
      badge: recoveryControl?.operationActive ? 'Active' : undefined,
    },
  ];

  return (
    <section className="grid gap-4">
      <EntityHeader
        title={formatProfileDisplayName(profile.profileId)}
        identifier={
          <CopyableId
            label={`${profile.profileId} profile ID`}
            prefix="Profile ID"
            value={profile.profileId}
          />
        }
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {canAdminister ? (
              <Button asChild size="sm">
                <Link
                  data-testid={`request-support-diagnostics-${node.nodeId}-${profile.profileId}`}
                  to={buildSupportDiagnosticRequestPath(
                    tenantId,
                    selectIncidentDiagnosticMode(
                      fleet.activeIncidents.find(
                        (candidate) =>
                          candidate.nodeId === node.nodeId &&
                          candidate.profileId === profile.profileId,
                      ),
                      node,
                    ),
                    profile.profileId,
                  )}
                >
                  Request support diagnostics
                </Link>
              </Button>
            ) : null}
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
              Download preflight context
            </Button>
            <StatusBadge
              status={node.isRevoked ? 'revoked' : node.isOnline ? 'online' : 'offline'}
            />
            <StatusBadge status={profile.managerStatus} />
            <StatusBadge status={profile.desiredStateStatus} />
          </div>
        }
      />
      <div className="grid gap-1 text-sm text-muted-foreground">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span>Node</span>
          <Link
            className="text-link underline-offset-4 hover:underline"
            to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(node.nodeId)}`}
          >
            {node.displayName}
          </Link>
          <CopyableId label={`${node.displayName} node ID`} value={node.nodeId} />
        </div>
        <span>
          {profile.scope} scope · generation {profile.generation} · manager contract{' '}
          {profile.managerContractVersion} · observed {formatTime(profile.observedAt)}
        </span>
        <span>
          {node.isOnline ? 'Current' : 'Last-known'} capacity, worker, diagnostics, and recovery
          evidence for this profile.
        </span>
      </div>

      {error ? <StateBanner tone="caution">Showing stale fleet data. {error}</StateBanner> : null}
      <ActiveIncidentSummary
        incidents={profileIncidents}
        tenantId={tenantId}
        testId={`profile-active-incidents-${profile.profileId}`}
      />
      {!node.isOnline ? (
        <StateBanner className="py-4" data-testid="profile-node-offline" tone="caution">
          This node is offline. Every profile, capacity, worker, resource, subsystem, and recovery
          value on these pages is last-known evidence observed {formatTime(profile.observedAt)}. The
          connector was last seen {formatTime(node.lastSeenAt)}.
        </StateBanner>
      ) : null}
      {diagnosticsPrepared ? (
        <p className="text-sm text-muted-foreground" role="status">
          Preflight context downloaded. This file is the starting evidence for the PitCrew remote
          diagnostics collector, which an operator runs against the host directly. Add the exact
          affected GitHub run or job before host collection. To collect evidence through the
          dashboard instead, request support diagnostics.
        </p>
      ) : null}
      {profile.managerStatus === 'stale' || profile.managerStatus === 'stopped' ? (
        <StateBanner data-testid="profile-manager-unavailable" tone="caution">
          The profile manager is {profile.managerStatus}; observations and slot state may not be
          current.
        </StateBanner>
      ) : null}

      <ReadinessSummary
        title="Profile readiness"
        description="Current or last-known manager evidence used to investigate this profile and authorize safe operations."
        status={
          <div className="flex flex-wrap gap-2">
            <StatusBadge status={attention.label} tone={attention.tone} />
            <StatusBadge status={profile.managerStatus} />
            <StatusBadge status={profile.desiredStateStatus} />
          </div>
        }
        items={[
          {
            label: 'Observation',
            value: formatTime(profile.observedAt),
            detail: node.isOnline ? 'Current connector projection' : 'Last-known evidence',
          },
          {
            label: 'Local capacity',
            value: `${profile.activeSlots} of ${configured}`,
            detail: `${profile.eligibleSlots ?? 'Unknown'} GitHub eligible`,
          },
          {
            label: 'Current work',
            value: workload.busyLabel,
            detail: `${workload.runningJobsLabel} running jobs · ${workload.runningJobsDetail}`,
          },
          {
            label: 'Current exception',
            value: attention.label,
            detail: attention.description,
          },
        ]}
      />
      <TaskWorkspace
        navigationLabel={`${profile.profileId} profile tasks`}
        navigationItems={navigation}
      >
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
      </TaskWorkspace>
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
  const workload = summarizeProfileWorkload(profile);
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
  const hostAdmission = describeHostAdmission(profile.hostAdmission);
  const healthSignals = [
    {
      label: 'Docker operations',
      description: describeSubsystemOverview(profile.subsystemHealth?.docker),
      status: dockerHealth.status,
      task: 'diagnostics',
      testId: `profile-overview-docker-${profile.profileId}`,
    },
    {
      label: 'GitHub operations',
      description: describeSubsystemOverview(profile.subsystemHealth?.github),
      status: githubHealth.status,
      task: 'diagnostics',
      testId: `profile-overview-github-${profile.profileId}`,
    },
    {
      label: 'Resource telemetry',
      description: telemetryDescription,
      status: telemetryStatus,
      task: 'diagnostics',
      testId: `profile-overview-resources-${profile.profileId}`,
    },
    {
      label: 'Host admission',
      description: hostAdmission.description,
      status: hostAdmission.status,
      task: 'capacity',
      testId: `profile-overview-host-admission-${profile.profileId}`,
    },
    {
      label: 'Worker image rollout',
      description: describeWorkerUpdate(profile),
      status: profile.update?.status ?? 'unavailable',
      task: 'workers',
      testId: `profile-overview-worker-update-${profile.profileId}`,
    },
    {
      label: 'Manager operations',
      description:
        operations.adverseCount > 0
          ? operations.label
          : `${operations.eventCount} retained ${operations.eventCount === 1 ? 'event' : 'events'}`,
      status: operations.status,
      task: 'diagnostics',
      testId: `profile-overview-operations-${profile.profileId}`,
    },
  ] as const;
  const healthSummary = summarizeHealthSignals(healthSignals);

  return (
    <section className="grid gap-4" aria-labelledby="profile-overview-heading">
      <h2 className="sr-only" id="profile-overview-heading">
        Profile overview
      </h2>

      <OperationalList label="Profile workflow summary">
        <OperationalRow
          title="Capacity"
          description={
            profile.autoscaling
              ? 'Configured autoscaling ceiling, current activation target, and registration eligibility.'
              : 'Configured fixed capacity and current registration eligibility.'
          }
          status={<StatusBadge status={profile.autoscaling?.status ?? 'fixed'} />}
          actions={
            <Button asChild size="sm" variant="outline">
              <Link to={`${basePath}/capacity`}>View capacity</Link>
            </Button>
          }
        >
          <dl className="grid gap-3 text-sm sm:grid-cols-2 xl:grid-cols-4">
            <OverviewFact
              label="Maximum"
              value={configured}
              testId={`profile-overview-maximum-${profile.profileId}`}
            />
            <OverviewFact
              label="Target"
              value={target}
              testId={`profile-overview-target-${profile.profileId}`}
            />
            <OverviewFact
              label="Local slots"
              value={profile.activeSlots}
              testId={`profile-overview-local-${profile.profileId}`}
            />
            <OverviewFact
              label="GitHub eligible"
              value={profile.eligibleSlots ?? 'Unknown'}
              testId={`profile-overview-eligible-${profile.profileId}`}
            />
          </dl>
        </OperationalRow>
        <OperationalRow
          title="Operational health"
          description="Latest subsystem, resource, host-admission, image, and manager-operation evidence."
          status={<StatusBadge status={healthSummary.label} tone={healthSummary.tone} />}
          actions={
            <Button asChild size="sm" variant="outline">
              <Link to={`${basePath}/${healthSummary.task}`}>Review {healthSummary.task}</Link>
            </Button>
          }
        >
          <ul className="grid grid-cols-[repeat(auto-fit,minmax(min(16rem,100%),1fr))] gap-2">
            {healthSignals.map((signal) => (
              <li
                className="flex min-w-0 items-start justify-between gap-3 rounded-md bg-muted/35 px-3 py-2"
                data-testid={signal.testId}
                key={signal.label}
              >
                <span className="min-w-0">
                  <span className="block text-xs font-medium">{signal.label}</span>
                  <span className="mt-0.5 block text-xs text-muted-foreground">
                    {signal.description}
                  </span>
                  <Link
                    aria-label={`View ${signal.task} for ${signal.label}`}
                    className="mt-1 inline-block text-xs font-medium text-link underline-offset-4 hover:underline"
                    to={`${basePath}/${signal.task}`}
                  >
                    View {signal.task}
                  </Link>
                </span>
                <span className="shrink-0 whitespace-nowrap">
                  <StatusBadge status={signal.status} />
                </span>
              </li>
            ))}
          </ul>
        </OperationalRow>
        <OperationalRow
          title="Workers and current jobs"
          description={`${profile.slots.length} workers · ${workload.busyLabel} · ${workload.runningJobsLabel} running jobs · ${profile.drainingSlots} draining`}
          status={
            <StatusBadge
              status={
                workload.unknownActivityWorkers > 0
                  ? 'activity unavailable'
                  : workload.confirmedBusyWorkers > 0
                    ? 'running'
                    : 'idle'
              }
              tone={workload.unknownActivityWorkers > 0 ? 'caution' : undefined}
            />
          }
          actions={
            <Button asChild size="sm" variant="outline">
              <Link to={`${basePath}/workers`}>View workers</Link>
            </Button>
          }
        >
          {workload.unknownActivityWorkers > 0 || workload.jobReportingProfiles === 0 ? (
            <p className="mb-2 text-xs text-muted-foreground">
              {workload.busyDetail}. {workload.runningJobsDetail}.
            </p>
          ) : null}
          {profile.slots.some((slot) => slot.currentJob != null) ? (
            <ul
              className="grid gap-2 text-sm"
              data-testid={`profile-current-jobs-${profile.profileId}`}
            >
              {profile.slots.flatMap((slot) =>
                slot.currentJob
                  ? [
                      <li className="rounded-md bg-muted/35 px-3 py-2" key={slot.currentJob.jobId}>
                        <a
                          className="font-semibold text-link underline-offset-4 hover:underline"
                          href={`${slot.currentJob.repository}/actions/runs/${slot.currentJob.workflowRunId}/job/${slot.currentJob.jobId}`}
                          rel="noreferrer"
                          target="_blank"
                        >
                          {slot.currentJob.displayName ?? `GitHub job ${slot.currentJob.jobId}`}
                        </a>
                        <div className="text-xs text-muted-foreground">
                          Started {formatTime(slot.currentJob.startedAt)} · inspect or cancel in
                          GitHub
                        </div>
                      </li>,
                    ]
                  : [],
              )}
            </ul>
          ) : null}
        </OperationalRow>
        <OperationalRow
          title="Manager recovery"
          description={recoveryDescription}
          status={<StatusBadge status={recoveryStatus} />}
          actions={
            <Button asChild size="sm" variant="outline">
              <Link to={`${basePath}/recovery`}>View recovery</Link>
            </Button>
          }
        />
      </OperationalList>
    </section>
  );
}

function summarizeHealthSignals(
  signals: ReadonlyArray<{
    readonly status: string;
    readonly task: 'capacity' | 'workers' | 'diagnostics';
  }>,
): {
  readonly label: string;
  readonly tone: 'positive' | 'caution' | 'critical';
  readonly task: 'capacity' | 'workers' | 'diagnostics';
} {
  const critical = signals.find((signal) =>
    ['degraded', 'failed', 'stopped', 'blocked'].includes(signal.status),
  );
  if (critical) {
    return { label: 'Critical evidence', tone: 'critical', task: critical.task };
  }
  const caution = signals.find((signal) =>
    ['partial', 'rolling', 'unknown', 'unavailable', 'stale', 'starting', 'truncated'].includes(
      signal.status,
    ),
  );
  if (caution) {
    return { label: 'Evidence needs attention', tone: 'caution', task: caution.task };
  }
  return { label: 'No reported exception', tone: 'positive', task: 'diagnostics' };
}

function OverviewFact({
  label,
  value,
  testId,
}: {
  readonly label: string;
  readonly value: ReactNode;
  readonly testId: string;
}) {
  return (
    <div className="min-w-0" data-testid={testId}>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 font-semibold tabular-nums text-foreground">{value}</dd>
    </div>
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
        <StateBanner data-testid="profile-node-unavailable" tone="caution">
          Capacity changes are unavailable while this node is{' '}
          {node.isRevoked ? 'revoked' : 'offline'}.
        </StateBanner>
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
      <ProfileHostAdmission profile={profile} />
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
