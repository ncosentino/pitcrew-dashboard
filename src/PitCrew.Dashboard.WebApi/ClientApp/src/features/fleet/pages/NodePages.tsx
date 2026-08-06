import { useState } from 'react';
import { Link, Outlet, useOutletContext, useParams } from 'react-router-dom';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { DisplayNameEditor } from '@/components/DisplayNameEditor';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';
import { useFleet, type FleetNode, type ManagerObservedState } from '@/core/fleet';
import { formatBytes, formatCpuCores, formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { EntitySectionNavigation } from '../components/EntitySectionNavigation';
import { FleetHistoryPanel } from '../components/FleetHistoryPanel';
import { HostHardwareCard } from '../components/HostHardwareSummary';
import { ActiveIncidentSummary } from '../components/ActiveIncidentSummary';
import { NodePressureCommandCenter } from '../components/NodePressureCommandCenter';
import { renameNode, requestCredentialRotation, revokeNode, setCapacityMaximum } from '../fleetApi';
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
}

function useNodeDetail(): NodeDetailContext {
  return useOutletContext<NodeDetailContext>();
}

function ProfileSummary({ profile, tenantId, nodeId }: ProfileSummaryProps) {
  const configured =
    profile.configuredSlots ?? profile.autoscaling?.maximumSlots ?? profile.desiredSlots;
  const resources = aggregateProfileResources(profile);

  return (
    <Card data-testid={`node-profile-${profile.profileId}`}>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle>
              <Link
                className="text-primary underline-offset-4 hover:underline"
                to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(nodeId)}/profiles/${encodeURIComponent(profile.profileId)}`}
              >
                {profile.profileId}
              </Link>
            </CardTitle>
            <CardDescription>
              {profile.scope} scope · generation {profile.generation} · observed{' '}
              {formatTime(profile.observedAt)}
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
            <dt className="text-xs text-muted-foreground uppercase">Configured</dt>
            <dd className="mt-1 font-semibold tabular-nums">{configured}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground uppercase">Desired</dt>
            <dd className="mt-1 font-semibold tabular-nums">{profile.desiredSlots}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground uppercase">Local slots</dt>
            <dd className="mt-1 font-semibold tabular-nums">{profile.activeSlots}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground uppercase">GitHub eligible</dt>
            <dd className="mt-1 font-semibold tabular-nums">
              {profile.eligibleSlots ?? 'Unknown'}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground uppercase">Draining</dt>
            <dd className="mt-1 font-semibold tabular-nums">{profile.drainingSlots}</dd>
          </div>
        </dl>
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border bg-muted/20 px-3 py-2 text-sm">
          <div>
            <span className="font-medium">Current CPU / memory: </span>
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
          <p className="text-sm text-amber-800 dark:text-amber-200">
            Partial telemetry: totals include only reporting manager and worker sources.
          </p>
        ) : null}
        {profile.autoscaling?.lastError ? (
          <p className="text-sm text-red-700 dark:text-red-300">
            Autoscaling error: {profile.autoscaling.lastError}
          </p>
        ) : null}
      </CardContent>
    </Card>
  );
}

/** Provides shared node identity, status, and route-level secondary navigation. */
export function NodeDetailLayout() {
  const { tenantId = '', nodeId = '' } = useParams();
  const { session } = useSession();
  const { fleet, error, isLoading, refreshNow } = useFleet();
  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);
  const canAdminister = tenant?.role === 'administrator' || tenant?.role === 'owner';
  const node = fleet?.nodes.find((candidate) => candidate.nodeId === nodeId);

  if (isLoading && !fleet) return <p className="text-muted-foreground">Loading node…</p>;

  if (!fleet) {
    return (
      <div
        className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
        role="alert"
      >
        {error ?? 'Node status is unavailable.'}
      </div>
    );
  }

  if (!node) {
    return (
      <section className="grid gap-4">
        {error ? (
          <div
            className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
            role="status"
          >
            Showing stale fleet data. {error}
          </div>
        ) : null}
        <Card>
          <CardHeader>
            <CardTitle>Node not found</CardTitle>
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
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-2xl font-bold tracking-tight">{node.displayName}</h2>
          <p className="font-mono text-xs text-muted-foreground">{node.nodeId}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge status={status} />
          {node.credentialRotationRequested ? <StatusBadge status="rotation requested" /> : null}
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
        incidents={fleet.activeIncidents.filter((incident) => incident.nodeId === node.nodeId)}
        tenantId={tenantId}
        testId={`node-active-incidents-${node.nodeId}`}
      />
      {status === 'offline' ? (
        <div className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
          This node is offline. Profile observations may no longer reflect current capacity.
        </div>
      ) : null}
      {status === 'revoked' ? (
        <div className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100">
          This node is revoked and cannot synchronize until it re-enrolls.
        </div>
      ) : null}

      <EntitySectionNavigation label={`${node.displayName} navigation`} items={navigation} />
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
  const [pauseProfileId, setPauseProfileId] = useState<string | null>(null);
  const [pauseError, setPauseError] = useState<string | null>(null);
  const aggregate = aggregateNode(node);
  const sortedProfiles = [...node.profiles].sort((left, right) =>
    left.profileId < right.profileId ? -1 : left.profileId > right.profileId ? 1 : 0,
  );
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
    <div className="grid gap-4">
      <Card>
        <CardHeader>
          <CardTitle>Node identity</CardTitle>
          <CardDescription>Connector, enrollment, and current aggregate capacity.</CardDescription>
        </CardHeader>
        <CardContent>
          <dl className="grid gap-3 text-sm sm:grid-cols-4">
            <div>
              <dt className="text-xs text-muted-foreground uppercase">Connector</dt>
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
                Configured / local / GitHub eligible
              </dt>
              <dd className="mt-1 font-medium tabular-nums">
                {aggregate.configuredSlots} / {aggregate.activeSlots} /{' '}
                {aggregate.eligibleSlots ?? 'Unknown'}
              </dd>
            </div>
          </dl>
        </CardContent>
      </Card>

      <HostHardwareCard hardware={node.hardware ?? null} />

      <NodePressureCommandCenter
        activeIncidents={fleet?.activeIncidents ?? []}
        canAdminister={canAdminister}
        disabled={pauseProfileId !== null || !node.isOnline || node.isRevoked}
        generatedAt={fleet?.generatedAt ?? node.lastSeenAt ?? node.enrolledAt}
        node={node}
        onPause={pauseProfile}
        tenantId={tenantId}
      />
      {pauseError ? (
        <p className="text-sm text-red-800 dark:text-red-200" role="alert">
          {pauseError}
        </p>
      ) : null}

      <section className="grid gap-3">
        <div>
          <h3 className="text-xl font-semibold">Profiles</h3>
          <p className="text-sm text-muted-foreground">
            Capacity and health summaries from the latest manager observations.
          </p>
        </div>
        {sortedProfiles.length === 0 ? (
          <Card>
            <CardHeader>
              <CardTitle>No profiles reported</CardTitle>
              <CardDescription>
                The connector has not reported any profile observations for this node.
              </CardDescription>
            </CardHeader>
          </Card>
        ) : (
          sortedProfiles.map((profile) => (
            <ProfileSummary
              key={profile.profileId}
              profile={profile}
              tenantId={tenantId}
              nodeId={node.nodeId}
            />
          ))
        )}
      </section>
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
        <CardTitle>Node administration</CardTitle>
        <CardDescription>
          Rename this node, rotate its connector credential, or revoke its enrollment.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        {mutationError ? (
          <div
            className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
            role="alert"
          >
            {mutationError}
          </div>
        ) : null}
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
