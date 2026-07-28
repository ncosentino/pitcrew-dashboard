import {
  describeTargetDivergence,
  statisticsFreshness,
  type AutoscalingTarget,
  type ManagerObservedState,
  type StatisticsFreshness,
} from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ProfileEvidencePanel } from './ProfileEvidencePanel';

const freshnessStatus: Record<StatisticsFreshness, string> = {
  current: 'available',
  stale: 'partial',
  unavailable: 'unavailable',
};

const freshnessLabel: Record<StatisticsFreshness, string> = {
  current: 'Current',
  stale: 'Stale',
  unavailable: 'Unavailable',
};

interface TargetRowProps {
  readonly target: AutoscalingTarget;
  readonly profileId: string;
  readonly observedAt: string;
}

function TargetRow({ target, profileId, observedAt }: TargetRowProps) {
  const freshness = statisticsFreshness(target, observedAt);
  const statistics = target.statistics;
  const divergence = describeTargetDivergence(target, freshness);

  return (
    <>
      <tr className="border-t" data-testid={`target-row-${profileId}-${target.key}`}>
        <td className="px-3 py-2">
          <div className="font-mono text-xs">{target.key}</div>
          <div className="text-xs text-muted-foreground">{target.repository ?? 'Shared scope'}</div>
        </td>
        <td className="px-3 py-2 text-right tabular-nums">
          {target.targetSlots} / {target.maximumSlots}
        </td>
        <td
          className="px-3 py-2 text-right tabular-nums"
          data-testid={`target-local-${profileId}-${target.key}`}
        >
          {target.localActiveWorkers} live · {target.localIdleWorkers} idle ·{' '}
          {target.localBusyWorkers} busy · {target.localDrainingWorkers} draining
        </td>
        <td
          className="px-3 py-2 text-right tabular-nums"
          data-testid={`target-github-${profileId}-${target.key}`}
        >
          {statistics === null
            ? 'Unavailable'
            : `${statistics.registeredRunners} registered · ${statistics.busyRunners} busy · ${statistics.idleRunners} idle`}
        </td>
        <td
          className="px-3 py-2 text-right tabular-nums"
          data-testid={`target-jobs-${profileId}-${target.key}`}
        >
          {statistics === null
            ? 'Unavailable'
            : `${statistics.availableJobs} available · ${statistics.acquiredJobs} acquired · ${statistics.assignedJobs} assigned · ${statistics.runningJobs} running`}
        </td>
        <td className="px-3 py-2" data-testid={`target-freshness-${profileId}-${target.key}`}>
          <StatusBadge status={freshnessStatus[freshness]} />
          <div className="text-xs text-muted-foreground">
            {freshnessLabel[freshness]}
            {statistics === null ? '' : ` · ${formatTime(statistics.observedAt)}`}
          </div>
        </td>
      </tr>
      {divergence ? (
        <tr className="border-t bg-amber-50 dark:bg-amber-950">
          <td
            className="px-3 py-2 text-xs text-amber-900 dark:text-amber-100"
            colSpan={6}
            data-testid={`target-divergence-${profileId}-${target.key}`}
          >
            {divergence}
          </td>
        </tr>
      ) : null}
    </>
  );
}

/** Renders per-target activation with separately labelled local and GitHub evidence. */
export function ProfileTargetsTable({ profile }: { readonly profile: ManagerObservedState }) {
  const targets = profile.autoscaling?.targets ?? null;
  if (targets === null) {
    return (
      <ProfileEvidencePanel
        title="Scale-set targets"
        description="Per-target local and GitHub evidence is unavailable."
        summary={<StatusBadge status="unavailable" />}
        testId={`profile-targets-${profile.profileId}`}
      >
        <p className="mt-1 text-sm text-muted-foreground">
          This manager does not report per-target scale-set evidence. Per-target local and GitHub
          counts are unavailable rather than zero.
        </p>
      </ProfileEvidencePanel>
    );
  }

  const divergenceCount = targets.filter((target) => {
    const freshness = statisticsFreshness(target, profile.observedAt);
    return describeTargetDivergence(target, freshness) !== null;
  }).length;
  const staleCount = targets.filter(
    (target) => statisticsFreshness(target, profile.observedAt) === 'stale',
  ).length;
  const unavailableCount = targets.filter(
    (target) => statisticsFreshness(target, profile.observedAt) === 'unavailable',
  ).length;

  return (
    <ProfileEvidencePanel
      title="Scale-set targets"
      description="Local Docker workers and GitHub statistics remain separate evidence."
      summary={
        <>
          <span>
            {targets.length} {targets.length === 1 ? 'target' : 'targets'}
          </span>
          {divergenceCount > 0 ? (
            <span className="text-amber-700 dark:text-amber-300">
              {divergenceCount} {divergenceCount === 1 ? 'warning' : 'warnings'}
            </span>
          ) : null}
          {staleCount > 0 ? (
            <span className="text-amber-700 dark:text-amber-300">{staleCount} stale</span>
          ) : null}
          {unavailableCount > 0 ? (
            <span className="text-amber-700 dark:text-amber-300">
              {unavailableCount} unavailable
            </span>
          ) : null}
        </>
      }
      testId={`profile-targets-${profile.profileId}`}
    >
      <div className="overflow-x-auto">
        <table className="w-full min-w-4xl text-left text-sm">
          <caption className="sr-only">
            Scale-set targets for profile {profile.profileId}. Local Docker worker counts and GitHub
            statistics are separate evidence and are never combined.
          </caption>
          <thead className="bg-muted/30 text-xs text-muted-foreground uppercase">
            <tr>
              <th scope="col" className="px-3 py-2 font-medium">
                Target
              </th>
              <th scope="col" className="px-3 py-2 text-right font-medium">
                Activation / maximum
              </th>
              <th scope="col" className="px-3 py-2 text-right font-medium">
                Local Docker workers
              </th>
              <th scope="col" className="px-3 py-2 text-right font-medium">
                GitHub runners
              </th>
              <th scope="col" className="px-3 py-2 text-right font-medium">
                GitHub jobs
              </th>
              <th scope="col" className="px-3 py-2 font-medium">
                GitHub statistics freshness
              </th>
            </tr>
          </thead>
          <tbody>
            {targets.map((target) => (
              <TargetRow
                key={target.key}
                target={target}
                profileId={profile.profileId}
                observedAt={profile.observedAt}
              />
            ))}
          </tbody>
        </table>
      </div>
      {targets.length === 0 ? (
        <p className="pt-3 text-sm text-muted-foreground">
          The manager reported no scale-set targets for this profile.
        </p>
      ) : null}
    </ProfileEvidencePanel>
  );
}
