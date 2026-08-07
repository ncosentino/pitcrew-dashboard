import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import type { FleetNode, HostHardwareInventory } from '@/core/fleet';
import { formatBytes, formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

interface HostHardwareCardProps {
  readonly hardware: HostHardwareInventory | null;
  readonly isOnline: boolean;
  readonly lastSeenAt: string | null;
}

/** Renders the latest bounded hardware inventory for one node. */
export function HostHardwareCard({ hardware, isOnline, lastSeenAt }: HostHardwareCardProps) {
  return (
    <Card data-testid="node-hardware">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <CardTitle>Host hardware</CardTitle>
            <CardDescription>
              Sanitized processor, memory, operating-system, and Docker runtime context.
            </CardDescription>
          </div>
          <StatusBadge status={hardwarePresentationStatus(hardware, isOnline)} />
        </div>
      </CardHeader>
      <CardContent className="grid gap-3">
        {hardware == null ? (
          <p className="text-sm text-muted-foreground">
            This connector has not reported manager contract 13 hardware inventory.
          </p>
        ) : (
          <>
            <dl className="grid gap-3 text-sm sm:grid-cols-3">
              <HardwareField label="Processor" value={hardware.processorModel} />
              <HardwareField label="Architecture" value={hardware.architecture} />
              <HardwareField
                label="Physical / logical cores"
                value={formatPair(hardware.physicalCoreCount, hardware.logicalProcessorCount)}
              />
              <HardwareField
                label="Performance / efficiency cores"
                value={formatPair(hardware.performanceCoreCount, hardware.efficiencyCoreCount)}
              />
              <HardwareField
                label="Docker-visible memory"
                value={hardware.memoryBytes == null ? null : formatBytes(hardware.memoryBytes)}
              />
              <HardwareField label="Operating system" value={hardware.operatingSystem} />
              <HardwareField label="Kernel" value={hardware.kernelVersion} />
              <HardwareField label="Docker server" value={hardware.dockerServerVersion} />
              <HardwareField
                label="Storage"
                value={
                  [hardware.dockerStorageDriver, hardware.dockerBackingFilesystem]
                    .filter((value) => value != null)
                    .join(' / ') || null
                }
              />
            </dl>
            <div className="grid gap-1 text-xs text-muted-foreground">
              {!isOnline ? <div>Last-known node evidence from {formatTime(lastSeenAt)}</div> : null}
              <div>Collected {formatTime(hardware.collectedAt)}</div>
              <div>Last attempted {formatTime(hardware.attemptedAt)}</div>
              <div className="font-mono">
                Inventory {hardware.inventoryHash?.slice(0, 16) ?? 'unavailable'}
              </div>
            </div>
            {hardware.status === 'stale' ? (
              <p className="text-sm text-amber-800 dark:text-amber-200">
                The latest collection attempt failed. Values shown are the last valid inventory.
              </p>
            ) : null}
          </>
        )}
      </CardContent>
    </Card>
  );
}

interface HardwareComparisonProps {
  readonly nodes: readonly FleetNode[];
}

/** Compares the reported hardware context of operator-selected nodes. */
export function HardwareComparison({ nodes }: HardwareComparisonProps) {
  if (nodes.length === 0) return null;
  return (
    <section className="overflow-x-auto rounded-lg border bg-card">
      <table className="w-full min-w-4xl text-left text-sm" data-testid="hardware-comparison">
        <caption className="p-3 text-left text-sm font-semibold">
          Hardware comparison for selected nodes
        </caption>
        <thead className="bg-muted/50 text-xs text-muted-foreground uppercase">
          <tr>
            <th scope="col" className="px-3 py-2">
              Node
            </th>
            <th scope="col" className="px-3 py-2">
              Processor
            </th>
            <th scope="col" className="px-3 py-2">
              Architecture
            </th>
            <th scope="col" className="px-3 py-2">
              Physical / logical
            </th>
            <th scope="col" className="px-3 py-2">
              Memory
            </th>
            <th scope="col" className="px-3 py-2">
              Docker storage
            </th>
            <th scope="col" className="px-3 py-2">
              Freshness
            </th>
          </tr>
        </thead>
        <tbody>
          {nodes.map((node) => (
            <tr className="border-t" key={node.nodeId}>
              <th className="px-3 py-2 font-medium" scope="row">
                {node.displayName}
              </th>
              <td className="px-3 py-2">{node.hardware?.processorModel ?? 'Unavailable'}</td>
              <td className="px-3 py-2">{node.hardware?.architecture ?? 'Unavailable'}</td>
              <td className="px-3 py-2">
                {formatPair(
                  node.hardware?.physicalCoreCount ?? null,
                  node.hardware?.logicalProcessorCount ?? null,
                )}
              </td>
              <td className="px-3 py-2">
                {node.hardware?.memoryBytes == null
                  ? 'Unavailable'
                  : formatBytes(node.hardware.memoryBytes)}
              </td>
              <td className="px-3 py-2">
                {[node.hardware?.dockerStorageDriver, node.hardware?.dockerBackingFilesystem]
                  .filter((value) => value != null)
                  .join(' / ') || 'Unavailable'}
              </td>
              <td className="px-3 py-2">
                <div className="grid gap-1">
                  <StatusBadge
                    status={hardwarePresentationStatus(node.hardware ?? null, node.isOnline)}
                  />
                  {!node.isOnline ? (
                    <span className="text-xs text-muted-foreground">
                      {formatTime(node.lastSeenAt)}
                    </span>
                  ) : null}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

function HardwareField({
  label,
  value,
}: {
  readonly label: string;
  readonly value: string | null;
}) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 font-medium">{value ?? 'Unavailable'}</dd>
    </div>
  );
}

function formatPair(left: number | null, right: number | null): string {
  if (left == null && right == null) return 'Unavailable';
  return `${left ?? 'Unknown'} / ${right ?? 'Unknown'}`;
}

function hardwarePresentationStatus(
  hardware: HostHardwareInventory | null,
  isOnline: boolean,
): string {
  if (hardware == null) return 'unreported';
  if (!isOnline) return 'last known';
  return hardware.status === 'current' ? 'latest reported' : hardware.status;
}
