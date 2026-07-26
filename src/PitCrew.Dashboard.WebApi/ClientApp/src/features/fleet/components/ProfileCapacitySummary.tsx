import { useState, type ReactNode } from 'react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { type CapacityControlState, type ManagerObservedState } from '@/core/fleet';
import { formatSeconds, formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { cn } from '@/lib/utils';

function formatScaleDownCountdown(value: string | null): string {
  if (value === null) return 'Not scheduled';
  const secondsRemaining = Math.max(0, Math.ceil((new Date(value).getTime() - Date.now()) / 1_000));
  const schedule = formatTime(value);
  return secondsRemaining === 0
    ? `Due now · ${schedule}`
    : `${formatSeconds(secondsRemaining)} remaining · ${schedule}`;
}

interface CapacityMetricProps {
  readonly label: string;
  readonly value: ReactNode;
  readonly testId: string;
}

function CapacityMetric({ label, value, testId }: CapacityMetricProps) {
  return (
    <div className="bg-background px-3 py-3">
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 text-2xl font-semibold tabular-nums" data-testid={testId}>
        {value}
      </dd>
    </div>
  );
}

interface CapacityMaximumControlProps {
  readonly control: CapacityControlState;
  readonly disabled: boolean;
  readonly onSetMaximum: (maximum: number) => Promise<void>;
}

function CapacityMaximumControl({ control, disabled, onSetMaximum }: CapacityMaximumControlProps) {
  const [draft, setDraft] = useState(String(control.currentMaximum));
  const parsed = Number(draft);
  const active =
    control.latestCommand?.status === 'pending' || control.latestCommand?.status === 'delivered';
  const valid =
    Number.isInteger(parsed) &&
    parsed >= 1 &&
    parsed <= control.maximumAllowed &&
    parsed !== control.currentMaximum;

  return (
    <div
      className="grid gap-2 rounded-md border bg-background px-3 py-3"
      data-testid={`profile-capacity-control-${control.profileId}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <div className="text-sm font-medium">Capacity maximum</div>
          <div className="text-xs text-muted-foreground">
            Local ceiling {control.maximumAllowed} · generation {control.generation}
          </div>
        </div>
        {control.latestCommand ? <StatusBadge status={control.latestCommand.status} /> : null}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <label className="text-xs font-medium" htmlFor={`capacity-maximum-${control.profileId}`}>
          Absolute maximum
        </label>
        <input
          id={`capacity-maximum-${control.profileId}`}
          className="h-9 w-28 rounded-md border bg-background px-3 text-sm tabular-nums"
          type="number"
          min={1}
          max={control.maximumAllowed}
          value={draft}
          disabled={disabled || active}
          onChange={(event) => setDraft(event.target.value)}
        />
        <ConfirmActionDialog
          title="Queue capacity change?"
          description={`Set ${control.profileId} capacity maximum to ${parsed}?`}
          confirmLabel="Confirm capacity change"
          trigger={
            <Button type="button" size="sm" disabled={disabled || active || !valid}>
              Queue change
            </Button>
          }
          onConfirm={() => onSetMaximum(parsed)}
        />
      </div>
      {control.latestCommand ? (
        <div className="text-xs text-muted-foreground">
          Requested {control.latestCommand.requestedMaximum} ·{' '}
          {control.latestCommand.resultMessage ?? 'Awaiting connector result.'}
        </div>
      ) : null}
    </div>
  );
}

interface ProfileCapacitySummaryProps {
  readonly profile: ManagerObservedState;
  readonly control: CapacityControlState | null;
  readonly canAdminister: boolean;
  readonly disabled: boolean;
  readonly onSetMaximum: (maximum: number) => Promise<void>;
}

/** Renders fixed or autoscaled profile capacity and its authorized absolute control. */
export function ProfileCapacitySummary({
  profile,
  control,
  canAdminister,
  disabled,
  onSetMaximum,
}: ProfileCapacitySummaryProps) {
  const autoscaling = profile.autoscaling ?? null;
  if (autoscaling === null) {
    return (
      <section
        className="grid gap-3 border-y bg-muted/10 px-4 py-4"
        data-testid={`profile-capacity-fixed-${profile.profileId}`}
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 className="font-semibold">Fixed capacity</h2>
            <p className="text-xs text-muted-foreground">
              The manager keeps the configured slot count active independent of queued demand.
            </p>
          </div>
          <StatusBadge status="fixed" />
        </div>
        <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-md border bg-border text-center sm:grid-cols-4">
          <CapacityMetric
            label="Configured"
            value={profile.configuredSlots ?? profile.desiredSlots}
            testId={`profile-capacity-configured-${profile.profileId}`}
          />
          <CapacityMetric
            label="Desired"
            value={profile.desiredSlots}
            testId={`profile-capacity-desired-${profile.profileId}`}
          />
          <CapacityMetric
            label="Active"
            value={profile.activeSlots}
            testId={`profile-capacity-active-${profile.profileId}`}
          />
          <CapacityMetric
            label="Draining"
            value={profile.drainingSlots}
            testId={`profile-capacity-draining-${profile.profileId}`}
          />
        </dl>
        {canAdminister && control ? (
          <CapacityMaximumControl
            key={`${control.profileId}-${control.currentMaximum}`}
            control={control}
            disabled={disabled}
            onSetMaximum={onSetMaximum}
          />
        ) : null}
      </section>
    );
  }

  const capacityMetrics = [
    ['Maximum', autoscaling.maximumSlots, 'maximum'],
    ['Target', autoscaling.targetSlots, 'target'],
    ['Active', profile.activeSlots, 'active'],
    ['Draining', profile.drainingSlots, 'draining'],
    ['Assigned', autoscaling.assignedJobs, 'assigned'],
    ['Running', autoscaling.runningJobs, 'running'],
    ['Available / queued', autoscaling.availableJobs, 'available'],
    ['Idle', autoscaling.idleRunners, 'idle'],
    ['Busy', autoscaling.busyRunners, 'busy'],
    ['Minimum idle', autoscaling.minimumIdleSlots, 'minimum-idle'],
    ['Scale-set count', autoscaling.scaleSetCount, 'scale-set-count'],
    ['Scale-down delay', formatSeconds(autoscaling.scaleDownDelaySeconds), 'scale-down-delay'],
    [
      'Scale-down countdown',
      formatScaleDownCountdown(autoscaling.scaleDownAt),
      'scale-down-countdown',
    ],
  ] as const;

  return (
    <section
      className="grid gap-3 border-y bg-muted/10 px-4 py-4"
      data-testid={`profile-capacity-autoscaled-${profile.profileId}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">Demand-driven autoscaling</h2>
          <p className="text-xs text-muted-foreground">
            Capacity follows assigned work and the minimum-idle policy up to the configured maximum.
          </p>
        </div>
        <span data-testid={`profile-autoscaling-status-${profile.profileId}`}>
          <StatusBadge status={autoscaling.status} />
        </span>
      </div>
      <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-md border bg-border text-center sm:grid-cols-3 xl:grid-cols-5">
        {capacityMetrics.map(([label, value, key]) => (
          <CapacityMetric
            key={key}
            label={label}
            value={value}
            testId={`profile-capacity-${key}-${profile.profileId}`}
          />
        ))}
      </dl>
      {canAdminister && control ? (
        <CapacityMaximumControl
          key={`${control.profileId}-${control.currentMaximum}`}
          control={control}
          disabled={disabled}
          onSetMaximum={onSetMaximum}
        />
      ) : null}
      <div
        className={cn(
          'rounded-md border px-3 py-2 text-sm',
          autoscaling.lastError
            ? 'border-red-300 bg-red-50 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100'
            : 'bg-background text-muted-foreground',
        )}
        data-testid={`profile-autoscaling-error-${profile.profileId}`}
      >
        <span className="font-medium">Last error:</span> {autoscaling.lastError || 'None'}
      </div>
    </section>
  );
}
