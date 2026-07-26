import { describeResourcePolicy, type ManagerObservedState } from '@/core/fleet';
import { StatusBadge } from '@/core/ui/StatusBadge';

/** Renders the configured per-worker resource policy and profile admission ceiling. */
export function ProfileResourcePolicy({ profile }: { readonly profile: ManagerObservedState }) {
  const policy = profile.resourcePolicy ?? null;
  const admissionCeiling = profile.autoscaling?.maximumActiveWorkers ?? null;
  const limits = describeResourcePolicy(policy);

  return (
    <section
      className="grid gap-3 border-b bg-muted/10 px-4 py-4"
      data-testid={`profile-resource-policy-${profile.profileId}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">Worker resource policy</h2>
          <p className="text-xs text-muted-foreground">
            Limits applied to every worker this manager launches. A policy change converges as busy
            workers finish and are replaced.
          </p>
        </div>
        <StatusBadge status={policy === null ? 'unavailable' : 'configured'} />
      </div>
      <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-md border bg-border sm:grid-cols-5">
        {limits.map(([label, value]) => (
          <div key={label} className="bg-background px-3 py-3">
            <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
            <dd
              className="mt-1 font-medium tabular-nums"
              data-testid={`profile-policy-${label.toLowerCase().replaceAll(' ', '-')}-${profile.profileId}`}
            >
              {policy === null ? 'Unavailable' : value}
            </dd>
          </div>
        ))}
        <div className="bg-background px-3 py-3">
          <dt className="text-xs text-muted-foreground uppercase">Admission ceiling</dt>
          <dd
            className="mt-1 font-medium tabular-nums"
            data-testid={`profile-admission-ceiling-${profile.profileId}`}
          >
            {admissionCeiling == null ? 'Unavailable' : `${admissionCeiling} active workers`}
          </dd>
        </div>
      </dl>
      {policy === null ? (
        <p className="text-xs text-muted-foreground">
          This manager did not report a resource policy. Unreported limits are unknown rather than
          unlimited.
        </p>
      ) : null}
    </section>
  );
}
