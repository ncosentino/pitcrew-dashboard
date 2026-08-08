import { useEffect, useRef, useState, type ReactNode } from 'react';

import { ConfirmActionDialog } from '@/components/ConfirmActionDialog';
import { Button } from '@/components/ui/button';
import {
  type CapacityCommandState,
  type FleetNode,
  type ManagerObservedState,
  type RecoveryCommandState,
  type RecoveryCommandStatus,
  type RecoveryControlState,
} from '@/core/fleet';
import { formatCounter, formatTime } from '@/core/formatting/formatters';
import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { cn } from '@/lib/utils';

import {
  describeRecoveryAvailability,
  recoveryConfirmationSignature,
  type RecoveryFences,
  type RecoveryUnavailableCode,
} from '../managerRecovery';

const statusDescriptions: Record<RecoveryCommandStatus, string> = {
  queued: 'Queued for the next outbound connector synchronization.',
  claimed: 'Durably claimed by the connector.',
  started: 'Local recovery execution started.',
  succeeded: 'The connector reported a completed manager-only restart.',
  rejected: 'The connector rejected the command before executing it.',
  failed: 'Local execution failed.',
  expired: 'The command expired before the connector claimed it.',
  indeterminate: 'The outcome is indeterminate and requires local investigation.',
};

const informationalUnavailableCodes = new Set<RecoveryUnavailableCode>([
  'not-authorized',
  'connector-read-only',
  'locally-disallowed',
]);

function shortHash(value: string | null): string {
  if (value === null) return 'Unavailable';
  return value.length <= 12 ? value : `${value.slice(0, 12)}…`;
}

interface EvidenceRowProps {
  readonly label: string;
  readonly value: ReactNode;
  readonly testId: string;
}

function EvidenceRow({ label, value, testId }: EvidenceRowProps) {
  return (
    <div className="bg-background px-3 py-2">
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 text-sm" data-testid={testId}>
        {value}
      </dd>
    </div>
  );
}

interface RecoveryLifecycleProps {
  readonly command: RecoveryCommandState;
  readonly profileId: string;
}

function RecoveryLifecycle({ command, profileId }: RecoveryLifecycleProps) {
  const steps = [
    ['Queued', command.requestedAt],
    ['Delivered', command.deliveredAt],
    ['Claimed', command.claimedAt],
    ['Started', command.startedAt],
    ['Completed', command.completedAt],
  ] as const;

  return (
    <div
      className="grid gap-2 rounded-md border bg-background px-3 py-3"
      data-testid={`profile-recovery-progress-${profileId}`}
      role="status"
      aria-live="polite"
    >
      <div className="flex flex-wrap items-center gap-2">
        <StatusBadge status={command.status} />
        <span className="text-sm">{statusDescriptions[command.status]}</span>
      </div>
      <p className="text-xs text-muted-foreground">
        Requested by GitHub user {command.requestedByGitHubUserId} · expires{' '}
        {formatTime(command.expiresAt)}
      </p>
      <ul className="grid gap-1 text-xs text-muted-foreground sm:grid-cols-2">
        {steps.map(([label, value]) => (
          <li key={label}>
            <span className="font-medium">{label}:</span>{' '}
            {value === null ? 'Not recorded' : formatTime(value)}
          </li>
        ))}
      </ul>
      {command.resultMessage === null ? null : (
        <p className="text-sm" data-testid={`profile-recovery-result-${profileId}`}>
          {command.resultMessage}
        </p>
      )}
    </div>
  );
}

interface RecoveryHistoryProps {
  readonly commands: readonly RecoveryCommandState[];
  readonly profileId: string;
}

