import { type ManagerObservedState, type ObservedSlot } from '@/core/fleet';
import {
  formatBytes,
  formatCpuCores,
  formatOptionalBytes,
  formatPids,
} from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { WorkerExitEvidence, WorkerImageIdentity } from '@/core/ui/WorkerEvidenceCells';

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
      <td className="px-3 py-2" data-testid={`slot-registration-${slot.key}`}>
        <StatusBadge status={slot.registrationStatus ?? 'unknown'} />
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
      <td className="px-3 py-2 text-right tabular-nums" data-testid={`slot-network-${slot.key}`}>
        {slot.resources
          ? `${formatOptionalBytes(slot.resources.networkRxBytes)} in · ${formatOptionalBytes(slot.resources.networkTxBytes)} out`
          : 'Unavailable'}
      </td>
      <td className="px-3 py-2 text-right tabular-nums" data-testid={`slot-block-io-${slot.key}`}>
        {slot.resources
          ? `${formatOptionalBytes(slot.resources.blockReadBytes)} read · ${formatOptionalBytes(slot.resources.blockWriteBytes)} written`
          : 'Unavailable'}
      </td>
      <td className="px-3 py-2" data-testid={`slot-image-${slot.key}`}>
        <WorkerImageIdentity imageId={slot.imageId} />
      </td>
      <td className="px-3 py-2" data-testid={`slot-last-exit-${slot.key}`}>
        <WorkerExitEvidence lastExit={slot.lastExit} />
      </td>
    </tr>
  );
}

/** Renders the current slot diagnostics for one profile. */
export function ProfileSlotsTable({ profile }: { readonly profile: ManagerObservedState }) {
  if (profile.slots.length === 0) {
    return (
      <section className="px-4 py-4">
        <h2 className="font-semibold">Slots</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          The manager has not reported any slots for this profile.
        </p>
      </section>
    );
  }

  return (
    <div className="overflow-x-auto">
      <p className="px-3 pt-3 text-xs text-muted-foreground">
        Cumulative I/O counters read Unavailable when the manager did not measure them and 0 B when
        a measured value is zero.
      </p>
      <table className="w-full min-w-4xl text-left text-sm">
        <caption className="px-3 py-2 text-left font-semibold">
          Slots for profile {profile.profileId}
        </caption>
        <thead className="bg-muted/30 text-xs text-muted-foreground uppercase">
          <tr>
            <th scope="col" className="px-3 py-2 font-medium">
              Slot
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Repository
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Target
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Activity
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              GitHub registration
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Local state
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              Failures
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              CPU cores
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              Memory
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              PIDs
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              Network I/O
            </th>
            <th scope="col" className="px-3 py-2 text-right font-medium">
              Block I/O
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Worker image
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Last exit
            </th>
          </tr>
        </thead>
        <tbody>
          {profile.slots.map((slot) => (
            <SlotRow key={slot.key} slot={slot} />
          ))}
        </tbody>
      </table>
    </div>
  );
}
