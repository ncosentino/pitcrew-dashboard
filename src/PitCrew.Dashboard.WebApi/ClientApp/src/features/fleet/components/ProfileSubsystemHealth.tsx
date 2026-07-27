import {
  describeSubsystemHealth,
  describeSubsystemOperation,
  type ManagerObservedState,
  type SubsystemHealthSummary,
} from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ProfileEvidenceDisclosure } from './ProfileEvidenceDisclosure';

interface SubsystemCardProps {
  readonly summary: SubsystemHealthSummary | null;
  readonly subsystem: string;
  readonly title: string;
  readonly testId: string;
}

function SubsystemCard({ summary, subsystem, title, testId }: SubsystemCardProps) {
  const health = describeSubsystemHealth(summary, subsystem);

  return (
    <div className="grid gap-2 rounded-md border bg-background px-3 py-3" data-testid={testId}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-medium">{title}</h3>
        <span data-testid={`${testId}-state`}>
          <StatusBadge status={health.status} />
        </span>
      </div>
      <p className="text-xs text-muted-foreground">{health.description}</p>
      {summary === null ? null : (
        <dl className="grid gap-1 text-xs">
          <div className="flex flex-wrap gap-2">
            <dt className="text-muted-foreground uppercase">Last success</dt>
            <dd data-testid={`${testId}-last-success`}>
              {describeSubsystemOperation(
                summary.lastSuccess,
                'No successful operation has been observed, which does not mean the subsystem is unusable.',
              )}
            </dd>
          </div>
          <div className="flex flex-wrap gap-2">
            <dt className="text-muted-foreground uppercase">Last failure</dt>
            <dd data-testid={`${testId}-last-failure`}>
              {describeSubsystemOperation(
                summary.lastFailure,
                'No failed operation has been observed.',
              )}
            </dd>
          </div>
          <div className="flex flex-wrap gap-2">
            <dt className="text-muted-foreground uppercase">Backoff</dt>
            <dd data-testid={`${testId}-backoff`}>
              {summary.retryAt === null
                ? 'No retry is scheduled.'
                : `Retry scheduled for ${formatTime(summary.retryAt)}.`}
            </dd>
          </div>
        </dl>
      )}
    </div>
  );
}

/** Renders manager-reported Docker and GitHub operation health as evidence, never as a diagnosis. */
export function ProfileSubsystemHealth({ profile }: { readonly profile: ManagerObservedState }) {
  const health = profile.subsystemHealth;
  const docker = describeSubsystemHealth(health?.docker ?? null, 'Docker');
  const github = describeSubsystemHealth(health?.github ?? null, 'GitHub');

  return (
    <ProfileEvidenceDisclosure
      title="Manager subsystem health"
      description="Manager-reported outcomes for the Docker and GitHub operations this manager performed."
      summary={
        <>
          <span
            className="flex items-center gap-1"
            data-testid={`profile-subsystem-summary-docker-${profile.profileId}`}
          >
            Docker
            <StatusBadge status={docker.status} />
          </span>
          <span
            className="flex items-center gap-1"
            data-testid={`profile-subsystem-summary-github-${profile.profileId}`}
          >
            GitHub
            <StatusBadge status={github.status} />
          </span>
        </>
      }
      testId={`profile-subsystem-health-${profile.profileId}`}
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <SubsystemCard
          summary={health?.docker ?? null}
          subsystem="Docker"
          title="Docker operations"
          testId={`profile-subsystem-docker-${profile.profileId}`}
        />
        <SubsystemCard
          summary={health?.github ?? null}
          subsystem="GitHub"
          title="GitHub operations"
          testId={`profile-subsystem-github-${profile.profileId}`}
        />
      </div>
    </ProfileEvidenceDisclosure>
  );
}