function RecoveryHistory({ commands, profileId }: RecoveryHistoryProps) {
  if (commands.length === 0) {
    return (
      <p
        className="text-sm text-muted-foreground"
        data-testid={`profile-recovery-empty-${profileId}`}
      >
        No manager recovery has been requested for this profile.
      </p>
    );
  }

  return (
    <div
      className="overflow-x-auto rounded-md border"
      data-testid={`profile-recovery-history-${profileId}`}
    >
      <table className="w-full text-left text-sm">
        <caption className="px-3 py-2 text-left text-xs text-muted-foreground">
          Immutable recovery history for profile {profileId}
        </caption>
        <thead className="bg-muted/40 text-xs uppercase">
          <tr>
            <th scope="col" className="px-3 py-2">
              Requested
            </th>
            <th scope="col" className="px-3 py-2">
              Actor
            </th>
            <th scope="col" className="px-3 py-2">
              Outcome
            </th>
            <th scope="col" className="px-3 py-2">
              Manager instance
            </th>
            <th scope="col" className="px-3 py-2">
              Detail
            </th>
          </tr>
        </thead>
        <tbody>
          {commands.map((command) => (
            <tr key={command.commandId} className="border-t align-top">
              <td className="px-3 py-2">{formatTime(command.requestedAt)}</td>
              <td className="px-3 py-2">{command.requestedByGitHubUserId}</td>
              <td className="px-3 py-2">
                <span className="flex flex-wrap items-center gap-2">
                  <StatusBadge status={command.status} />
                  <span>{command.status}</span>
                </span>
                {command.failureCategory === null ? null : (
                  <span
                    className="block text-xs text-muted-foreground"
                    data-testid={`profile-recovery-category-${command.commandId}`}
                  >
                    Category: {command.failureCategory}
                  </span>
                )}
              </td>
              <td className="px-3 py-2 text-xs">
                {command.beforeManagerInstanceId ?? 'Unavailable'} →{' '}
                {command.afterManagerInstanceId ?? 'Unavailable'}
              </td>
              <td className="px-3 py-2 text-xs">
                {command.resultMessage ?? statusDescriptions[command.status]}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

interface ProfileManagerRecoveryProps {
  readonly tenantId: string;
  readonly node: FleetNode;
  readonly profile: ManagerObservedState;
  readonly control: RecoveryControlState | null;
  readonly capacityCommand: CapacityCommandState | null;
  readonly canAdminister: boolean;
  readonly generatedAt: string;
  readonly isMutating: boolean;
  readonly onRecover: (fences: RecoveryFences) => Promise<void>;
}

/** Renders the fenced manager-recovery action, its progress, and its audit history. */
export function ProfileManagerRecovery({
  tenantId,
  node,
  profile,
  control,
  capacityCommand,
  canAdminister,
  generatedAt,
  isMutating,
  onRecover,
}: ProfileManagerRecoveryProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [isAcknowledged, setIsAcknowledged] = useState(false);
  const [invalidation, setInvalidation] = useState<string | null>(null);
  const confirmedSignature = useRef<string | null>(null);

  const availability = describeRecoveryAvailability({
    node,
    profile,
    control,
    capacityCommand,
    canAdminister,
    generatedAt,
  });
  const signature = recoveryConfirmationSignature(availability, control);
  const profileId = profile.profileId;
  const explanationId = `profile-recovery-explanation-${profileId}`;
  const explanationIsInformational =
    availability.code !== null && informationalUnavailableCodes.has(availability.code);

  useEffect(() => {
    if (!isOpen || confirmedSignature.current === signature) return;
    setIsOpen(false);
    setIsAcknowledged(false);
    setInvalidation(
      'The expected recovery evidence changed while the confirmation was open. Review the refreshed evidence and confirm again.',
    );
  }, [isOpen, signature]);

  const changeOpen = (next: boolean) => {
    if (next) {
      confirmedSignature.current = signature;
      setIsAcknowledged(false);
      setInvalidation(null);
    }
    setIsOpen(next);
  };

  const confirm = async () => {
    if (
      !isAcknowledged ||
      !availability.canQueue ||
      availability.fences === null ||
      confirmedSignature.current !== signature
    ) {
      setInvalidation(
        'The expected recovery evidence is no longer the evidence that was confirmed. Nothing was queued.',
      );
      setIsOpen(false);
      setIsAcknowledged(false);
      return;
    }
    setIsOpen(false);
    setIsAcknowledged(false);
    await onRecover(availability.fences);
  };

  const autoscaling = profile.autoscaling ?? null;
  const latestCommand = control?.latestCommand ?? null;
  const fences = availability.fences;

  const details = (
    <ConfirmationSummary
      identity={[
        {
          label: 'Scope',
          value: `${tenantId} · ${node.displayName} · ${profileId}`,
          testId: `profile-recovery-scope-${profileId}`,
        },
        {
          label: 'Observed manager',
          value: `${profile.managerInstanceId} · generation ${profile.generation}`,
          testId: `profile-recovery-observed-${profileId}`,
        },
        {
          label: 'Counts',
          value: `configured ${formatCounter(profile.configuredSlots ?? profile.desiredSlots)} · target ${formatCounter(
            autoscaling?.targetSlots ?? profile.desiredSlots,
          )} · local ${formatCounter(profile.activeSlots)} · GitHub eligible ${formatCounter(
            profile.eligibleSlots,
          )}`,
          testId: `profile-recovery-counts-${profileId}`,
        },
        {
          label: 'Degraded evidence',
          value: (
            <>
              <span>Manager status {profile.managerStatus}</span>
              {autoscaling === null ? null : (
                <>
                  <span aria-hidden="true"> · </span>
                  <span>
                    autoscaling {autoscaling.status}
                    {autoscaling.lastError === null ? '' : `: ${autoscaling.lastError}`}
                  </span>
                </>
              )}
            </>
          ),
          testId: `profile-recovery-degraded-${profileId}`,
        },
      ]}
      fences={[
        {
          label: 'Manager instance, generation, and hash',
          value:
            fences === null
              ? 'Unavailable'
              : `${fences.expectedManagerInstanceId} · generation ${fences.expectedGeneration} · hash ${shortHash(fences.expectedDesiredStateHash)}`,
          testId: `profile-recovery-fences-${profileId}`,
        },
      ]}
      effects={[
        'Local PitCrew restarts this one profile manager exactly once, using only the expected fences shown above.',
      ]}
      prohibitedEffects={[
        'No worker, Docker daemon or Desktop, host, capacity, image, release, routing, or configuration change is made, and no stopped manager or profile is started.',
        'Recovery can still fail or end indeterminate. An indeterminate outcome is never retried automatically and requires local investigation on the host.',
      ]}
      acknowledgement={{
        label: (
          <>
            I confirm recovery of manager {fences?.expectedManagerInstanceId ?? 'unavailable'} at
            generation {fences?.expectedGeneration ?? 'unavailable'} with desired-state hash{' '}
            {shortHash(fences?.expectedDesiredStateHash ?? null)}.
          </>
        ),
        checked: isAcknowledged,
        onCheckedChange: setIsAcknowledged,
        testId: `profile-recovery-acknowledgement-${profileId}`,
      }}
    />
  );

  return (
    <section
      className="grid gap-2 rounded-lg border bg-card px-4 py-4 shadow-sm"
      data-testid={`profile-recovery-${profileId}`}
      aria-labelledby={`profile-recovery-heading-${profileId}`}
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h2 className="font-semibold" id={`profile-recovery-heading-${profileId}`}>
            Manager recovery
          </h2>
          <p className="text-xs text-muted-foreground">
            Restarts one wedged profile manager through local PitCrew. The dashboard never repairs
            Docker, the host, or any worker.
          </p>
        </div>
        <ConfirmActionDialog
          title="Queue manager recovery?"
          description={`Recover the manager for profile ${profileId} on ${node.displayName}.`}
          confirmLabel="Queue manager recovery"
          confirmVariant="destructive"
          details={details}
          confirmDisabled={!isAcknowledged}
          open={isOpen}
          onOpenChange={changeOpen}
          onConfirm={confirm}
          trigger={
            <Button
              type="button"
              size="sm"
              variant="destructive"
              disabled={!availability.canQueue || isMutating}
              aria-describedby={availability.explanation === null ? undefined : explanationId}
              data-testid={`profile-recovery-action-${profileId}`}
            >
              Recover manager
            </Button>
          }
        />
      </div>

      {availability.explanation === null ? null : (
        <p
          className={cn(
            'rounded-md border px-3 py-2 text-sm',
            explanationIsInformational
              ? 'bg-background text-muted-foreground'
              : 'border-amber-300 bg-amber-50 text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100',
          )}
          id={explanationId}
          data-testid={`profile-recovery-unavailable-${profileId}`}
          data-reason={availability.code ?? ''}
        >
          {availability.explanation}
        </p>
      )}

      {invalidation === null ? null : (
        <p
          role="alert"
          className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
          data-testid={`profile-recovery-invalidated-${profileId}`}
        >
          {invalidation}
        </p>
      )}

      {latestCommand === null ? null : (
        <RecoveryLifecycle command={latestCommand} profileId={profileId} />
      )}

      {latestCommand === null ? null : (
        <dl
          className="grid grid-cols-1 gap-px overflow-hidden rounded-md border bg-border sm:grid-cols-2"
          data-testid={`profile-recovery-evidence-${profileId}`}
        >
          <EvidenceRow
            label="Manager instance transition"
            value={`${latestCommand.beforeManagerInstanceId ?? 'Unavailable'} → ${
              latestCommand.afterManagerInstanceId ?? 'Unavailable'
            }`}
            testId={`profile-recovery-transition-${profileId}`}
          />
          <EvidenceRow
            label="Generation and hash"
            value={`generation ${profile.generation} · hash ${shortHash(profile.desiredStateHash)}`}
            testId={`profile-recovery-identity-${profileId}`}
          />
          <EvidenceRow
            label="Observed-state freshness"
            value={
              control === null
                ? 'Unavailable'
                : `${control.observedStateAgeSeconds} s at last report · limit ${control.observedStateMaximumAgeSeconds} s`
            }
            testId={`profile-recovery-freshness-${profileId}`}
          />
          <EvidenceRow
            label="Counts after recovery"
            value={`local ${formatCounter(profile.activeSlots)} · GitHub eligible ${formatCounter(
              profile.eligibleSlots,
            )} · target ${formatCounter(autoscaling?.targetSlots ?? profile.desiredSlots)}`}
            testId={`profile-recovery-counts-after-${profileId}`}
          />
          <EvidenceRow
            label="Failure category"
            value={latestCommand.failureCategory ?? 'None recorded'}
            testId={`profile-recovery-failure-${profileId}`}
          />
          <EvidenceRow
            label="Worker observation"
            value="Recovery issued no worker-directed mutation. Equal worker counts before and after are an observation, never proof that workers were preserved."
            testId={`profile-recovery-worker-note-${profileId}`}
          />
        </dl>
      )}

      {control === null && latestCommand === null ? null : (
        <RecoveryHistory commands={control?.recentCommands ?? []} profileId={profileId} />
      )}
    </section>
  );
}
