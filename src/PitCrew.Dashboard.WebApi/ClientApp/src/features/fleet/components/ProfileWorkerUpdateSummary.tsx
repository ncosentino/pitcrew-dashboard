import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { describeWorkerUpdate, type ManagerObservedState } from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

function shortIdentity(value: string | null): string {
  if (value === null) return 'Unavailable';
  return value.startsWith('sha256:') ? value.slice(7, 19) : value.slice(0, 12);
}

/** Renders the target worker image and current/stale convergence evidence for one profile. */
export function ProfileWorkerUpdateSummary({
  profile,
}: {
  readonly profile: ManagerObservedState;
}) {
  const update = profile.update;
  const status = update?.status ?? 'unavailable';

  return (
    <Card data-testid={`profile-worker-update-${profile.profileId}`}>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle>Worker image rollout</CardTitle>
            <CardDescription>
              Target identity and safe convergence of live ephemeral workers.
            </CardDescription>
          </div>
          <StatusBadge status={status} />
        </div>
      </CardHeader>
      <CardContent className="grid gap-4">
        {update === null ? (
          <p className="text-sm text-muted-foreground">
            No rollout evidence was reported. This does not prove that workers are current.
          </p>
        ) : (
          <>
            <dl className="grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
              <div>
                <dt className="text-xs text-muted-foreground">Target image</dt>
                <dd className="mt-1 break-all font-mono text-xs" title={update.targetImage ?? ''}>
                  {update.targetImage ?? 'Unavailable'}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">Image identity</dt>
                <dd
                  className="mt-1 font-mono text-xs"
                  title={update.targetImageId ?? 'Unavailable'}
                >
                  {shortIdentity(update.targetImageId)}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">Worker revision</dt>
                <dd className="mt-1 font-mono text-xs" title={update.targetRevision}>
                  {shortIdentity(update.targetRevision)}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">Workers</dt>
                <dd className="mt-1 tabular-nums">
                  {update.currentWorkers} current · {update.staleWorkers} stale
                </dd>
              </div>
            </dl>
            <p className="text-xs text-muted-foreground">
              Observed {formatTime(profile.observedAt)}. {describeWorkerUpdate(profile)}
            </p>
            {update.lastError ? (
              <p
                className="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
                role="alert"
              >
                {update.lastError}
              </p>
            ) : null}
          </>
        )}
      </CardContent>
    </Card>
  );
}
