import { Link } from 'react-router-dom';

import type { ManagerObservedState } from '@/core/fleet';
import { formatBytes, formatCpuCores, formatTime } from '@/core/formatting/formatters';
import { ScrollableRegion } from '@/core/ui/ScrollableRegion';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { aggregateProfileResources } from '../nodeSummary';

interface ProfileComparisonTableProps {
  readonly profiles: ReadonlyArray<ManagerObservedState>;
  readonly tenantId: string;
  readonly nodeId: string;
  readonly nodeIsOnline: boolean;
  readonly formatProfileName: (profileId: string) => string;
}

/** Aligns repeated profile evidence for fast cross-profile comparison. */
export function ProfileComparisonTable({
  profiles,
  tenantId,
  nodeId,
  nodeIsOnline,
  formatProfileName,
}: ProfileComparisonTableProps) {
  return (
    <ScrollableRegion
      className="rounded-lg border bg-card"
      label="Profile capacity and health comparison"
    >
      <table
        className="w-full min-w-[72rem] text-left text-sm"
        data-testid="node-profile-comparison-table"
      >
        <caption className="sr-only">
          {nodeIsOnline ? 'Current' : 'Last-known'} profile capacity and health comparison
        </caption>
        <thead className="bg-muted/50 text-xs text-muted-foreground uppercase">
          <tr>
            <th scope="col" className="px-4 py-3 font-medium">
              Profile
            </th>
            <th scope="col" className="px-4 py-3 font-medium">
              State
            </th>
            <th scope="col" className="px-4 py-3 text-right font-medium">
              Configured
            </th>
            <th scope="col" className="px-4 py-3 text-right font-medium">
              Desired
            </th>
            <th scope="col" className="px-4 py-3 text-right font-medium">
              Local
            </th>
            <th scope="col" className="px-4 py-3 text-right font-medium">
              Eligible
            </th>
            <th scope="col" className="px-4 py-3 text-right font-medium">
              Draining
            </th>
            <th scope="col" className="px-4 py-3 font-medium">
              Resources
            </th>
            <th scope="col" className="px-4 py-3 font-medium">
              Evidence
            </th>
          </tr>
        </thead>
        <tbody>
          {profiles.map((profile) => {
            const configured =
              profile.configuredSlots ?? profile.autoscaling?.maximumSlots ?? profile.desiredSlots;
            const resources = aggregateProfileResources(profile);
            return (
              <tr
                className="border-t align-top"
                data-testid={`node-profile-table-${profile.profileId}`}
                key={profile.profileId}
              >
                <th scope="row" className="px-4 py-3 font-normal">
                  <Link
                    className="font-semibold text-link underline-offset-4 hover:underline"
                    to={`/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(nodeId)}/profiles/${encodeURIComponent(profile.profileId)}`}
                  >
                    {formatProfileName(profile.profileId)}
                  </Link>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">
                    {profile.profileId}
                  </div>
                  <div className="mt-1 text-xs text-muted-foreground">{profile.scope} scope</div>
                </th>
                <td className="px-4 py-3">
                  <div className="flex max-w-48 flex-wrap gap-1.5">
                    <StatusBadge status={profile.managerStatus} />
                    <StatusBadge status={profile.desiredStateStatus} />
                    {profile.autoscaling ? (
                      <StatusBadge status={profile.autoscaling.status} />
                    ) : null}
                  </div>
                  {profile.autoscaling?.lastError ? (
                    <div className="mt-2 text-xs text-destructive">Autoscaling error reported</div>
                  ) : null}
                </td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">{configured}</td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">
                  {profile.desiredSlots}
                </td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">
                  {profile.activeSlots}
                </td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">
                  {profile.eligibleSlots ?? 'Unknown'}
                </td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">
                  {profile.drainingSlots}
                </td>
                <td className="px-4 py-3 whitespace-nowrap">
                  <div className="font-medium">
                    {resources.reportingSources > 0
                      ? `${formatCpuCores(resources.cpuCores)} / ${formatBytes(resources.memoryWorkingSetBytes)}`
                      : 'Unavailable'}
                  </div>
                  <div className="mt-1 flex items-center gap-2 text-xs text-muted-foreground">
                    <span>
                      {resources.reportingSources} of {resources.totalSources} sources
                    </span>
                    <StatusBadge status={resources.status} />
                  </div>
                </td>
                <td className="px-4 py-3 text-xs whitespace-nowrap">
                  <div>Generation {profile.generation}</div>
                  <div className="mt-1 text-muted-foreground">
                    {nodeIsOnline ? 'Observed' : 'Last known'} {formatTime(profile.observedAt)}
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </ScrollableRegion>
  );
}
