import { useState, type ReactNode } from 'react';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { DisplayNameEditor } from '@/components/DisplayNameEditor';
import {
  useFleet,
  type CapacityControlState,
  type FleetResponse,
  type ManagerObservedState,
  type ObservedSlot,
} from '@/core/fleet';
import {
  formatBytes,
  formatCpuCores,
  formatPids,
  formatSeconds,
  formatTime,
} from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { cn } from '@/lib/utils';

import { renameNode, requestCredentialRotation, revokeNode, setCapacityMaximum } from './fleetApi';

/** Props for tenant-scoped fleet visibility and node administration. */
export interface FleetDashboardProps {
  readonly tenantId: string;
  readonly canAdminister: boolean;
  readonly antiforgeryToken: string;
}

interface MutationError {
  readonly message: string;
  readonly fleet: FleetResponse | null;
}

function formatScaleDownCountdown(value: string | null): string {
  if (value === null) return 'Not scheduled';
  const secondsRemaining = Math.max(0, Math.ceil((new Date(value).getTime() - Date.now()) / 1_000));
  const schedule = formatTime(value);
  return secondsRemaining === 0
    ? `Due now · ${schedule}`
    : `${formatSeconds(secondsRemaining)} remaining · ${schedule}`;
}

interface CapacityMetricProps {
  readonly label: string;
  readonly value: ReactNode;
  readonly testId: string;
}

function CapacityMetric({ label, value, testId }: CapacityMetricProps) {
  return (
    <div className="bg-background px-3 py-3">
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 text-2xl font-semibold tabular-nums" data-testid={testId}>
        {value}
      </dd>
    </div>
  );
}

interface CapacityMaximumControlProps {
  readonly control: CapacityControlState;
  readonly disabled: boolean;
  readonly onSetMaximum: (maximum: number) => Promise<void>;
}

