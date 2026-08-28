import { useEffect, useState, type ReactNode } from 'react';
import { ChevronDown, Rows3, Table2 } from 'lucide-react';
import { Link, Outlet, useOutletContext, useParams } from 'react-router-dom';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { DisplayNameEditor } from '@/components/DisplayNameEditor';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';
import {
  buildDiagnosticsContext,
  serializeDiagnosticsContext,
  useFleet,
  type FleetNode,
  type ManagerObservedState,
} from '@/core/fleet';
import { formatBytes, formatCpuCores, formatTime } from '@/core/formatting/formatters';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { CopyableId } from '@/core/ui/CopyableId';
import { DetailPanel } from '@/core/ui/DetailPanel';
import { EmptyState } from '@/core/ui/EmptyState';
import { EntityHeader } from '@/core/ui/EntityHeader';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { ReadinessSummary } from '@/core/ui/ReadinessSummary';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { TaskWorkspace } from '@/core/ui/TaskWorkspace';

import { ActiveIncidentSummary } from '../components/ActiveIncidentSummary';
import { ConnectorHealthSummary } from '../components/ConnectorHealthSummary';
import { FleetHistoryPanel } from '../components/FleetHistoryPanel';
import { HostHardwareCard } from '../components/HostHardwareSummary';
import { NodePressureCommandCenter } from '../components/NodePressureCommandCenter';
import { ProfileComparisonTable } from '../components/ProfileComparisonTable';
import { renameNode, requestCredentialRotation, revokeNode, setCapacityMaximum } from '../fleetApi';
import { downloadDiagnosticsContext } from '../diagnosticsDownload';
import { aggregateNode, aggregateProfileResources, getNodeStatus } from '../nodeSummary';
import {
  summarizeNodeWorkload,
  summarizeProfileAttention,
  summarizeProfileWorkload,
  type ProfileAttentionSummary,
} from '../profileWorkspace';

interface NodeDetailContext {
  readonly tenantId: string;
  readonly node: FleetNode;
  readonly canAdminister: boolean;
  readonly antiforgeryToken: string;
  readonly refreshNow: () => Promise<void>;
}

interface ProfileSummaryProps {
  readonly profile: ManagerObservedState;
  readonly attention: ProfileAttentionSummary;
  readonly tenantId: string;
  readonly nodeId: string;
  readonly nodeIsOnline: boolean;
}

interface ResponsiveOverviewSectionProps {
  readonly title: string;
  readonly summary: string;
  readonly status?: string;
  readonly isDesktop: boolean;
  readonly testId: string;
  readonly children: ReactNode;
}

const desktopOverviewQuery = '(min-width: 48rem)';
export const profileComparisonViewStorageKey = 'pitcrew.node-profiles.view';
type ProfilePresentation = 'cards' | 'table';

function useNodeDetail(): NodeDetailContext {
  return useOutletContext<NodeDetailContext>();
}

function useDesktopOverview(): boolean {
  const [isDesktop, setIsDesktop] = useState(
    () => globalThis.matchMedia(desktopOverviewQuery).matches,
  );

  useEffect(() => {
    const mediaQuery = globalThis.matchMedia(desktopOverviewQuery);
    const handleChange = (event: MediaQueryListEvent) => setIsDesktop(event.matches);
    mediaQuery.addEventListener('change', handleChange);
    return () => mediaQuery.removeEventListener('change', handleChange);
  }, []);

  return isDesktop;
}

function ResponsiveOverviewSection({
  title,
  summary,
  status,
  isDesktop,
  testId,
  children,
}: ResponsiveOverviewSectionProps) {
  const [isOpenOnMobile, setIsOpenOnMobile] = useState(false);
  const isOpen = isDesktop || isOpenOnMobile;

  return (
    <details
      className="group min-w-0 md:contents"
      data-testid={testId}
      open={isOpen}
      onToggle={(event) => {
        if (!isDesktop) setIsOpenOnMobile(event.currentTarget.open);
      }}
    >
      <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-3 rounded-lg border bg-card px-4 py-3 outline-none transition-colors hover:bg-muted/40 focus-visible:ring-2 focus-visible:ring-ring md:hidden">
        <span className="min-w-0">
          <span className="block font-semibold">{title}</span>
          <span className="block truncate text-xs text-muted-foreground">{summary}</span>
        </span>
        <span className="flex shrink-0 items-center gap-2">
          {status ? <StatusBadge status={status} /> : null}
          <ChevronDown
            aria-hidden="true"
            className="size-4 text-muted-foreground transition-transform group-open:rotate-180"
          />
        </span>
      </summary>
      <div className="mt-3 min-w-0 md:mt-0">{children}</div>
    </details>
  );
}

