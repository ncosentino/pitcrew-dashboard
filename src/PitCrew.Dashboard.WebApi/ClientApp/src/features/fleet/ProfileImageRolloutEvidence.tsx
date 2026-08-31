import { Link } from 'react-router-dom';

import type { ImageCandidate } from '@/core/images/imageCandidatesApi';
import { formatTime } from '@/core/formatting/formatters';
import { CopyableId } from '@/core/ui/CopyableId';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import type { ProfileImageRolloutCommand, ProfileImageRolloutControl } from './imageRolloutApi';
import {
  commandAttentionRank,
  formatImageRolloutState,
  imageRolloutStatusTone,
  shortImageIdentity,
} from './profileRolloutView';

interface ProfileImageChangeoverLaneProps {
  readonly tenantId: string;
  readonly control: ProfileImageRolloutControl | null;
  readonly candidate: ImageCandidate | null;
}

/** Shows the exact current and proposed immutable image identities in one bounded lane. */
export function ProfileImageChangeoverLane({
  tenantId,
  control,
  candidate,
}: ProfileImageChangeoverLaneProps) {
  return (
    <section
      aria-labelledby="profile-image-changeover-heading"
      className="overflow-hidden rounded-xl border bg-border"
    >
      <h2 className="sr-only" id="profile-image-changeover-heading">
        Current and target image changeover
      </h2>
      <div className="grid gap-px lg:grid-cols-[minmax(0,1fr)_8rem_minmax(0,1fr)]">
        <div className="min-w-0 bg-card p-4 sm:p-5">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-base font-semibold">Current profile image</h3>
            <StatusBadge status={control?.managerConvergenceStatus ?? 'unavailable'} />
          </div>
          <dl className="mt-4 grid gap-3 text-sm">
            <EvidenceFact
              label="Configured image"
              value={control?.currentImageReference ?? 'Unavailable'}
              mono
            />
            <EvidenceFact
              label="Registry digest"
              value={control?.currentImageDigest ?? 'Unavailable'}
              mono
            />
            <EvidenceFact
              label="Worker revision"
              value={control?.currentWorkerRevision ?? 'Unavailable'}
              mono
            />
            <EvidenceFact
              label="Worker convergence"
              value={
                control?.currentWorkers == null || control.staleWorkers == null
                  ? 'Unavailable'
                  : `${control.currentWorkers} current · ${control.staleWorkers} stale`
              }
            />
          </dl>
        </div>

        <div className="flex min-h-16 items-center justify-center bg-muted px-3 py-4 text-center text-xs font-semibold text-muted-foreground uppercase">
          Approved change
        </div>

        <div className="min-w-0 bg-card p-4 sm:p-5">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-base font-semibold">Selected candidate</h3>
            <StatusBadge
              status={candidate?.outcome ?? 'not selected'}
              tone={candidate?.outcome === 'ready' ? 'positive' : 'neutral'}
            />
          </div>
          {candidate === null ? (
            <p className="mt-4 text-sm text-muted-foreground">
              Select a ready registry candidate to compare exact authority.
            </p>
          ) : (
            <dl className="mt-4 grid gap-3 text-sm">
              <EvidenceFact label="Recipe" value={candidate.recipeId} />
              <EvidenceFact
                label="Immutable digest"
                value={candidate.digest ?? 'Unavailable'}
                mono
              />
              <EvidenceFact label="Platform" value={candidate.platform} />
              <EvidenceFact
                label="Qualified source"
                value={`${candidate.sourceRepository} · ${candidate.sourceCommit.slice(0, 12)}`}
              />
              <div>
                <dt className="text-xs font-medium text-muted-foreground">Qualification</dt>
                <dd className="mt-1">
                  <Link
                    className="text-link underline-offset-4 hover:underline"
                    to={`/tenants/${encodeURIComponent(tenantId)}/images/candidates?request=${encodeURIComponent(candidate.requestId)}`}
                  >
                    Open immutable candidate evidence
                  </Link>
                </dd>
              </div>
            </dl>
          )}
        </div>
      </div>
    </section>
  );
}

interface ProfileImageInvariantLedgerProps {
  readonly control: ProfileImageRolloutControl | null;
}

/** Keeps exact operation fences and preserved non-image effects visible beside confirmation. */
export function ProfileImageInvariantLedger({ control }: ProfileImageInvariantLedgerProps) {
  const preserved = [
    'Repository, organization, or enterprise routing',
    'Labels, default-label policy, runner group, and name prefix',
    'Fixed or scale-set mode, desired capacity, and autoscaling policy',
    'Host admission, service network, read-only volumes, and worker resources',
    'Protected local runner registration credential',
  ];

  return (
    <section
      aria-labelledby="profile-image-invariants-heading"
      className="rounded-xl border bg-card"
    >
      <div className="border-b px-4 py-3 sm:px-5">
        <h2 className="text-base font-semibold" id="profile-image-invariants-heading">
          Preserved operating contract
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          The typed operation changes image authority only. These fingerprints invalidate the
          request if local profile state moves before execution.
        </p>
      </div>
      <div className="grid gap-4 p-4 sm:p-5">
        <ul className="grid gap-2 text-sm">
          {preserved.map((item) => (
            <li className="flex gap-2" key={item}>
              <span aria-hidden="true" className="mt-2 size-1.5 shrink-0 rounded-full bg-primary" />
              <span>{item}</span>
            </li>
          ))}
        </ul>
        <dl className="grid gap-px overflow-hidden rounded-md border bg-border sm:grid-cols-2">
          <FenceFact label="Static profile" value={control?.staticFingerprint ?? null} />
          <FenceFact
            label="Preserved configuration"
            value={control?.preservedConfigurationFingerprint ?? null}
          />
          <FenceFact label="Routing and capacity" value={control?.routingFingerprint ?? null} />
          <FenceFact
            label="Desired generation"
            value={control ? String(control.desiredGeneration) : null}
          />
          <FenceFact label="Desired state hash" value={control?.desiredStateHash ?? null} />
          <FenceFact
            label="Observed age"
            value={
              control
                ? `${control.observedStateAgeSeconds}s · ${control.observedStateFresh ? 'fresh' : 'stale'}`
                : null
            }
          />
        </dl>
      </div>
    </section>
  );
}

