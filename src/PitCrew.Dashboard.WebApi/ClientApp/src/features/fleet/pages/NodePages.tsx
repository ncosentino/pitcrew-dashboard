import { useEffect, useState, type ReactNode } from 'react';
import { ChevronDown, LayoutGrid, Table2 } from 'lucide-react';
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
import { EntityHeader } from '@/core/ui/EntityHeader';
import { SectionNavigation } from '@/core/ui/SectionNavigation';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ActiveIncidentSummary } from '../components/ActiveIncidentSummary';
import { ConnectorHealthSummary } from '../components/ConnectorHealthSummary';
import { FleetHistoryPanel } from '../components/FleetHistoryPanel';
import { HostHardwareCard } from '../components/HostHardwareSummary';
import { NodePressureCommandCenter } from '../components/NodePressureCommandCenter';
import { ProfileComparisonTable } from '../components/ProfileComparisonTable';
import { renameNode, requestCredentialRotation, revokeNode, setCapacityMaximum } from '../fleetApi';
import { downloadDiagnosticsContext } from '../diagnosticsDownload';
import { aggregateNode, aggregateProfileResources, getNodeStatus } from '../nodeSummary';

interface NodeDetailContext {
  readonly tenantId: string;
  readonly node: FleetNode;
  readonly canAdminister: boolean;
  readonly antiforgeryToken: string;
  readonly refreshNow: () => Promise<void>;
}

interface ProfileSummaryProps {
  readonly profile: ManagerObservedState;
  readonly tenantId: string;
  readonly nodeId: string;
  readonly nodeIsOnline: boolean;
  readonly isDesktop: boolean;
}

