import {
  capacityDeficitScopes,
  describeCapacityDeficit,
  type CapacityDeficitScope,
  type ManagerObservedState,
} from '@/core/fleet';
import { formatCounter, formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ProfileEvidencePanel } from './ProfileEvidencePanel';

interface DeficitCardProps {
  readonly scope: CapacityDeficitScope;
  readonly profileId: string;
}

function DeficitCard({ scope, profileId }: DeficitCardProps) {
  const summary = describeCapacityDeficit(scope.deficit);

  return (
    <div
      className="grid gap-2 rounded-md border bg-background px-3 py-3"
      data-testid={`profile-capacity-deficit-${profileId}-${scope.key}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0">
          <h3 className="font-mono text-xs">{scope.label}</h3>
          <p className="text-xs text-muted-foreground">{scope.repository ?? 'Shared scope'}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusBadge status={summary.status} />
          <span
            className="text-xs font-medium"
            data-testid={`profile-capacity-deficit-label-${profileId}-${scope.key}`}
          >
            {summary.label}
          </span>
        </div>
      </div>
      <dl className="flex flex-wrap gap-x-4 gap-y-1 text-xs tabular-nums">
        <div className="flex gap-1">
          <dt className="text-muted-foreground uppercase">Activation target</dt>
          <dd data-testid={`profile-capacity-deficit-target-${profileId}-${scope.key}`}>
            {scope.deficit.targetSlots}
          </dd>
        </div>
        <div className="flex gap-1">
          <dt className="text-muted-foreground uppercase">Active</dt>
          <dd>{scope.deficit.activeWorkers}</dd>
        </div>
        <div className="flex gap-1">
          <dt className="text-muted-foreground uppercase">Starting</dt>
          <dd>{scope.deficit.startingWorkers}</dd>
        </div>
        <div className="flex gap-1">
          <dt className="text-muted-foreground uppercase">Draining</dt>
          <dd>{scope.deficit.drainingWorkers}</dd>
        </div>
        <div className="flex gap-1">
          <dt className="text-muted-foreground uppercase">Cleanup pending</dt>
          <dd>{scope.deficit.cleanupPendingWorkers}</dd>
        </div>
        <div className="flex gap-1">
          <dt className="text-muted-foreground uppercase">GitHub eligible</dt>
          <dd data-testid={`profile-capacity-deficit-eligible-${profileId}-${scope.key}`}>
            {formatCounter(scope.deficit.eligibleWorkers)}
          </dd>
        </div>
        <div className="flex gap-1">
          <dt className="text-muted-foreground uppercase">Manager reason</dt>
          <dd data-testid={`profile-capacity-deficit-reason-${profileId}-${scope.key}`}>
            {scope.deficit.reason}
          </dd>
        </div>
        <div className="flex gap-1">
          <dt className="text-muted-foreground uppercase">Measured</dt>
          <dd>{formatTime(scope.deficit.observedAt)}</dd>
        </div>
      </dl>
      <p
        className="text-xs text-muted-foreground"
        data-testid={`profile-capacity-deficit-description-${profileId}-${scope.key}`}
      >
        {summary.description}
      </p>
    </div>
  );
}

/**
 * Renders manager-reported capacity-deficit evidence against the accepted activation target. A
 * configured autoscaling maximum is never rendered as a missing-capacity threshold.
 */
export function ProfileCapacityEvidence({ profile }: { readonly profile: ManagerObservedState }) {
  const scopes = capacityDeficitScopes(profile);
  if (profile.capacityEvidence === null) {
    return (
      <ProfileEvidencePanel
        title="Capacity evidence"
        description="Manager-reported shortfall against the accepted activation target."
        summary={<StatusBadge status="unavailable" />}
        testId={`profile-capacity-evidence-${profile.profileId}`}
      >
        <p className="text-sm text-muted-foreground">
          This manager does not report capacity-deficit evidence. A shortfall against the activation
          target is unavailable rather than zero, and the configured maximum is a ceiling rather
          than a missing-capacity threshold.
        </p>
      </ProfileEvidencePanel>
    );
  }

  const shortfallCount = scopes.filter((scope) => scope.deficit.localDeficit > 0).length;
  const unavailableCount = scopes.filter(
    (scope) => scope.deficit.freshness === 'unavailable',
  ).length;
  const staleCount = scopes.filter((scope) => scope.deficit.freshness === 'stale').length;

  return (
    <ProfileEvidencePanel
      title="Capacity evidence"
      description="Manager-reported shortfall against the accepted activation target, never against the configured maximum."
      summary={
        <>
          <span>
            {scopes.length} {scopes.length === 1 ? 'scope' : 'scopes'}
          </span>
          {shortfallCount > 0 ? (
            <span className="text-amber-700 dark:text-amber-300">
              {shortfallCount} reporting a shortfall
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
      testId={`profile-capacity-evidence-${profile.profileId}`}
    >
      {scopes.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          The manager reported no capacity scopes for this profile.
        </p>
      ) : (
        <div className="grid gap-3">
          {scopes.map((scope) => (
            <DeficitCard key={scope.key} scope={scope} profileId={profile.profileId} />
          ))}
        </div>
      )}
    </ProfileEvidencePanel>
  );
}