interface ProfileImageRolloutCommandSummaryProps {
  readonly tenantId: string;
  readonly command: ProfileImageRolloutCommand;
}

/** Presents the latest durable operation independently from ongoing worker convergence. */
export function ProfileImageRolloutCommandSummary({
  tenantId,
  command,
}: ProfileImageRolloutCommandSummaryProps) {
  return (
    <section
      aria-labelledby="profile-image-latest-command-heading"
      className="rounded-xl border bg-card p-4 shadow-sm sm:p-5"
    >
      <div className="flex flex-wrap items-start justify-between gap-3 border-b pb-4">
        <div>
          <h2 className="text-base font-semibold" id="profile-image-latest-command-heading">
            Latest rollout command
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Durable command state survives refresh and connector restart.
          </p>
        </div>
        <StatusBadge
          status={formatImageRolloutState(command.status)}
          tone={imageRolloutStatusTone(command.status)}
        />
      </div>
      <dl className="grid gap-3 pt-4 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <EvidenceFact label="Requested" value={formatTime(command.requestedAt)} />
        <EvidenceFact
          label="Completed"
          value={command.completedAt ? formatTime(command.completedAt) : 'Not terminal'}
        />
        <EvidenceFact
          label="Worker convergence"
          value={
            command.currentWorkers == null || command.staleWorkers == null
              ? command.managerConvergenceStatus
                ? formatImageRolloutState(command.managerConvergenceStatus)
                : 'Unavailable'
              : `${command.currentWorkers} current · ${command.staleWorkers} stale`
          }
        />
        <EvidenceFact
          label="Target revision"
          value={shortImageIdentity(command.targetWorkerRevision)}
          mono
        />
      </dl>
      <div className="mt-4 flex min-w-0 flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <CopyableId label="Rollout command ID" prefix="Command" value={command.commandId} />
        <span aria-hidden="true">·</span>
        <Link
          className="text-link underline-offset-4 hover:underline"
          to={`/tenants/${encodeURIComponent(tenantId)}/images/candidates?candidate=${encodeURIComponent(command.candidateId)}`}
        >
          Candidate {command.recipeId}
        </Link>
      </div>
      {command.resultMessage ? (
        <StateBanner
          className="mt-4"
          tone={command.status === 'succeeded' ? 'positive' : 'critical'}
        >
          {command.resultMessage}
          {command.failureCategory ? ` Category: ${command.failureCategory}.` : ''}
        </StateBanner>
      ) : null}
      {command.status === 'indeterminate' ? (
        <StateBanner className="mt-4" tone="critical">
          The connector cannot prove the started operation's terminal state. This command is never
          executed automatically again; any new request requires a fresh explicit confirmation.
        </StateBanner>
      ) : null}
    </section>
  );
}

interface ProfileImageRolloutHistoryProps {
  readonly tenantId: string;
  readonly commands: ReadonlyArray<ProfileImageRolloutCommand>;
}

/** Renders bounded immutable rollout history in attention order. */
export function ProfileImageRolloutHistory({
  tenantId,
  commands,
}: ProfileImageRolloutHistoryProps) {
  const ordered = [...commands].sort((left, right) => {
    const rank = commandAttentionRank(left.status) - commandAttentionRank(right.status);
    return rank !== 0 ? rank : right.requestedAt.localeCompare(left.requestedAt);
  });
  if (ordered.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No profile image rollout has been requested for this profile.
      </p>
    );
  }
  return (
    <OperationalList label="Profile image rollout history">
      {ordered.map((command) => (
        <OperationalRow
          key={command.commandId}
          title={command.recipeId}
          description={`${shortImageIdentity(command.targetDigest)} · requested ${formatTime(command.requestedAt)}`}
          status={
            <StatusBadge
              status={formatImageRolloutState(command.status)}
              tone={imageRolloutStatusTone(command.status)}
            />
          }
          metadata={
            <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
              <CopyableId label="Rollout command ID" value={command.commandId} />
              <Link
                className="text-link underline-offset-4 hover:underline"
                to={`/tenants/${encodeURIComponent(tenantId)}/images/candidates?candidate=${encodeURIComponent(command.candidateId)}`}
              >
                Candidate evidence
              </Link>
              {command.currentWorkers == null || command.staleWorkers == null ? null : (
                <span>
                  {command.currentWorkers} current · {command.staleWorkers} stale
                </span>
              )}
            </div>
          }
        />
      ))}
    </OperationalList>
  );
}

function EvidenceFact({
  label,
  value,
  mono = false,
}: {
  readonly label: string;
  readonly value: string;
  readonly mono?: boolean;
}) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd
        className={
          mono
            ? 'mt-1 min-w-0 break-all font-mono text-xs text-foreground'
            : 'mt-1 min-w-0 [overflow-wrap:anywhere] text-sm text-foreground'
        }
        title={value}
      >
        {value}
      </dd>
    </div>
  );
}

function FenceFact({ label, value }: { readonly label: string; readonly value: string | null }) {
  return (
    <div className="min-w-0 bg-background px-3 py-2">
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 break-all font-mono text-xs">{value ?? 'Unavailable'}</dd>
    </div>
  );
}
