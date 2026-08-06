import { useState, type ReactNode } from 'react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import { type CapacityControlState, type ManagerObservedState } from '@/core/fleet';
import { formatSeconds, formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

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
    <div className="bg-background px-3 py-2.5">
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 text-xl font-semibold tabular-nums" data-testid={testId}>
        {value}
      </dd>
    </div>
  );
}

function CapacityDetail({ label, value, testId }: CapacityMetricProps) {
  return (
    <div className="flex min-w-0 items-baseline gap-2">
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="font-medium tabular-nums" data-testid={testId}>
        {value}
      </dd>
    </div>
  );
}

interface CapacityMaximumControlProps {
  readonly control: CapacityControlState;
  readonly disabled: boolean;
  readonly onSetMaximum: (maximum: number, resumeCommandId?: string) => Promise<void>;
}

function CapacityMaximumControl({ control, disabled, onSetMaximum }: CapacityMaximumControlProps) {
  const [draft, setDraft] = useState(
    String(control.currentMaximum === 0 ? (control.resumeMaximum ?? 1) : control.currentMaximum),
  );
  const parsed = Number(draft);
  const active =
    control.latestCommand?.status === 'pending' || control.latestCommand?.status === 'delivered';
  const resume =
    control.pauseCommandId !== null && control.resumeMaximum !== null
      ? { commandId: control.pauseCommandId, maximum: control.resumeMaximum }
      : null;
  const operationStatus =
    active && control.latestCommand?.requestedMaximum === 0
      ? 'pausing'
      : active && control.currentMaximum === 0 && control.latestCommand?.resumesCommandId !== null
        ? 'resuming'
        : active
          ? control.latestCommand?.status
          : control.currentMaximum === 0
            ? 'paused'
            : control.latestCommand?.status;
  const valid =
    Number.isInteger(parsed) &&
    parsed >= 1 &&
    parsed <= control.maximumAllowed &&
    parsed !== control.currentMaximum &&
    !(control.currentMaximum === 0 && resume?.maximum === parsed);

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
        {operationStatus ? <StatusBadge status={operationStatus} /> : null}
      </div>
      {control.currentMaximum === 0 ? (
        <p className="text-sm text-muted-foreground">
          New work is paused. Busy workers continue until their current jobs finish.
        </p>
      ) : null}
      <div className="flex flex-wrap items-center gap-2">
        <label className="text-xs font-medium" htmlFor={`capacity-maximum-${control.profileId}`}>
          Explicit maximum
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
        {control.supportsZeroMaximum && control.currentMaximum > 0 ? (
          <ConfirmActionDialog
            title="Pause new work?"
            description={`Set ${control.profileId} capacity to zero? Busy workers continue, but no replacement or new worker will be admitted.`}
            confirmLabel="Pause new work"
            trigger={
              <Button type="button" size="sm" variant="destructive" disabled={disabled || active}>
                Pause new work
              </Button>
            }
            onConfirm={() => onSetMaximum(0)}
          />
        ) : null}
        {control.currentMaximum === 0 && resume !== null ? (
          <ConfirmActionDialog
            title={`Resume to ${resume.maximum}?`}
            description={`Restore ${control.profileId} to its recorded pre-pause maximum of ${resume.maximum}.`}
            confirmLabel={`Resume to ${resume.maximum}`}
            trigger={
              <Button type="button" size="sm" disabled={disabled || active}>
                Resume to {resume.maximum}
              </Button>
            }
            onConfirm={() => onSetMaximum(resume.maximum, resume.commandId)}
          />
        ) : null}
      </div>
      {control.latestCommand ? (
        <div className="text-xs text-muted-foreground">
          Previous {control.latestCommand.previousMaximum ?? 'unavailable'} · requested{' '}
          {control.latestCommand.requestedMaximum} ·{' '}
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
  readonly onSetMaximum: (maximum: number, resumeCommandId?: string) => Promise<void>;
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
        className="grid gap-3 rounded-lg border bg-card px-4 py-4 shadow-sm"
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
        <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-md border bg-border text-center sm:grid-cols-5">
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
            label="Local slots"
            value={profile.activeSlots}
            testId={`profile-capacity-active-${profile.profileId}`}
          />
          <CapacityMetric
            label="GitHub eligible"
            value={profile.eligibleSlots ?? 'Unknown'}
            testId={`profile-capacity-eligible-${profile.profileId}`}
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

  const primaryMetrics = [
    ['Maximum', autoscaling.maximumSlots, 'maximum'],
    ['Target', autoscaling.targetSlots, 'target'],
    ['Local slots', profile.activeSlots, 'active'],
    ['GitHub eligible', profile.eligibleSlots ?? 'Unknown', 'eligible'],
    ['Assigned', autoscaling.assignedJobs, 'assigned'],
    ['Draining', profile.drainingSlots, 'draining'],
  ] as const;
  const secondaryMetrics = [
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
      className="grid gap-3 rounded-lg border bg-card px-4 py-4 shadow-sm"
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
      <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-md border bg-border text-center sm:grid-cols-3 xl:grid-cols-6">
        {primaryMetrics.map(([label, value, key]) => (
          <CapacityMetric
            key={key}
            label={label}
            value={value}
            testId={`profile-capacity-${key}-${profile.profileId}`}
          />
        ))}
      </dl>
      <dl className="flex flex-wrap gap-x-5 gap-y-2 rounded-md border bg-background px-3 py-2 text-sm">
        {secondaryMetrics.map(([label, value, key]) => (
          <CapacityDetail
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
      {autoscaling.lastError ? (
        <div
          className="rounded-md border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
          data-testid={`profile-autoscaling-error-${profile.profileId}`}
        >
          <span className="font-medium">Last error:</span> {autoscaling.lastError}
        </div>
      ) : null}
    </section>
  );
}