function formatProfileDisplayName(profileId: string): string {
  const words = profileId
    .split(/[-_]+/u)
    .filter((segment) => segment.length > 0)
    .map((segment) => segment.charAt(0).toUpperCase() + segment.slice(1));
  return words.length > 0 ? words.join(' ') : profileId;
}

function ProfileSummary({
  profile,
  attention,
  tenantId,
  nodeId,
  nodeIsOnline,
}: ProfileSummaryProps) {
  const configured =
    profile.configuredSlots ?? profile.autoscaling?.maximumSlots ?? profile.desiredSlots;
  const resources = aggregateProfileResources(profile);
  const workload = summarizeProfileWorkload(profile);
  const profileName = formatProfileDisplayName(profile.profileId);
  const profilePath = `/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(nodeId)}/profiles/${encodeURIComponent(profile.profileId)}`;
  const attentionPath =
    attention.task === 'overview' ? profilePath : `${profilePath}/${attention.task}`;

  return (
    <OperationalRow
      testId={`node-profile-${profile.profileId}`}
      title={profileName}
      description={`${attention.label}. ${profile.scope} scope · generation ${profile.generation} · ${
        nodeIsOnline ? 'observed' : 'last-known observation'
      } ${formatTime(profile.observedAt)}`}
      status={
        <span className="flex flex-wrap gap-2">
          <StatusBadge status={attention.label} tone={attention.tone} />
          <StatusBadge status={profile.managerStatus} />
          <StatusBadge status={profile.desiredStateStatus} />
          {profile.autoscaling ? <StatusBadge status={profile.autoscaling.status} /> : null}
        </span>
      }
      metadata={
        <CopyableId
          label={`${profile.profileId} profile ID`}
          prefix="Profile ID"
          value={profile.profileId}
        />
      }
      actions={
        <Button asChild size="sm" variant="outline">
          <Link to={attentionPath}>
            {attention.rank < 100 ? `Review ${attention.task}` : 'Open profile'}
          </Link>
        </Button>
      }
    >
      <div className="hidden gap-3 md:grid">
        <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-3 xl:grid-cols-6">
          <ProfileFact
            label={nodeIsOnline ? 'Configured' : 'Last known configured'}
            value={configured}
          />
          <ProfileFact
            label={nodeIsOnline ? 'Desired' : 'Last known desired'}
            value={profile.desiredSlots}
          />
          <ProfileFact
            label={nodeIsOnline ? 'Local slots' : 'Last known local slots'}
            value={profile.activeSlots}
          />
          <ProfileFact
            label={nodeIsOnline ? 'GitHub eligible' : 'Last known GitHub eligible'}
            value={profile.eligibleSlots ?? 'Unknown'}
          />
          <ProfileFact
            label={nodeIsOnline ? 'Draining' : 'Last known draining'}
            value={profile.drainingSlots}
          />
          <ProfileFact
            label={nodeIsOnline ? 'Busy workers' : 'Last known busy workers'}
            value={workload.busyLabel}
          />
        </dl>
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-md bg-muted/35 px-3 py-2 text-sm">
          <span>
            <span className="font-medium">
              {nodeIsOnline ? 'CPU / memory: ' : 'Last known CPU / memory: '}
            </span>
            {resources.reportingSources > 0
              ? `${formatCpuCores(resources.cpuCores)} / ${formatBytes(resources.memoryWorkingSetBytes)}`
              : 'Unavailable'}
          </span>
          <span className="flex items-center gap-2">
            <span className="text-xs text-muted-foreground">
              {resources.reportingSources} of {resources.totalSources} sources
            </span>
            <StatusBadge status={resources.status} />
          </span>
        </div>
        {resources.status === 'partial' ? (
          <StateBanner className="py-3 text-sm" tone="caution">
            Partial telemetry: totals include only reporting manager and worker sources.
          </StateBanner>
        ) : null}
        {profile.autoscaling?.lastError ? (
          <StateBanner className="py-3 text-sm" tone="critical">
            Autoscaling error: {profile.autoscaling.lastError}
          </StateBanner>
        ) : null}
      </div>
    </OperationalRow>
  );
}

