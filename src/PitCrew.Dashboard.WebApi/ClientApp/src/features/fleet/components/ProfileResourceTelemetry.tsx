import { type ManagerObservedState, type ObservedSlot } from '@/core/fleet';
import { formatBytes, formatCpuCores, formatPids, formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

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

/** Renders one profile's current telemetry without synthesizing missing values. */
export function ProfileResourceTelemetry({ profile }: { readonly profile: ManagerObservedState }) {
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
          <h2 className="font-semibold">Point-in-time resource utilization</h2>
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
