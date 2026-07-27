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
    return (
      <span
        className="inline-flex max-w-48 items-center whitespace-nowrap text-xs text-muted-foreground"
        title={summary.description}
      >
        <span aria-hidden="true">Not recorded</span>
        <span className="sr-only">{summary.description}</span>
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-2 whitespace-nowrap">
      <StatusBadge status={lastExit.classification} />
      <span className="sr-only">{summary.description}</span>
      <span className="text-xs text-muted-foreground" title={summary.description}>
        {formatTime(lastExit.observedAt)} · {lastExit.evidence}
      </span>
    </span>
  );
}
