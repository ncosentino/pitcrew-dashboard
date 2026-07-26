import type { FleetNode, ManagerObservedState } from '@/core/fleet';

export type NodeStatus = 'online' | 'offline' | 'revoked';
export type NodeStatusFilter = 'all' | NodeStatus;
export type NodeSort = 'name' | 'status' | 'lastSeen';

export interface ResourceAggregate {
  readonly cpuCores: number;
  readonly memoryWorkingSetBytes: number;
  readonly reportingSources: number;
  readonly totalSources: number;
  readonly status: 'available' | 'partial' | 'unavailable';
}

export interface NodeAggregate {
  readonly configuredSlots: number;
  readonly activeSlots: number;
  readonly eligibleSlots: number | null;
  readonly resources: ResourceAggregate;
}

/** Returns the lifecycle state displayed for a node. */
export function getNodeStatus(node: FleetNode): NodeStatus {
  if (node.isRevoked) return 'revoked';
  return node.isOnline ? 'online' : 'offline';
}

/** Aggregates configured capacity and only resource samples that were actually reported. */
export function aggregateNode(node: FleetNode): NodeAggregate {
  const configuredSlots = node.profiles.reduce(
    (total, profile) =>
      total +
      (profile.configuredSlots ?? profile.autoscaling?.maximumSlots ?? profile.desiredSlots),
    0,
  );
  const activeSlots = node.profiles.reduce((total, profile) => total + profile.activeSlots, 0);
  const eligibleSlots = node.profiles.every((profile) => profile.eligibleSlots != null)
    ? node.profiles.reduce((total, profile) => total + (profile.eligibleSlots ?? 0), 0)
    : null;
  const profileResources = node.profiles.map(aggregateProfileResources);
  const resources = profileResources.reduce(
    (aggregate, profile) => ({
      cpuCores: aggregate.cpuCores + profile.cpuCores,
      memoryWorkingSetBytes: aggregate.memoryWorkingSetBytes + profile.memoryWorkingSetBytes,
      reportingSources: aggregate.reportingSources + profile.reportingSources,
      totalSources: aggregate.totalSources + profile.totalSources,
      status: aggregate.status,
    }),
    emptyResourceAggregate(),
  );

  return {
    configuredSlots,
    activeSlots,
    eligibleSlots,
    resources: {
      ...resources,
      status:
        resources.reportingSources === 0
          ? 'unavailable'
          : resources.reportingSources < resources.totalSources ||
              profileResources.some((profile) => profile.status !== 'available')
            ? 'partial'
            : 'available',
    },
  };
}

/** Aggregates one profile's reported manager and worker resource samples. */
export function aggregateProfileResources(profile: ManagerObservedState): ResourceAggregate {
  const usages = [
    profile.resourceTelemetry?.manager ?? null,
    ...profile.slots.map((slot) => slot.resources ?? null),
  ];
  const aggregate = usages.reduce(
    (current, usage) => ({
      cpuCores: current.cpuCores + (usage?.cpuCores ?? 0),
      memoryWorkingSetBytes: current.memoryWorkingSetBytes + (usage?.memoryWorkingSetBytes ?? 0),
      reportingSources: current.reportingSources + (usage ? 1 : 0),
      totalSources: current.totalSources,
      status: current.status,
    }),
    {
      ...emptyResourceAggregate(),
      totalSources: usages.length,
    },
  );
  return {
    ...aggregate,
    status:
      aggregate.reportingSources === 0
        ? 'unavailable'
        : aggregate.reportingSources < aggregate.totalSources ||
            (profile.resourceTelemetry?.status ?? 'unavailable') !== 'available'
          ? 'partial'
          : 'available',
  };
}

/** Filters and sorts nodes with stable node-ID tie breaking. */
export function selectNodes(
  nodes: ReadonlyArray<FleetNode>,
  status: NodeStatusFilter,
  query: string,
  sort: NodeSort,
): ReadonlyArray<FleetNode> {
  const normalizedQuery = query.trim().toLowerCase();
  return [...nodes]
    .filter((node) => status === 'all' || getNodeStatus(node) === status)
    .filter((node) => {
      if (!normalizedQuery) return true;
      return [
        node.displayName,
        node.nodeId,
        node.connectorVersion,
        ...node.profiles.map((p) => p.profileId),
      ]
        .join(' ')
        .toLowerCase()
        .includes(normalizedQuery);
    })
    .sort((left, right) => compareNodes(left, right, sort));
}

function emptyResourceAggregate(): ResourceAggregate {
  return {
    cpuCores: 0,
    memoryWorkingSetBytes: 0,
    reportingSources: 0,
    totalSources: 0,
    status: 'unavailable',
  };
}

function compareText(left: string, right: string): number {
  const normalizedLeft = left.toLowerCase();
  const normalizedRight = right.toLowerCase();
  if (normalizedLeft < normalizedRight) return -1;
  if (normalizedLeft > normalizedRight) return 1;
  return 0;
}

function compareNodes(left: FleetNode, right: FleetNode, sort: NodeSort): number {
  let comparison: number;
  if (sort === 'name') {
    comparison = compareText(left.displayName, right.displayName);
  } else if (sort === 'status') {
    const statusOrder: Readonly<Record<NodeStatus, number>> = {
      online: 0,
      offline: 1,
      revoked: 2,
    };
    comparison = statusOrder[getNodeStatus(left)] - statusOrder[getNodeStatus(right)];
  } else {
    comparison =
      (right.lastSeenAt ? Date.parse(right.lastSeenAt) : Number.NEGATIVE_INFINITY) -
      (left.lastSeenAt ? Date.parse(left.lastSeenAt) : Number.NEGATIVE_INFINITY);
  }

  return (
    comparison ||
    compareText(left.displayName, right.displayName) ||
    compareText(left.nodeId, right.nodeId)
  );
}
