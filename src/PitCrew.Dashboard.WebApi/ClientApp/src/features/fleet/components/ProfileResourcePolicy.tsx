import { describeResourcePolicy, type ManagerObservedState } from '@/core/fleet';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ProfileEvidenceDisclosure } from './ProfileEvidenceDisclosure';

/** Renders the configured per-worker resource policy and profile admission ceiling. */
export function ProfileResourcePolicy({ profile }: { readonly profile: ManagerObservedState }) {
  const policy = profile.resourcePolicy ?? null;
  const admissionCeiling = profile.autoscaling?.maximumActiveWorkers ?? null;
  const limits = describeResourcePolicy(policy);

  return (
    <ProfileEvidenceDisclosure
      title="Worker resource policy"
      description={
        policy === null
          ? 'No worker limits were reported. Unreported limits remain unknown.'
          : 'Configured limits apply to newly launched workers and converge through normal turnover.'
      }
      summary={
        <>
          <StatusBadge status={policy === null ? 'unavailable' : 'configured'} />
          <span>
            {admissionCeiling == null ? 'Admission unavailable' : `${admissionCeiling} max workers`}
          </span>
        </>
      }
      testId={`profile-resource-policy-${profile.profileId}`}
    >
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
    </ProfileEvidenceDisclosure>
  );
}