function ProfileFact({ label, value }: { readonly label: string; readonly value: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 font-semibold tabular-nums">{value}</dd>
    </div>
  );
}

/** Provides shared node identity, status, and route-level secondary navigation. */
export function NodeDetailLayout() {
  const { tenantId = '', nodeId = '' } = useParams();
  const { session } = useSession();
  const { fleet, error, isLoading, refreshNow } = useFleet();
  const [diagnosticsPrepared, setDiagnosticsPrepared] = useState(false);
  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);
  const canAdminister = tenant?.role === 'administrator' || tenant?.role === 'owner';
  const node = fleet?.nodes.find((candidate) => candidate.nodeId === nodeId);

  if (isLoading && !fleet) return <LoadingState label="Loading node status" />;

  if (!fleet) {
    return <StateBanner tone="critical">{error ?? 'Node status is unavailable.'}</StateBanner>;
  }

  if (!node) {
    return (
      <section className="grid gap-4">
        {error ? <StateBanner tone="caution">Showing stale fleet data. {error}</StateBanner> : null}
        <DetailPanel
          title="Node not found"
          description={`No node with ID ${nodeId} exists in this tenant's fleet projection.`}
        >
          <StateBanner tone="critical">
            Return to the fleet and select a node from the current projection.
          </StateBanner>
        </DetailPanel>
      </section>
    );
  }

  const status = getNodeStatus(node);
  const aggregate = aggregateNode(node);
  const workload = summarizeNodeWorkload(node.profiles);
  const nodeIncidents = fleet.activeIncidents.filter((incident) => incident.nodeId === node.nodeId);
  const basePath = `/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(node.nodeId)}`;
  const navigation = [
    {
      label: 'Overview',
      description: 'Readiness, pressure, and host evidence',
      path: basePath,
      badge: nodeIncidents.length > 0 ? String(nodeIncidents.length) : undefined,
    },
    {
      label: 'Profiles',
      description: 'Manager and capacity inventory',
      path: `${basePath}/profiles`,
      badge: node.profiles.length > 0 ? String(node.profiles.length) : undefined,
    },
    {
      label: 'History',
      description: 'Retained node observations',
      path: `${basePath}/history`,
    },
    ...(canAdminister
      ? [
          {
            label: 'Administration',
            description: 'Identity and credential lifecycle',
            path: `${basePath}/administration`,
          },
        ]
      : []),
  ];

  return (
    <section className="grid gap-4">
      <EntityHeader
        title={node.displayName}
        identifier={<CopyableId label={`${node.displayName} node ID`} value={node.nodeId} />}
        actions={
          <>
            <Button
              data-testid={`prepare-diagnostics-${node.nodeId}`}
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
            <StatusBadge status={status} />
            {node.credentialRotationRequested ? <StatusBadge status="rotation requested" /> : null}
          </>
        }
      />
      <p className="text-sm text-muted-foreground">
        Current or last-known node evidence, affected profiles, active workloads, and safe capacity
        actions.
      </p>

      {error ? <StateBanner tone="caution">Showing stale fleet data. {error}</StateBanner> : null}
      <ActiveIncidentSummary
        incidents={nodeIncidents}
        tenantId={tenantId}
        testId={`node-active-incidents-${node.nodeId}`}
      />
      {status === 'offline' ? (
        <StateBanner tone="caution">
          This node is offline. Every connector, profile, capacity, resource, and hardware value
          below is last known from {formatTime(node.lastSeenAt)} unless a more specific source
          timestamp is shown.
        </StateBanner>
      ) : null}
      {diagnosticsPrepared ? (
        <p className="text-sm text-muted-foreground" role="status">
          Diagnostics context downloaded. Add the exact affected GitHub run or job before host
          collection.
        </p>
      ) : null}
      {status === 'revoked' ? (
        <StateBanner tone="critical">
          This node is revoked and cannot synchronize until it re-enrolls.
        </StateBanner>
      ) : null}

      <ReadinessSummary
        title="Node readiness"
        description="Current or last-known evidence used to choose the next node or profile investigation."
        status={<StatusBadge status={status} />}
        items={[
          {
            label: 'Last connector contact',
            value: formatTime(node.lastSeenAt),
            detail: node.isOnline ? 'Connector currently online' : 'Last-known connector evidence',
          },
          {
            label: 'Profiles',
            value: node.profiles.length,
            detail: `${aggregate.activeSlots} of ${aggregate.configuredSlots} local slots`,
          },
          {
            label: 'Current work',
            value: workload.busyLabel,
            detail: `${workload.runningJobsLabel} running jobs · ${workload.runningJobsDetail}`,
          },
          {
            label: 'Active incidents',
            value: nodeIncidents.length,
            detail:
              nodeIncidents.length > 0
                ? 'Review incident evidence before acting'
                : 'No active incident retained',
          },
        ]}
      />
      <TaskWorkspace navigationLabel={`${node.displayName} tasks`} navigationItems={navigation}>
        <Outlet
          context={
            {
              tenantId,
              node,
              canAdminister,
              antiforgeryToken: session?.antiforgeryToken ?? '',
              refreshNow,
            } satisfies NodeDetailContext
          }
        />
      </TaskWorkspace>
    </section>
  );
}

