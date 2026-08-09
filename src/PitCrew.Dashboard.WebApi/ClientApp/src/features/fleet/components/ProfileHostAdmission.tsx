import { describeHostAdmission, type ManagerObservedState } from '@/core/fleet';
import { formatCounter } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ProfileEvidencePanel } from './ProfileEvidencePanel';

interface AdmissionMetricProps {
  readonly label: string;
  readonly value: number | null;
  readonly testId: string;
}

function AdmissionMetric({ label, value, testId }: AdmissionMetricProps) {
  return (
    <div className="min-w-0 bg-background px-3 py-3">
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 text-lg font-semibold tabular-nums" data-testid={testId}>
        {formatCounter(value)}
      </dd>
    </div>
  );
}

/** Renders profile-scoped host budget, reservation, borrowing, and withheld-demand evidence. */
export function ProfileHostAdmission({ profile }: { readonly profile: ManagerObservedState }) {
  const admission = profile.hostAdmission;
  const summary = describeHostAdmission(admission);
  const accounting = admission?.accounting ?? null;

  return (
    <ProfileEvidencePanel
      title="Host admission"
      description="Host-local budget and reservation evidence, separate from the profile ceiling and GitHub demand."
      summary={
        <>
          <StatusBadge status={summary.status} />
          <span>{summary.label}</span>
        </>
      }
      testId={`profile-host-admission-${profile.profileId}`}
    >
      {admission == null ||
      admission.status === 'disabled' ||
      admission.status === 'unavailable' ? (
        <div className="grid gap-2 text-sm text-muted-foreground">
          <p>{summary.description}</p>
          {admission?.namespace ? (
            <p>
              Configured namespace <span className="font-mono">{admission.namespace}</span>
            </p>
          ) : null}
        </div>
      ) : (
        <div className="grid gap-4">
          <p className="text-sm text-muted-foreground">{summary.description}</p>
          <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-md border bg-border text-center sm:grid-cols-3 xl:grid-cols-6">
            <AdmissionMetric
              label="Host budget"
              value={admission.effectiveTotalUnits}
              testId={`profile-host-admission-budget-${profile.profileId}`}
            />
            <AdmissionMetric
              label="Available"
              value={admission.availableUnits}
              testId={`profile-host-admission-available-${profile.profileId}`}
            />
            <AdmissionMetric
              label="Held"
              value={accounting?.heldUnits ?? null}
              testId={`profile-host-admission-held-${profile.profileId}`}
            />
            <AdmissionMetric
              label="Reserved"
              value={accounting?.reservedUnits ?? null}
              testId={`profile-host-admission-reserved-${profile.profileId}`}
            />
            <AdmissionMetric
              label="Borrowed"
              value={accounting?.borrowedUnits ?? null}
              testId={`profile-host-admission-borrowed-${profile.profileId}`}
            />
            <AdmissionMetric
              label="Withheld"
              value={accounting?.withheldUnits ?? null}
              testId={`profile-host-admission-withheld-${profile.profileId}`}
            />
          </dl>

          <dl className="grid gap-3 text-sm sm:grid-cols-2 xl:grid-cols-4">
            <div>
              <dt className="text-xs text-muted-foreground uppercase">Reservation policy</dt>
              <dd className="mt-1 font-medium">
                {accounting == null
                  ? 'Unavailable'
                  : accounting.borrowable
                    ? 'Borrowable reservation'
                    : 'Protected reservation'}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground uppercase">Worker cost</dt>
              <dd className="mt-1 font-medium tabular-nums">
                {formatCounter(accounting?.unitCost)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground uppercase">Capacity / safety margin</dt>
              <dd className="mt-1 font-medium tabular-nums">
                {formatCounter(admission.capacityUnits)} /{' '}
                {formatCounter(admission.safetyMarginUnits)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground uppercase">
                Coordinator epoch / sequence
              </dt>
              <dd className="mt-1 font-medium tabular-nums">
                {formatCounter(admission.epoch)} / {formatCounter(admission.decisionSequence)}
              </dd>
            </div>
          </dl>

          <div className="flex min-w-0 flex-wrap items-center gap-x-4 gap-y-2 border-t pt-3 text-xs text-muted-foreground">
            <span className="min-w-0 break-words">
              Namespace <span className="font-mono">{admission.namespace ?? 'Unavailable'}</span>
            </span>
            <span>
              Pending units{' '}
              <span className="font-medium">{formatCounter(accounting?.pendingUnits)}</span>
            </span>
            {admission.lastDecision ? (
              <span data-testid={`profile-host-admission-decision-${profile.profileId}`}>
                Last decision #{admission.lastDecision.sequence}:{' '}
                {admission.lastDecision.command.replaceAll('-', ' ')} ·{' '}
                {admission.lastDecision.granted
                  ? 'granted'
                  : (admission.lastDecision.failureCategory ?? 'rejected')}
              </span>
            ) : (
              <span>No admission decision reported.</span>
            )}
          </div>
        </div>
      )}
    </ProfileEvidencePanel>
  );
}