function CapacityMaximumControl({ control, disabled, onSetMaximum }: CapacityMaximumControlProps) {
  const [draft, setDraft] = useState(String(control.currentMaximum));

  const parsed = Number(draft);
  const active =
    control.latestCommand?.status === 'pending' || control.latestCommand?.status === 'delivered';
  const valid =
    Number.isInteger(parsed) &&
    parsed >= 1 &&
    parsed <= control.maximumAllowed &&
    parsed !== control.currentMaximum;

  return (
    <div
      className="grid gap-2 rounded-md border bg-background px-3 py-3"
      data-testid={`profile-capacity-control-${control.profileId}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <div className="text-sm font-medium">Capacity maximum</div>
          <div className="text-xs text-muted-foreground">
            Local ceiling {control.maximumAllowed} · generation {control.generation}
          </div>
        </div>
        {control.latestCommand ? <StatusBadge status={control.latestCommand.status} /> : null}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <label className="text-xs font-medium" htmlFor={`capacity-maximum-${control.profileId}`}>
          Absolute maximum
        </label>
        <input
          id={`capacity-maximum-${control.profileId}`}
          className="h-9 w-28 rounded-md border bg-background px-3 text-sm tabular-nums"
          type="number"
          min={1}
          max={control.maximumAllowed}
          value={draft}
          disabled={disabled || active}
          onChange={(event) => setDraft(event.target.value)}
        />
        <Button
          type="button"
          size="sm"
          disabled={disabled || active || !valid}
          onClick={() => {
            if (globalThis.confirm(`Set ${control.profileId} capacity maximum to ${parsed}?`)) {
              void onSetMaximum(parsed);
            }
          }}
        >
          Queue change
        </Button>
      </div>
      {control.latestCommand ? (
        <div className="text-xs text-muted-foreground">
          Requested {control.latestCommand.requestedMaximum} ·{' '}
          {control.latestCommand.resultMessage ?? 'Awaiting connector result.'}
        </div>
      ) : null}
    </div>
  );
}

interface CapacitySummaryProps {
  readonly profile: ManagerObservedState;
  readonly control: CapacityControlState | null;
  readonly canAdminister: boolean;
  readonly isMutating: boolean;
  readonly onSetMaximum: (maximum: number) => Promise<void>;
}

function CapacitySummary({
  profile,
  control,
  canAdminister,
  isMutating,
  onSetMaximum,
}: CapacitySummaryProps) {
  const autoscaling = profile.autoscaling ?? null;
  if (autoscaling === null) {
    return (
      <section
        className="grid gap-3 border-y bg-muted/10 px-4 py-4"
        data-testid={`profile-capacity-fixed-${profile.profileId}`}
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h4 className="font-semibold">Fixed capacity</h4>
            <p className="text-xs text-muted-foreground">
              The manager keeps the configured slot count active independent of queued demand.
            </p>
          </div>
          <StatusBadge status="fixed" />
        </div>
        <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-md border bg-border text-center sm:grid-cols-4">
          <CapacityMetric
            label="Configured"
            value={profile.configuredSlots ?? profile.desiredSlots}
            testId={`profile-capacity-configured-${profile.profileId}`}
          />
          <CapacityMetric
            label="Desired"
            value={profile.desiredSlots}
            testId={`profile-capacity-desired-${profile.profileId}`}
          />
          <CapacityMetric
            label="Active"
            value={profile.activeSlots}
            testId={`profile-capacity-active-${profile.profileId}`}
          />
          <CapacityMetric
            label="Draining"
            value={profile.drainingSlots}
            testId={`profile-capacity-draining-${profile.profileId}`}
          />
        </dl>
        {canAdminister && control ? (
          <CapacityMaximumControl
            key={`${control.profileId}-${control.currentMaximum}`}
            control={control}
            disabled={isMutating}
            onSetMaximum={onSetMaximum}
          />
        ) : null}
      </section>
    );
  }

  const capacityMetrics = [
    ['Maximum', autoscaling.maximumSlots, 'maximum'],
    ['Target', autoscaling.targetSlots, 'target'],
    ['Active', profile.activeSlots, 'active'],
    ['Draining', profile.drainingSlots, 'draining'],
    ['Assigned', autoscaling.assignedJobs, 'assigned'],
    ['Running', autoscaling.runningJobs, 'running'],
    ['Available / queued', autoscaling.availableJobs, 'available'],
    ['Idle', autoscaling.idleRunners, 'idle'],
    ['Busy', autoscaling.busyRunners, 'busy'],
    ['Minimum idle', autoscaling.minimumIdleSlots, 'minimum-idle'],
    ['Scale-set count', autoscaling.scaleSetCount, 'scale-set-count'],
    ['Scale-down delay', formatSeconds(autoscaling.scaleDownDelaySeconds), 'scale-down-delay'],
    [
      'Scale-down countdown',
      formatScaleDownCountdown(autoscaling.scaleDownAt),
      'scale-down-countdown',
    ],
  ] as const;

  return (
    <section
      className="grid gap-3 border-y bg-muted/10 px-4 py-4"
      data-testid={`profile-capacity-autoscaled-${profile.profileId}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h4 className="font-semibold">Demand-driven autoscaling</h4>
          <p className="text-xs text-muted-foreground">
            Capacity follows assigned work and the minimum-idle policy up to the configured maximum.
          </p>
        </div>
        <span data-testid={`profile-autoscaling-status-${profile.profileId}`}>
          <StatusBadge status={autoscaling.status} />
        </span>
      </div>
      <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-md border bg-border text-center sm:grid-cols-3 xl:grid-cols-5">
        {capacityMetrics.map(([label, value, key]) => (
          <CapacityMetric
            key={key}
            label={label}
            value={value}
            testId={`profile-capacity-${key}-${profile.profileId}`}
          />
        ))}
      </dl>
      {canAdminister && control ? (
        <CapacityMaximumControl
          key={`${control.profileId}-${control.currentMaximum}`}
          control={control}
          disabled={isMutating}
          onSetMaximum={onSetMaximum}
        />
      ) : null}
      <div
        className={cn(
          'rounded-md border px-3 py-2 text-sm',
          autoscaling.lastError
            ? 'border-red-300 bg-red-50 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100'
            : 'bg-background text-muted-foreground',
        )}
        data-testid={`profile-autoscaling-error-${profile.profileId}`}
      >
        <span className="font-medium">Last error:</span> {autoscaling.lastError || 'None'}
      </div>
    </section>
  );
}

function aggregateSlotResources(slots: ReadonlyArray<ObservedSlot>) {
  return slots.reduce(
    (aggregate, slot) => {
      if (!slot.resources) return aggregate;
      return {
        cpuCores: aggregate.cpuCores + slot.resources.cpuCores,
        memoryWorkingSetBytes:
          aggregate.memoryWorkingSetBytes + slot.resources.memoryWorkingSetBytes,
        pids: aggregate.pids + slot.resources.pids,
        reportingSlots: aggregate.reportingSlots + 1,
      };
    },
    {
      cpuCores: 0,
      memoryWorkingSetBytes: 0,
      pids: 0,
      reportingSlots: 0,
    },
  );
}

function ResourceTelemetrySummary({ profile }: { readonly profile: ManagerObservedState }) {
  const telemetry = profile.resourceTelemetry ?? null;
  const workerResources = aggregateSlotResources(profile.slots);
  const workerCoverage =
    profile.slots.length === 0
      ? 'No slots reported'
      : `${workerResources.reportingSlots} of ${profile.slots.length} slots reporting`;

  return (
    <section
      className="grid gap-3 border-b bg-muted/10 px-4 py-4"
      data-testid={`profile-resource-telemetry-${profile.profileId}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h4 className="font-semibold">Point-in-time resource utilization</h4>
          <p className="text-xs text-muted-foreground">
            Manager samples arrive roughly every 30 seconds; 5-second dashboard polling can repeat
            the same sample.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <StatusBadge status={telemetry?.status ?? 'unavailable'} />
          <span
            className="text-muted-foreground"
            data-testid={`profile-resource-sampled-${profile.profileId}`}
          >
            Sampled {telemetry ? formatTime(telemetry.sampledAt) : 'Unavailable'}
          </span>
        </div>
      </div>
      <dl className="grid gap-3 sm:grid-cols-3">
        <div className="rounded-md border bg-background px-3 py-3">
          <dt className="text-xs text-muted-foreground uppercase">Host capacity</dt>
          <dd
            className="mt-1 font-medium tabular-nums"
            data-testid={`profile-resource-host-${profile.profileId}`}
          >
            {telemetry?.host
              ? `${new Intl.NumberFormat(undefined).format(telemetry.host.logicalProcessorCount)} logical processors · ${formatBytes(telemetry.host.memoryBytes)}`
              : 'Unavailable'}
          </dd>
        </div>
        <div className="rounded-md border bg-background px-3 py-3">
          <dt className="text-xs text-muted-foreground uppercase">Manager usage</dt>
          <dd
            className="mt-1 font-medium tabular-nums"
            data-testid={`profile-resource-manager-${profile.profileId}`}
          >
            {telemetry?.manager
              ? `${formatCpuCores(telemetry.manager.cpuCores)} · ${formatBytes(telemetry.manager.memoryWorkingSetBytes)} · ${formatPids(telemetry.manager.pids)}`
              : 'Unavailable'}
          </dd>
        </div>
        <div className="rounded-md border bg-background px-3 py-3">
          <dt className="text-xs text-muted-foreground uppercase">Profile workers</dt>
          <dd
            className="mt-1 font-medium tabular-nums"
            data-testid={`profile-resource-workers-${profile.profileId}`}
          >
            {workerResources.reportingSlots > 0
              ? `${formatCpuCores(workerResources.cpuCores)} · ${formatBytes(workerResources.memoryWorkingSetBytes)} · ${formatPids(workerResources.pids)}`
              : 'Unavailable'}
          </dd>
          <div className="mt-1 text-xs text-muted-foreground">{workerCoverage}</div>
        </div>
      </dl>
    </section>
  );
}

function SlotRow({ slot }: { readonly slot: ObservedSlot }) {
  return (
    <tr className="border-t" data-testid={`slot-row-${slot.key}`}>
      <td className="px-3 py-2 font-mono text-xs">{slot.key}</td>
      <td className="px-3 py-2">{slot.repository ?? 'Shared scope'}</td>
      <td className="px-3 py-2" data-testid={`slot-target-${slot.key}`}>
        {slot.target ?? '—'}
      </td>
      <td className="px-3 py-2" data-testid={`slot-activity-${slot.key}`}>
        {slot.activity ? <StatusBadge status={slot.activity} /> : '—'}
      </td>
      <td className="px-3 py-2">
        <StatusBadge status={slot.state} />
      </td>
      <td className="px-3 py-2 text-right tabular-nums">{slot.failureCount}</td>
      <td className="px-3 py-2 text-right tabular-nums" data-testid={`slot-cpu-${slot.key}`}>
        {slot.resources ? formatCpuCores(slot.resources.cpuCores) : 'Unavailable'}
      </td>
      <td className="px-3 py-2 text-right tabular-nums" data-testid={`slot-memory-${slot.key}`}>
        {slot.resources ? formatBytes(slot.resources.memoryWorkingSetBytes) : 'Unavailable'}
      </td>
      <td className="px-3 py-2 text-right tabular-nums" data-testid={`slot-pids-${slot.key}`}>
        {slot.resources ? formatPids(slot.resources.pids) : 'Unavailable'}
      </td>
    </tr>
  );
}

/** Renders one tenant's live fleet plus authorized enrollment and node controls. */
export function FleetDashboard({ tenantId, canAdminister, antiforgeryToken }: FleetDashboardProps) {
  const { fleet, error, isLoading, refreshNow } = useFleet();
  const [mutationError, setMutationError] = useState<MutationError | null>(null);
  const [isMutating, setIsMutating] = useState(false);
  const currentMutationError = mutationError?.fleet === fleet ? mutationError.message : null;

  const mutate = async (operation: () => Promise<void>) => {
    setIsMutating(true);
    setMutationError(null);
    try {
      await operation();
      await refreshNow();
    } catch (caught) {
      setMutationError({
        message: caught instanceof Error ? caught.message : 'Fleet administration failed.',
        fleet,
      });
    } finally {
      setIsMutating(false);
    }
  };

  const renameServer = async (nodeId: string, displayName: string) => {
    await renameNode(tenantId, nodeId, displayName, antiforgeryToken);
    await refreshNow();
  };

  const queueCapacityMaximum = async (nodeId: string, profileId: string, maximum: number) => {
    await mutate(async () => {
      await setCapacityMaximum(tenantId, nodeId, profileId, maximum, antiforgeryToken);
    });
  };

  return (
    <>
      <section className="grid gap-2">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-2xl font-bold tracking-tight">Fleet status</h2>
            <p className="text-sm text-muted-foreground">
              Servers connect outbound and report credential-free manager observations.
            </p>
          </div>
          <div className="text-right text-sm text-muted-foreground">
            {fleet ? `Updated ${formatTime(fleet.generatedAt)}` : 'Waiting for status'}
          </div>
        </div>
      </section>

      {(currentMutationError ?? error) ? (
        <div className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100">
          {currentMutationError ?? error}
        </div>
      ) : null}

      {isLoading ? <p className="text-muted-foreground">Loading fleet status…</p> : null}

      {!isLoading && fleet?.nodes.length === 0 ? (
        <Card>
          <CardHeader>
            <CardTitle>No servers enrolled</CardTitle>
            <CardDescription>
              Create a one-time code, configure it on a connector, and start the connector.
            </CardDescription>
          </CardHeader>
        </Card>
      ) : null}

      <section className="grid gap-6">
        {fleet?.nodes.map((node) => (
          <Card key={node.nodeId}>
            <CardHeader>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <CardTitle className="text-xl">{node.displayName}</CardTitle>
                  <CardDescription>
                    Connector {node.connectorVersion || 'unknown'} · Last seen{' '}
                    {formatTime(node.lastSeenAt)}
                  </CardDescription>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <StatusBadge
                    status={node.isRevoked ? 'revoked' : node.isOnline ? 'online' : 'offline'}
                  />
                  {node.credentialRotationRequested ? (
                    <StatusBadge status="rotation requested" />
                  ) : null}
                </div>
              </div>
              {canAdminister ? (
                <div className="grid gap-3 pt-3">
                  <DisplayNameEditor
                    value={node.displayName}
                    label="Server display name"
                    submitLabel="Rename server"
                    successMessage="Server name updated."
                    onSave={(displayName) => renameServer(node.nodeId, displayName)}
                  />
                  <div className="flex flex-wrap gap-2">
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      disabled={isMutating || node.isRevoked || node.credentialRotationRequested}
                      onClick={() =>
                        void mutate(() =>
                          requestCredentialRotation(tenantId, node.nodeId, antiforgeryToken),
                        )
                      }
                    >
                      Rotate credential
                    </Button>
                    <Button
                      type="button"
                      size="sm"
                      variant="destructive"
                      disabled={isMutating || node.isRevoked}
                      onClick={() => {
                        if (
                          globalThis.confirm(
                            `Revoke ${node.displayName}? The connector will stop synchronizing until it re-enrolls with a new one-time code.`,
                          )
                        ) {
                          void mutate(() => revokeNode(tenantId, node.nodeId, antiforgeryToken));
                        }
                      }}
                    >
                      Revoke
                    </Button>
                  </div>
                </div>
              ) : null}
            </CardHeader>
            <CardContent className="grid gap-4">
              {node.profiles.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  The connector has not reported any profile observations.
                </p>
              ) : null}
              {node.profiles.map((profile) => {
                const capacityControl =
                  node.capacityControls.find(
                    (control) => control.profileId === profile.profileId,
                  ) ?? null;
                return (
                  <section key={profile.profileId} className="overflow-hidden rounded-lg border">
                    <div className="flex flex-wrap items-center justify-between gap-3 bg-muted/50 px-4 py-3">
                      <div>
                        <h3 className="font-semibold">{profile.profileId}</h3>
                        <p className="text-sm text-muted-foreground">
                          {profile.scope} scope · generation {profile.generation} · manager contract{' '}
                          {profile.managerContractVersion}
                        </p>
                      </div>
                      <div className="flex items-center gap-2">
                        <StatusBadge status={profile.managerStatus} />
                        <StatusBadge status={profile.desiredStateStatus} />
                      </div>
                    </div>
                    <CapacitySummary
                      profile={profile}
                      control={capacityControl}
                      canAdminister={canAdminister}
                      isMutating={isMutating || !node.isOnline || node.isRevoked}
                      onSetMaximum={(maximum) =>
                        queueCapacityMaximum(node.nodeId, profile.profileId, maximum)
                      }
                    />
                    <ResourceTelemetrySummary profile={profile} />
                    <div className="overflow-x-auto">
                      <table className="w-full min-w-4xl text-left text-sm">
                        <thead className="bg-muted/30 text-xs text-muted-foreground uppercase">
                          <tr>
                            <th className="px-3 py-2 font-medium">Slot</th>
                            <th className="px-3 py-2 font-medium">Repository</th>
                            <th className="px-3 py-2 font-medium">Target</th>
                            <th className="px-3 py-2 font-medium">Activity</th>
                            <th className="px-3 py-2 font-medium">State</th>
                            <th className="px-3 py-2 text-right font-medium">Failures</th>
                            <th className="px-3 py-2 text-right font-medium">CPU cores</th>
                            <th className="px-3 py-2 text-right font-medium">Memory</th>
                            <th className="px-3 py-2 text-right font-medium">PIDs</th>
                          </tr>
                        </thead>
                        <tbody>
                          {profile.slots.map((slot) => (
                            <SlotRow key={slot.key} slot={slot} />
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </section>
                );
              })}
            </CardContent>
          </Card>
        ))}
      </section>
    </>
  );
}