/** Renders node identity and profile triage without detailed operational evidence. */
export function NodeOverviewPage() {
  const { tenantId, node, canAdminister, antiforgeryToken, refreshNow } = useNodeDetail();
  const { fleet } = useFleet();
  const isDesktop = useDesktopOverview();
  const [pauseProfileId, setPauseProfileId] = useState<string | null>(null);
  const [pauseError, setPauseError] = useState<string | null>(null);
  const aggregate = aggregateNode(node);
  const workload = summarizeNodeWorkload(node.profiles);
  const incidents = (fleet?.activeIncidents ?? []).filter(
    (incident) => incident.nodeId === node.nodeId,
  );
  const nodeStatus = getNodeStatus(node);
  const connectorStatus =
    node.connectorHealth?.snapshot == null
      ? 'unavailable'
      : node.isOnline
        ? node.connectorHealth.snapshot.state
        : 'last known';
  const hardwareStatus =
    node.hardware == null
      ? 'unreported'
      : !node.isOnline
        ? 'last known'
        : node.hardware.status === 'current'
          ? 'latest reported'
          : node.hardware.status;
  const pressureStatus = incidents.some((incident) => incident.severity === 'critical')
    ? 'critical'
    : incidents.length > 0
      ? 'warning'
      : workload.confirmedBusyWorkers > 0 || workload.reportedRunningJobs > 0
        ? 'running'
        : 'current';
  const pauseProfile = async (profileId: string) => {
    setPauseProfileId(profileId);
    setPauseError(null);
    try {
      await setCapacityMaximum(tenantId, node.nodeId, profileId, 0, antiforgeryToken);
      await refreshNow();
    } catch (caught) {
      setPauseError(caught instanceof Error ? caught.message : 'Pause could not be queued.');
    } finally {
      setPauseProfileId(null);
    }
  };

  return (
    <div className="grid gap-2 md:gap-4">
      <ResponsiveOverviewSection
        isDesktop={isDesktop}
        status={nodeStatus}
        summary={`${node.connectorVersion || 'Unknown connector'} · ${aggregate.activeSlots} of ${aggregate.configuredSlots} local slots`}
        testId="node-overview-section-identity"
        title="Node identity"
      >
        <Card>
          <CardHeader>
            <CardTitle as="h3">Node identity</CardTitle>
            <CardDescription>
              Connector, enrollment, and current aggregate capacity.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <dl className="grid gap-3 text-sm sm:grid-cols-3 xl:grid-cols-6">
              <div>
                <dt className="text-xs text-muted-foreground uppercase">
                  {node.isOnline ? 'Connector' : 'Last known connector'}
                </dt>
                <dd className="mt-1 font-medium">{node.connectorVersion || 'Unknown'}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">Last seen</dt>
                <dd className="mt-1 font-medium">{formatTime(node.lastSeenAt)}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">Enrolled</dt>
                <dd className="mt-1 font-medium">{formatTime(node.enrolledAt)}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">
                  {node.isOnline ? 'Configured slots' : 'Last known configured slots'}
                </dt>
                <dd className="mt-1 font-medium tabular-nums">{aggregate.configuredSlots}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">
                  {node.isOnline ? 'Local slots' : 'Last known local slots'}
                </dt>
                <dd className="mt-1 font-medium tabular-nums">{aggregate.activeSlots}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">
                  {node.isOnline ? 'GitHub eligible' : 'Last known GitHub eligible'}
                </dt>
                <dd className="mt-1 font-medium tabular-nums">
                  {aggregate.eligibleSlots ?? 'Unknown'}
                </dd>
              </div>
            </dl>
          </CardContent>
        </Card>
      </ResponsiveOverviewSection>

      <ResponsiveOverviewSection
        isDesktop={isDesktop}
        status={pressureStatus}
        summary={`${workload.busyLabel} · ${workload.runningJobsLabel} running jobs`}
        testId="node-overview-section-pressure"
        title="Pressure and workloads"
      >
        <NodePressureCommandCenter
          activeIncidents={fleet?.activeIncidents ?? []}
          canAdminister={canAdminister}
          disabled={pauseProfileId !== null || !node.isOnline || node.isRevoked}
          generatedAt={fleet?.generatedAt ?? node.lastSeenAt ?? node.enrolledAt}
          node={node}
          onPause={pauseProfile}
          tenantId={tenantId}
        />
      </ResponsiveOverviewSection>
      {pauseError ? (
        <StateBanner role="alert" tone="critical">
          {pauseError}
        </StateBanner>
      ) : null}

      <ResponsiveOverviewSection
        isDesktop={isDesktop}
        status={connectorStatus}
        summary={
          node.connectorHealth?.snapshot == null
            ? 'No retained connector evidence'
            : `${node.connectorHealth.snapshot.consecutiveFailures} consecutive failures`
        }
        testId="node-overview-section-connector"
        title="Connector health"
      >
        <ConnectorHealthSummary node={node} />
      </ResponsiveOverviewSection>

      <ResponsiveOverviewSection
        isDesktop={isDesktop}
        status={hardwareStatus}
        summary={
          node.hardware == null
            ? 'Hardware inventory not reported'
            : `${node.hardware.processorModel ?? 'Processor unavailable'} · ${
                node.hardware.memoryBytes == null
                  ? 'Memory unavailable'
                  : formatBytes(node.hardware.memoryBytes)
              }`
        }
        testId="node-overview-section-hardware"
        title="Host hardware"
      >
        <HostHardwareCard
          hardware={node.hardware ?? null}
          isOnline={node.isOnline}
          lastSeenAt={node.lastSeenAt}
        />
      </ResponsiveOverviewSection>
    </div>
  );
}