interface OverviewSummaryCardProps {
  readonly label: string;
  readonly value: ReactNode;
  readonly description: string;
  readonly status?: string;
  readonly testId: string;
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

function countPauseReadyProfiles(node: FleetNode): number {
  return node.capacityControls.filter((control) => {
    const active =
      control.latestCommand?.status === 'pending' || control.latestCommand?.status === 'delivered';
    const recovery = node.recoveryControls.find(
      (candidate) => candidate.profileId === control.profileId,
    );
    return (
      control.supportsZeroMaximum &&
      control.currentMaximum > 0 &&
      !active &&
      recovery?.operationActive !== true
    );
  }).length;
}

function OverviewSummaryCard({
  label,
  value,
  description,
  status,
  testId,
}: OverviewSummaryCardProps) {
  return (
    <div
      className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 px-4 py-3 sm:block sm:p-0"
      data-testid={testId}
    >
      <span className="text-xs font-semibold text-muted-foreground uppercase">{label}</span>
      <strong className="text-xl font-semibold tabular-nums">{value}</strong>
      {status ? (
        <span className="col-start-2 row-span-2 row-start-1 justify-self-end sm:mt-2 sm:block">
          <StatusBadge status={status} />
        </span>
      ) : null}
      <p className="hidden text-xs text-muted-foreground sm:mt-2 sm:block">{description}</p>
    </div>
  );
}

function ProfileSummary({
  profile,
  tenantId,
  nodeId,
  nodeIsOnline,
  isDesktop,
}: ProfileSummaryProps) {
  const configured =
    profile.configuredSlots ?? profile.autoscaling?.maximumSlots ?? profile.desiredSlots;
  const resources = aggregateProfileResources(profile);
  const profileName = formatProfileDisplayName(profile.profileId);

  return (
    <ResponsiveOverviewSection
      isDesktop={isDesktop}
      status={profile.managerStatus}
      summary={`${configured} configured · ${profile.activeSlots} local · ${profile.eligibleSlots ?? 'unknown'} eligible`}
      testId={`node-profile-disclosure-${profile.profileId}`}
      title={profileName}
    >
      <Card data-testid={`node-profile-${profile.profileId}`}>
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="grid gap-2">
              <CardTitle as="h3">
                <Link
                  className="text-link underline-offset-4 hover:underline"
                  to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(nodeId)}/profiles/${encodeURIComponent(profile.profileId)}`}
                >
                  {profileName}
                </Link>
              </CardTitle>
              <CardDescription className="flex flex-wrap items-center gap-x-3 gap-y-1">
                <CopyableId
                  label={`${profile.profileId} profile ID`}
                  prefix="Profile ID"
                  value={profile.profileId}
                />
                <span>{profile.scope} scope</span>
                <span>generation {profile.generation}</span>
                <span>
                  {nodeIsOnline ? 'observed' : 'last-known observation'}{' '}
                  {formatTime(profile.observedAt)}
                </span>
              </CardDescription>
            </div>
            <div className="flex flex-wrap gap-2">
              <StatusBadge status={profile.managerStatus} />
              <StatusBadge status={profile.desiredStateStatus} />
              {profile.autoscaling ? <StatusBadge status={profile.autoscaling.status} /> : null}
            </div>
          </div>
        </CardHeader>
        <CardContent className="grid gap-4">
          <dl className="grid gap-3 text-sm sm:grid-cols-5">
            <div>
              <dt className="text-xs text-muted-foreground uppercase">
                {nodeIsOnline ? 'Configured' : 'Last known configured'}
              </dt>
              <dd className="mt-1 font-semibold tabular-nums">{configured}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground uppercase">
                {nodeIsOnline ? 'Desired' : 'Last known desired'}
              </dt>
              <dd className="mt-1 font-semibold tabular-nums">{profile.desiredSlots}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground uppercase">
                {nodeIsOnline ? 'Local slots' : 'Last known local slots'}
              </dt>
              <dd className="mt-1 font-semibold tabular-nums">{profile.activeSlots}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground uppercase">
                {nodeIsOnline ? 'GitHub eligible' : 'Last known GitHub eligible'}
              </dt>
              <dd className="mt-1 font-semibold tabular-nums">
                {profile.eligibleSlots ?? 'Unknown'}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground uppercase">
                {nodeIsOnline ? 'Draining' : 'Last known draining'}
              </dt>
              <dd className="mt-1 font-semibold tabular-nums">{profile.drainingSlots}</dd>
            </div>
          </dl>
          <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border bg-muted/20 px-3 py-2 text-sm">
            <div>
              <span className="font-medium">
                {nodeIsOnline ? 'CPU / memory: ' : 'Last known CPU / memory: '}
              </span>
              {resources.reportingSources > 0
                ? `${formatCpuCores(resources.cpuCores)} / ${formatBytes(resources.memoryWorkingSetBytes)}`
                : 'Unavailable'}
            </div>
            <div className="flex items-center gap-2">
              <span className="text-xs text-muted-foreground">
                {resources.reportingSources} of {resources.totalSources} sources
              </span>
              <StatusBadge status={resources.status} />
            </div>
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
        </CardContent>
      </Card>
    </ResponsiveOverviewSection>
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

  if (isLoading && !fleet) return <p className="text-muted-foreground">Loading node…</p>;

  if (!fleet) {
    return <StateBanner tone="critical">{error ?? 'Node status is unavailable.'}</StateBanner>;
  }

  if (!node) {
    return (
      <section className="grid gap-4">
        {error ? <StateBanner tone="caution">Showing stale fleet data. {error}</StateBanner> : null}
        <Card>
          <CardHeader>
            <CardTitle as="h2">Node not found</CardTitle>
            <CardDescription>
              No node with ID {nodeId} exists in this tenant&apos;s fleet projection.
            </CardDescription>
          </CardHeader>
        </Card>
      </section>
    );
  }

  const status = getNodeStatus(node);
  const basePath = `/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(node.nodeId)}`;
  const navigation = [
    { label: 'Overview', path: basePath },
    { label: 'History', path: `${basePath}/history` },
    ...(canAdminister ? [{ label: 'Administration', path: `${basePath}/administration` }] : []),
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
        incidents={fleet.activeIncidents.filter((incident) => incident.nodeId === node.nodeId)}
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

      <SectionNavigation label={`${node.displayName} navigation`} items={navigation} />
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
  const [profilePresentation, setProfilePresentation] = useState<ProfilePresentation>(() =>
    globalThis.localStorage.getItem(profileComparisonViewStorageKey) === 'table'
      ? 'table'
      : 'cards',
  );
  const aggregate = aggregateNode(node);
  const sortedProfiles = [...node.profiles].sort((left, right) =>
    left.profileId < right.profileId ? -1 : left.profileId > right.profileId ? 1 : 0,
  );
  const incidents = (fleet?.activeIncidents ?? []).filter(
    (incident) => incident.nodeId === node.nodeId,
  );
  const busyWorkers = node.profiles.reduce(
    (count, profile) => count + profile.slots.filter((slot) => slot.activity === 'busy').length,
    0,
  );
  const runningJobs = node.profiles.reduce(
    (count, profile) => count + (profile.autoscaling?.runningJobs ?? 0),
    0,
  );
  const affectedProfiles = new Set(
    incidents.flatMap((incident) => (incident.profileId ? [incident.profileId] : [])),
  );
  const pauseReadyProfiles = countPauseReadyProfiles(node);
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
      : busyWorkers > 0 || runningJobs > 0
        ? 'running'
        : 'current';
  const effectiveProfilePresentation = isDesktop ? profilePresentation : 'cards';
  const changeProfilePresentation = (next: ProfilePresentation) => {
    setProfilePresentation(next);
    globalThis.localStorage.setItem(profileComparisonViewStorageKey, next);
  };
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
      <section
        aria-label="Node triage summary"
        className="divide-y rounded-lg border bg-card sm:grid sm:grid-cols-2 sm:gap-5 sm:divide-y-0 sm:p-4 xl:grid-cols-4"
      >
        <OverviewSummaryCard
          label="What's wrong"
          value={
            incidents.length > 0
              ? `${incidents.length} active`
              : node.isOnline && node.connectorHealth?.snapshot.state === 'degraded'
                ? 'Connector degraded'
                : node.isOnline
                  ? 'No active incident'
                  : 'Node offline'
          }
          description={
            incidents.length > 0
              ? 'Active incident evidence is attached below with pressure and affected-profile detail.'
              : node.isOnline
                ? 'No active incident is retained for this node right now.'
                : 'Operational evidence below is last-known until the connector returns.'
          }
          status={
            incidents.some((incident) => incident.severity === 'critical')
              ? 'critical'
              : incidents.length > 0 || !node.isOnline
                ? 'warning'
                : 'healthy'
          }
          testId={`node-overview-incidents-${node.nodeId}`}
        />
        <OverviewSummaryCard
          label="What's affected"
          value={
            incidents.length > 0
              ? `${affectedProfiles.size} named ${affectedProfiles.size === 1 ? 'profile' : 'profiles'}`
              : 'No active impact'
          }
          description={
            incidents.length > 0
              ? `${sortedProfiles.length} ${sortedProfiles.length === 1 ? 'profile is' : 'profiles are'} reported; node-wide incidents may not name one.`
              : `${sortedProfiles.length} ${sortedProfiles.length === 1 ? 'profile is' : 'profiles are'} available for inspection.`
          }
          status={incidents.length > 0 ? 'warning' : 'available'}
          testId={`node-overview-profiles-${node.nodeId}`}
        />
        <OverviewSummaryCard
          label="What's running"
          value={`${aggregate.activeSlots} local · ${runningJobs} jobs`}
          description={`${busyWorkers} busy workers reported across ${aggregate.configuredSlots} configured slots.`}
          status={busyWorkers > 0 || runningJobs > 0 ? 'running' : 'idle'}
          testId={`node-overview-workloads-${node.nodeId}`}
        />
        <OverviewSummaryCard
          label="What's safe to do"
          value={
            !canAdminister
              ? 'Read only'
              : !node.isOnline || node.isRevoked
                ? 'No capacity action'
                : pauseReadyProfiles > 0
                  ? `${pauseReadyProfiles} pause-ready`
                  : 'Observe only'
          }
          description={
            !canAdminister
              ? 'Authorized operators can pause new work or administer this node.'
              : !node.isOnline || node.isRevoked
                ? 'Capacity changes stay blocked until the connector is online and not revoked.'
                : 'Pause actions only fence new work; busy workers continue until their current job finishes.'
          }
          status={
            !canAdminister
              ? 'unavailable'
              : !node.isOnline || node.isRevoked
                ? 'warning'
                : pauseReadyProfiles > 0
                  ? 'available'
                  : 'current'
          }
          testId={`node-overview-actions-${node.nodeId}`}
        />
      </section>

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
        summary={`${busyWorkers} busy ${busyWorkers === 1 ? 'worker' : 'workers'} · ${runningJobs} GitHub ${runningJobs === 1 ? 'job' : 'jobs'}`}
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

      <ResponsiveOverviewSection
        isDesktop={isDesktop}
        status={sortedProfiles.length > 0 ? 'available' : 'unavailable'}
        summary={`${sortedProfiles.length} ${sortedProfiles.length === 1 ? 'profile' : 'profiles'} · ${affectedProfiles.size} named in incidents`}
        testId="node-overview-section-profiles"
        title="Profiles"
      >
        <section className="grid gap-3">
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div>
              <h3 className="text-xl font-semibold">Profiles</h3>
              <p className="text-sm text-muted-foreground">
                Capacity and health summaries from the latest manager observations.
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
                  <LayoutGrid aria-hidden="true" />
                  Cards
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
            <Card>
              <CardHeader>
                <CardTitle as="h3">No profiles reported</CardTitle>
                <CardDescription>
                  The connector has not reported any profile observations for this node.
                </CardDescription>
              </CardHeader>
            </Card>
          ) : effectiveProfilePresentation === 'table' ? (
            <ProfileComparisonTable
              formatProfileName={formatProfileDisplayName}
              nodeId={node.nodeId}
              nodeIsOnline={node.isOnline}
              profiles={sortedProfiles}
              tenantId={tenantId}
            />
          ) : (
            sortedProfiles.map((profile) => (
              <ProfileSummary
                isDesktop={isDesktop}
                key={profile.profileId}
                profile={profile}
                tenantId={tenantId}
                nodeId={node.nodeId}
                nodeIsOnline={node.isOnline}
              />
            ))
          )}
        </section>
      </ResponsiveOverviewSection>
    </div>
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
