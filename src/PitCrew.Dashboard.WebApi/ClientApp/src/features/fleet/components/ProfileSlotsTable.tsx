import { type ManagerObservedState, type ObservedSlot } from '@/core/fleet';
import {
  formatBytes,
  formatCpuCores,
  formatOptionalBytes,
  formatPids,
} from '@/core/formatting/formatters';
import { ScrollableRegion } from '@/core/ui/ScrollableRegion';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { WorkerExitEvidence, WorkerImageIdentity } from '@/core/ui/WorkerEvidenceCells';

function SlotRow({ slot }: { readonly slot: ObservedSlot }) {
  const repository = slot.repository ?? 'Shared scope';
  const target = slot.target ?? '—';
  const resources = slot.resources;

  return (
    <tr className="border-t align-middle" data-testid={`slot-row-${slot.key}`}>
      <td className="min-w-36 whitespace-nowrap px-3 py-2 font-mono text-xs">{slot.key}</td>
      <td className="min-w-64 whitespace-nowrap px-3 py-2">{repository}</td>
      <td className="min-w-36 whitespace-nowrap px-3 py-2" data-testid={`slot-target-${slot.key}`}>
        {target}
      </td>
      <td className="min-w-64 px-3 py-2">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
          <span>
            <span className="sr-only">Job activity: {slot.activity ?? 'unavailable'}.</span>
            <span className="inline-flex items-center gap-1.5 whitespace-nowrap" aria-hidden="true">
              <span className="text-[10px] text-muted-foreground uppercase">Job</span>
              <span data-testid={`slot-activity-${slot.key}`}>
                {slot.activity ? <StatusBadge status={slot.activity} /> : '—'}
              </span>
            </span>
          </span>
          <span>
            <span className="sr-only">
              GitHub registration: {slot.registrationStatus ?? 'unknown'}.
            </span>
            <span className="inline-flex items-center gap-1.5 whitespace-nowrap" aria-hidden="true">
              <span className="text-[10px] text-muted-foreground uppercase">GitHub</span>
              <span data-testid={`slot-registration-${slot.key}`}>
                <StatusBadge status={slot.registrationStatus ?? 'unknown'} />
              </span>
            </span>
          </span>
          <span>
            <span className="sr-only">Local state: {slot.state}.</span>
            <span className="inline-flex items-center gap-1.5 whitespace-nowrap" aria-hidden="true">
              <span className="text-[10px] text-muted-foreground uppercase">Local</span>
              <span data-testid={`slot-local-state-${slot.key}`}>
                <StatusBadge status={slot.state} />
              </span>
            </span>
          </span>
        </div>
      </td>
      <td className="px-3 py-2 text-right tabular-nums">{slot.failureCount}</td>
      <td className="min-w-52 px-3 py-2" data-testid={`slot-last-exit-${slot.key}`}>
        <WorkerExitEvidence lastExit={slot.lastExit} />
      </td>
      <td className="min-w-64 whitespace-nowrap px-3 py-2 text-right text-xs tabular-nums">
        <span className="sr-only">
          {resources
            ? `CPU ${formatCpuCores(resources.cpuCores)}; memory ${formatBytes(resources.memoryWorkingSetBytes)}; ${formatPids(resources.pids)}.`
            : 'Worker resources unavailable.'}
        </span>
        <span aria-hidden="true">
          <span data-testid={`slot-cpu-${slot.key}`}>
            {resources ? formatCpuCores(resources.cpuCores) : 'Unavailable'}
          </span>
          <span> · </span>
          <span data-testid={`slot-memory-${slot.key}`}>
            {resources ? formatBytes(resources.memoryWorkingSetBytes) : 'Unavailable'}
          </span>
          <span> · </span>
          <span data-testid={`slot-pids-${slot.key}`}>
            {resources ? formatPids(resources.pids) : 'Unavailable'}
          </span>
        </span>
      </td>
      <td className="min-w-64 whitespace-nowrap px-3 py-2 text-right text-xs tabular-nums">
        <span className="sr-only">
          {resources
            ? `Network I/O ${formatOptionalBytes(resources.networkRxBytes)} in and ${formatOptionalBytes(resources.networkTxBytes)} out; block I/O ${formatOptionalBytes(resources.blockReadBytes)} read and ${formatOptionalBytes(resources.blockWriteBytes)} written.`
            : 'Worker I/O unavailable.'}
        </span>
        <div aria-hidden="true">
          <div data-testid={`slot-network-${slot.key}`}>
            {resources
              ? `${formatOptionalBytes(resources.networkRxBytes)} in · ${formatOptionalBytes(resources.networkTxBytes)} out`
              : 'Unavailable'}
          </div>
          <div className="text-muted-foreground" data-testid={`slot-block-io-${slot.key}`}>
            {resources
              ? `${formatOptionalBytes(resources.blockReadBytes)} read · ${formatOptionalBytes(resources.blockWriteBytes)} written`
              : 'Unavailable'}
          </div>
        </div>
      </td>
      <td className="min-w-32 px-3 py-2" data-testid={`slot-image-${slot.key}`}>
        <WorkerImageIdentity imageId={slot.imageId} />
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
    <section className="min-w-0 overflow-hidden rounded-lg border bg-card shadow-raised-surface">
      <div className="flex flex-wrap items-end justify-between gap-2 px-4 py-3">
        <div>
          <h2 className="font-semibold">Workers</h2>
          <p className="text-xs text-muted-foreground">
            {profile.slots.length} {profile.slots.length === 1 ? 'slot' : 'slots'} · cumulative I/O
            keeps unavailable measurements distinct from measured zero.
          </p>
        </div>
      </div>
      <div className="grid gap-3 border-t p-3 lg:hidden" data-testid="workers-mobile-summary">
        {profile.slots.map((slot) => (
          <div className="grid gap-2 rounded-lg border bg-card p-4" key={slot.key}>
            <div className="flex min-w-0 flex-wrap items-center gap-2">
              <span className="min-w-0 break-all font-mono text-xs">{slot.key}</span>
              <StatusBadge status={slot.state} />
              {slot.activity ? <StatusBadge status={slot.activity} /> : null}
            </div>
            <div className="break-words text-xs text-muted-foreground">
              {slot.repository ?? 'Shared scope'} · {slot.target ?? 'Target unavailable'}
            </div>
            <div className="text-xs tabular-nums">
              {slot.resources
                ? `${formatCpuCores(slot.resources.cpuCores)} · ${formatBytes(slot.resources.memoryWorkingSetBytes)} · ${formatPids(slot.resources.pids)}`
                : 'Worker resources unavailable'}
            </div>
            <WorkerExitEvidence lastExit={slot.lastExit} />
            <WorkerImageIdentity imageId={slot.imageId} />
          </div>
        ))}
      </div>
      <ScrollableRegion
        className="hidden max-h-[70vh] overflow-y-auto border-t lg:block"
        label={`Scrollable worker slots for profile ${profile.profileId}`}
      >
        <table
          aria-label={`Slots for profile ${profile.profileId}`}
          className="w-full min-w-[88rem] text-left text-sm"
        >
          <caption className="sr-only">Slots for profile {profile.profileId}</caption>
          <thead className="sticky top-0 z-10 bg-muted text-xs text-muted-foreground uppercase">
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
                Status
              </th>
              <th scope="col" className="px-3 py-2 text-right font-medium">
                Failures
              </th>
              <th scope="col" className="px-3 py-2 font-medium">
                Last exit
              </th>
              <th scope="col" className="px-3 py-2 text-right font-medium">
                Resources
              </th>
              <th scope="col" className="px-3 py-2 text-right font-medium">
                I/O
              </th>
              <th scope="col" className="px-3 py-2 font-medium">
                Worker image
              </th>
            </tr>
          </thead>
          <tbody>
            {profile.slots.map((slot) => (
              <SlotRow key={slot.key} slot={slot} />
            ))}
          </tbody>
        </table>
      </ScrollableRegion>
    </section>
  );
}