/** Renders attention-ranked profile inventory separately from host-level evidence. */
export function NodeProfilesPage() {
  const { tenantId, node } = useNodeDetail();
  const { fleet } = useFleet();
  const isDesktop = useDesktopOverview();
  const [profilePresentation, setProfilePresentation] = useState<ProfilePresentation>(() =>
    globalThis.localStorage.getItem(profileComparisonViewStorageKey) === 'table'
      ? 'table'
      : 'cards',
  );
  const profileIncidents = (fleet?.activeIncidents ?? []).filter(
    (incident) => incident.nodeId === node.nodeId && incident.profileId != null,
  );
  const sortedProfiles = [...node.profiles].sort((left, right) => {
    const rankDifference =
      summarizeProfileAttention(left, profileIncidents).rank -
      summarizeProfileAttention(right, profileIncidents).rank;
    if (rankDifference !== 0) return rankDifference;
    return left.profileId.localeCompare(right.profileId);
  });
  const effectiveProfilePresentation = isDesktop ? profilePresentation : 'cards';
  const changeProfilePresentation = (next: ProfilePresentation) => {
    setProfilePresentation(next);
    globalThis.localStorage.setItem(profileComparisonViewStorageKey, next);
  };

  return (
    <section aria-labelledby="node-profiles-heading" className="grid min-w-0 gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 id="node-profiles-heading" className="text-xl font-semibold">
            Profiles
          </h2>
          <p className="mt-1 max-w-[72ch] text-sm text-muted-foreground">
            Profiles requiring attention appear before ordinary manager inventory. Select one to
            investigate its capacity, workers, diagnostics, history, or recovery.
          </p>
        </div>
        {sortedProfiles.length > 0 ? (
          <div
            aria-label="Profile presentation"
            className="hidden items-center rounded-lg border bg-muted/40 p-1 md:flex"
            role="group"
          >
            <Button
              aria-pressed={profilePresentation === 'cards'}
              size="sm"
              type="button"
              variant={profilePresentation === 'cards' ? 'secondary' : 'ghost'}
              onClick={() => changeProfilePresentation('cards')}
            >
              <Rows3 aria-hidden="true" />
              List
            </Button>
            <Button
              aria-pressed={profilePresentation === 'table'}
              size="sm"
              type="button"
              variant={profilePresentation === 'table' ? 'secondary' : 'ghost'}
              onClick={() => changeProfilePresentation('table')}
            >
              <Table2 aria-hidden="true" />
              Table
            </Button>
          </div>
        ) : null}
      </div>
      {sortedProfiles.length === 0 ? (
        <EmptyState
          headingLevel="h3"
          title="No profiles reported"
          description="The connector has not reported any profile observations for this node."
        />
      ) : effectiveProfilePresentation === 'table' ? (
        <ProfileComparisonTable
          formatProfileName={formatProfileDisplayName}
          nodeId={node.nodeId}
          nodeIsOnline={node.isOnline}
          profiles={sortedProfiles}
          tenantId={tenantId}
        />
      ) : (
        <OperationalList label="Node profiles">
          {sortedProfiles.map((profile) => (
            <ProfileSummary
              key={profile.profileId}
              profile={profile}
              attention={summarizeProfileAttention(profile, profileIncidents)}
              tenantId={tenantId}
              nodeId={node.nodeId}
              nodeIsOnline={node.isOnline}
            />
          ))}
        </OperationalList>
      )}
    </section>
  );
}

