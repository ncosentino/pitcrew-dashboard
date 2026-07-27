export { FleetProvider } from './FleetProvider';
export { useFleet } from './useFleet';
export {
  describeExitEvidence,
  describeResourcePolicy,
  describeTargetDivergence,
  staleStatisticsSeconds,
  statisticsFreshness,
} from './workerEvidence';
export type { ExitEvidenceSummary, StatisticsFreshness } from './workerEvidence';
export type {
  AutoscalingTarget,
  CapacityControlState,
  FleetNode,
  FleetResponse,
  ManagerObservedState,
  ObservedSlot,
  ScaleSetStatistics,
  WorkerLastExit,
  WorkerResourcePolicy,
} from './fleetApi';
