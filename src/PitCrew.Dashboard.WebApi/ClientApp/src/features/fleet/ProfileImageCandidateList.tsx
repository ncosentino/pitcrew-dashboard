import { Link } from 'react-router-dom';

import type { ImageCandidate } from '@/core/images/imageCandidatesApi';
import { formatTime } from '@/core/formatting/formatters';
import { EmptyState } from '@/core/ui/EmptyState';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { StatusBadge } from '@/core/ui/StatusBadge';

import type { ProfileImageRolloutControl } from './imageRolloutApi';
import { describeCandidateCompatibility, shortImageIdentity } from './profileRolloutView';

interface ProfileImageCandidateListProps {
  readonly tenantId: string;
  readonly nodeOnline: boolean;
  readonly control: ProfileImageRolloutControl | null;
  readonly candidates: ReadonlyArray<ImageCandidate>;
  readonly selectedCandidateId: string | null;
  readonly truncated: boolean;
}

/** Presents rollout-ready candidate evidence without importing the Images feature. */
export function ProfileImageCandidateList({
  tenantId,
  nodeOnline,
  control,
  candidates,
  selectedCandidateId,
  truncated,
}: ProfileImageCandidateListProps) {
  const readyRegistryCandidates = candidates.filter(
    (candidate) =>
      candidate.outcome === 'ready' &&
      candidate.outputMode === 'registry' &&
      candidate.digest !== null &&
      candidate.immutableReference !== null,
  );
  if (readyRegistryCandidates.length === 0) {
    return (
      <EmptyState
        title="No rollout-ready candidates"
        description="Build and qualify a registry candidate in Runner images before changing this profile."
        action={
          <Link
            className="text-link underline-offset-4 hover:underline"
            to={`/tenants/${encodeURIComponent(tenantId)}/images/candidates`}
          >
            Open Runner image candidates
          </Link>
        }
      />
    );
  }

  return (
    <section aria-labelledby="profile-image-candidates-heading" className="grid min-w-0 gap-3">
      <div>
        <h2 className="text-base font-semibold" id="profile-image-candidates-heading">
          Ready candidates
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Select one immutable registry candidate. Compatibility is recalculated from current
          profile evidence.
        </p>
      </div>
      {truncated ? (
        <p className="text-xs text-muted-foreground">
          This bounded view shows the newest 100 candidates. A deep-linked candidate outside this
          window is loaded separately without replacing the selection.
        </p>
      ) : null}
      <OperationalList label="Ready profile image candidates">
        {readyRegistryCandidates.map((candidate) => {
          const compatibility = describeCandidateCompatibility(candidate, control, nodeOnline);
          const selected = candidate.candidateId === selectedCandidateId;
          return (
            <OperationalRow
              key={candidate.candidateId}
              selected={selected}
              title={candidate.recipeId}
              description={`${candidate.sourceRepository} · ${candidate.sourceCommit.slice(0, 12)}`}
              status={
                <StatusBadge
                  status={
                    compatibility.alreadyCurrent
                      ? 'Already current'
                      : compatibility.eligible
                        ? 'Compatible'
                        : 'Unavailable'
                  }
                  tone={
                    compatibility.alreadyCurrent || compatibility.eligible ? 'positive' : 'caution'
                  }
                />
              }
              metadata={
                <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
                  <span className="font-mono">{shortImageIdentity(candidate.digest)}</span>
                  <span>{candidate.platform}</span>
                  <span>Qualified {formatTime(candidate.storedAt)}</span>
                </div>
              }
              actions={
                <Link
                  aria-current={selected ? 'true' : undefined}
                  className="inline-flex min-h-8 items-center rounded-md border px-3 text-sm font-medium hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
                  to={`?candidate=${encodeURIComponent(candidate.candidateId)}`}
                >
                  {selected ? 'Selected' : 'Compare'}
                </Link>
              }
            >
              {!compatibility.eligible && !compatibility.alreadyCurrent ? (
                <p className="text-xs text-muted-foreground">{compatibility.reasons[0]}</p>
              ) : null}
            </OperationalRow>
          );
        })}
      </OperationalList>
    </section>
  );
}
