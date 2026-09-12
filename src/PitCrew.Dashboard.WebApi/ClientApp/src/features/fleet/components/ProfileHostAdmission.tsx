import {
  describeHostAdmission,
  describeHostAdmissionWithholding,
  type ManagerObservedState,
} from '@/core/fleet';
import { formatCounter } from '@/core/formatting/formatters';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { cn } from '@/lib/utils';

import { ProfileEvidencePanel } from './ProfileEvidencePanel';

interface AdmissionMetricProps {
  readonly label: string;
  readonly value: number | null;
  readonly testId: string;
  readonly className?: string;
}

function AdmissionMetric({ label, value, testId, className }: AdmissionMetricProps) {
  return (
    <div className={cn('min-w-0 bg-background px-3 py-3', className)}>
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 text-lg font-semibold tabular-nums" data-testid={testId}>
        {formatCounter(value)}
      </dd>
    </div>
  );
}

function formatWorkers(value: number | null): string {
  if (value === null) return 'Unavailable';
  return `${formatCounter(value)} ${value === 1 ? 'worker' : 'workers'}`;
}

function formatUnits(value: number | null): string {
  if (value === null) return 'Unavailable';
  return `${formatCounter(value)} ${value === 1 ? 'unit' : 'units'}`;
}

/** Renders profile-usable capacity separately from shared host budget and policy ceilings. */
export function ProfileHostAdmission({ profile }: { readonly profile: ManagerObservedState }) {
  const admission = profile.hostAdmission;
  const summary = describeHostAdmission(admission);
  const accounting = admission?.accounting ?? null;
  const withholding = describeHostAdmissionWithholding(accounting?.withholdingReason ?? null);
  const profileCapacityReported =
    accounting?.allocatableUnits != null &&
    accounting.allocatableWorkers != null &&
    accounting.theoreticalMaximumUnits != null &&
    accounting.theoreticalMaximumWorkers != null;
  const configuredMaximum = profile.autoscaling?.maximumActiveWorkers ?? null;

  return (
    <ProfileEvidencePanel
      title="Host admission"
      description="Profile-usable capacity, shared host budget, and policy ceilings answer different questions and are not interchangeable."
      summary={
        <>
          <StatusBadge
            status={withholding ? 'withheld' : summary.status}
            tone={withholding ? 'caution' : undefined}
          />
          <span>
            {withholding
              ? 'Profile admission withheld'
              : summary.status === 'available'
                ? 'Coordinator current'
                : summary.status === 'disabled'
                  ? 'Independent admission'
                  : summary.status === 'degraded'
                    ? 'Accounting incomplete'
                    : 'Evidence missing'}
          </span>
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
            <p className="min-w-0 break-words">
              Configured namespace <span className="font-mono">{admission.namespace}</span>
            </p>
          ) : null}
        </div>
      ) : (
        <div className="grid gap-4">
          <p className="max-w-3xl text-sm text-muted-foreground">{summary.description}</p>

          <div className="grid gap-px overflow-hidden rounded-lg border bg-border lg:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
            <section
              className="min-w-0 bg-accent/55 px-4 py-4"
              aria-labelledby="profile-usable-heading"
            >
              <h3 className="text-sm font-semibold" id="profile-usable-heading">
                Profile usable now
              </h3>
              <p
                className="mt-2 text-2xl font-semibold tracking-tight tabular-nums"
                data-testid={`profile-host-admission-allocatable-workers-${profile.profileId}`}
              >
                {formatWorkers(accounting?.allocatableWorkers ?? null)}
              </p>
              <p
                className="mt-1 text-sm font-medium tabular-nums"
                data-testid={`profile-host-admission-allocatable-units-${profile.profileId}`}
              >
                {formatUnits(accounting?.allocatableUnits ?? null)}
              </p>
              <p className="mt-2 max-w-prose text-xs text-muted-foreground">
                {profileCapacityReported
                  ? `Current admission for this profile at ${formatCounter(accounting?.unitCost)} units per worker.`
                  : 'Profile-scoped capacity was not reported. Contract 18 and degraded compatibility evidence remain unavailable rather than zero.'}
              </p>
            </section>

            <div className="grid min-w-0 gap-px bg-border sm:grid-cols-2 lg:grid-cols-1">
              <section
                className="min-w-0 bg-background px-4 py-3"
                aria-labelledby="host-pool-heading"
              >
                <h3
                  className="text-xs font-semibold text-muted-foreground uppercase"
                  id="host-pool-heading"
                >
                  Shared host pool now
                </h3>
                <p
                  className="mt-1 text-lg font-semibold tabular-nums"
                  data-testid={`profile-host-admission-available-${profile.profileId}`}
                >
                  {formatUnits(admission.availableUnits)}
                </p>
                <p className="mt-1 text-xs text-muted-foreground">
                  {formatUnits(admission.effectiveTotalUnits)} effective host budget. Available host
                  units may be protected or shared and are not automatically usable by this profile.
                </p>
              </section>

              <section
                className="min-w-0 bg-background px-4 py-3"
                aria-labelledby="admission-limits-heading"
              >
                <h3
                  className="text-xs font-semibold text-muted-foreground uppercase"
                  id="admission-limits-heading"
                >
                  Upper bounds
                </h3>
                <dl className="mt-2 grid gap-3 sm:grid-cols-2 lg:grid-cols-2">
                  <div className="min-w-0">
                    <dt className="text-xs text-muted-foreground">Theoretical profile maximum</dt>
                    <dd
                      className="mt-0.5 font-semibold tabular-nums"
                      data-testid={`profile-host-admission-theoretical-workers-${profile.profileId}`}
                    >
                      {formatWorkers(accounting?.theoreticalMaximumWorkers ?? null)}
                    </dd>
                    <dd
                      className="text-xs text-muted-foreground tabular-nums"
                      data-testid={`profile-host-admission-theoretical-units-${profile.profileId}`}
                    >
                      {formatUnits(accounting?.theoreticalMaximumUnits ?? null)}
                    </dd>
                  </div>
                  <div className="min-w-0">
                    <dt className="text-xs text-muted-foreground">Configured active-worker cap</dt>
                    <dd
                      className="mt-0.5 font-semibold tabular-nums"
                      data-testid={`profile-host-admission-configured-maximum-${profile.profileId}`}
                    >
                      {formatWorkers(configuredMaximum)}
                    </dd>
                    <dd className="text-xs text-muted-foreground">
                      Manager setting, not guaranteed demand or physical host capacity.
                    </dd>
                  </div>
                </dl>
              </section>
            </div>
          </div>

          {withholding ? (
            <StateBanner
              data-testid={`profile-host-admission-withholding-${profile.profileId}`}
              tone="caution"
            >
              <p className="font-semibold">{withholding.label}</p>
              <p className="mt-1 opacity-85">{withholding.description}</p>
              <p className="mt-1 opacity-85">
                Host-wide availability can remain positive because shared host units are not the
                same as capacity allocatable to this profile.
              </p>
            </StateBanner>
          ) : null}

          <section className="grid gap-2" aria-labelledby="admission-ledger-heading">
            <div>
              <h3 className="text-sm font-semibold" id="admission-ledger-heading">
                Allocation ledger
              </h3>
              <p className="text-xs text-muted-foreground">
                Current reservation use and demand accounting for this profile.
              </p>
            </div>
            <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-md border bg-border text-center sm:grid-cols-5">
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
                label="Pending"
                value={accounting?.pendingUnits ?? null}
                testId={`profile-host-admission-pending-${profile.profileId}`}
              />
              <AdmissionMetric
                className="col-span-2 sm:col-span-1"
                label="Withheld"
                value={accounting?.withheldUnits ?? null}
                testId={`profile-host-admission-withheld-${profile.profileId}`}
              />
            </dl>
          </section>

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
            <div className="min-w-0">
              <dt className="text-xs text-muted-foreground uppercase">Namespace</dt>
              <dd className="mt-1 break-words font-mono text-xs">
                {admission.namespace ?? 'Unavailable'}
              </dd>
            </div>
          </dl>

          <div className="border-t pt-3 text-xs text-muted-foreground">
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