/** Renders bounded retained history for every profile reported by the node. */
export function NodeHistoryPage() {
  const { tenantId, node } = useNodeDetail();
  return (
    <FleetHistoryPanel
      tenantId={tenantId}
      nodeId={node.nodeId}
      profileId={null}
      presentation="page"
      testId="node-history"
    />
  );
}

/** Renders authorized node identity and credential lifecycle operations. */
export function NodeAdministrationPage() {
  const { tenantId, node, antiforgeryToken, refreshNow } = useNodeDetail();
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [isMutating, setIsMutating] = useState(false);

  const mutate = async (operation: () => Promise<void>) => {
    setIsMutating(true);
    setMutationError(null);
    try {
      await operation();
      await refreshNow();
    } catch (caught) {
      setMutationError(caught instanceof Error ? caught.message : 'Node administration failed.');
    } finally {
      setIsMutating(false);
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle as="h2">Node administration</CardTitle>
        <CardDescription>
          Rename this node, rotate its connector credential, or revoke its enrollment.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        {mutationError ? <StateBanner tone="critical">{mutationError}</StateBanner> : null}
        {isMutating ? (
          <p className="text-sm text-muted-foreground" role="status">
            Updating node…
          </p>
        ) : null}
        <DisplayNameEditor
          value={node.displayName}
          label="Server display name"
          submitLabel="Rename server"
          successMessage="Server name updated."
          onSave={async (displayName) => {
            await renameNode(tenantId, node.nodeId, displayName, antiforgeryToken);
            await refreshNow();
          }}
        />
        <div className="flex flex-wrap gap-2 border-t pt-4">
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={isMutating || node.isRevoked || node.credentialRotationRequested}
            onClick={() =>
              void mutate(() => requestCredentialRotation(tenantId, node.nodeId, antiforgeryToken))
            }
          >
            Rotate credential
          </Button>
          <ConfirmActionDialog
            title={`Revoke ${node.displayName}?`}
            description={`Revoke ${node.displayName}? The connector will stop synchronizing until it re-enrolls with a new one-time code.`}
            confirmLabel="Revoke node"
            confirmVariant="destructive"
            details={
              <ConfirmationSummary
                identity={[
                  { label: 'Node', value: node.displayName },
                  { label: 'Identifier', value: node.nodeId },
                ]}
                effects={[
                  'The connector stops synchronizing with this tenant until it re-enrolls with a new one-time code.',
                ]}
                prohibitedEffects={[
                  'No profile, worker, or capacity configuration on this node is changed.',
                ]}
              />
            }
            trigger={
              <Button
                type="button"
                size="sm"
                variant="destructive"
                disabled={isMutating || node.isRevoked}
              >
                Revoke
              </Button>
            }
            onConfirm={() => mutate(() => revokeNode(tenantId, node.nodeId, antiforgeryToken))}
          />
        </div>
      </CardContent>
    </Card>
  );
}
