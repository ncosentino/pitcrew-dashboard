import { describeExitEvidence, type WorkerLastExit } from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

/** Renders a worker image digest, keeping an unreported image distinct from a known image. */
export function WorkerImageIdentity({ imageId }: { readonly imageId: string | null }) {
  if (imageId === null) return <span>Unavailable</span>;
  return (
    <span className="font-mono text-xs" title={imageId}>
      {imageId.slice(7, 19)}
    </span>
  );
}

/** Renders bounded worker exit evidence without describing unknown evidence as clean. */
export function WorkerExitEvidence({ lastExit }: { readonly lastExit: WorkerLastExit | null }) {
  const summary = describeExitEvidence(lastExit);
  if (lastExit === null) {
    return <span className="text-xs text-muted-foreground">{summary.description}</span>;
  }
  return (
    <>
      <StatusBadge status={lastExit.classification} />
      <span className="sr-only">{summary.description}</span>
      <div className="text-xs text-muted-foreground">
        {formatTime(lastExit.observedAt)} · {lastExit.evidence}
      </div>
    </>
  );
}
