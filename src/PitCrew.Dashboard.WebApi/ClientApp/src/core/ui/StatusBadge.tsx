import { cn } from '@/lib/utils';

interface StatusBadgeProps {
  readonly status: string;
  readonly tone?: 'positive' | 'caution' | 'critical' | 'neutral';
}

type StatusTone = NonNullable<StatusBadgeProps['tone']>;

function toneClasses(tone: StatusTone): string {
  switch (tone) {
    case 'positive':
      return 'bg-status-positive text-status-positive-foreground';
    case 'caution':
      return 'bg-status-caution text-status-caution-foreground';
    case 'critical':
      return 'bg-status-critical text-status-critical-foreground';
    case 'neutral':
      return 'bg-muted text-muted-foreground';
  }
}

function statusTone(status: string): StatusTone {
  switch (status.toLocaleLowerCase()) {
    case 'available':
    case 'connected':
    case 'idle':
    case 'online':
    case 'running':
    case 'accepted':
    case 'active':
    case 'succeeded':
    case 'recovered':
    case 'healthy':
    case 'clean':
    case 'current':
      return 'positive';
    case 'partial':
    case 'backoff':
    case 'degraded':
    case 'disconnected':
    case 'draining':
    case 'restarting':
    case 'rotation requested':
    case 'starting':
    case 'stopping':
    case 'delivered':
    case 'pending':
    case 'warning':
    case 'acknowledged':
    case 'stale':
    case 'retry-scheduled':
    case 'rolling':
    case 'withheld':
    case 'stopped':
      return 'caution';
    case 'invalid':
    case 'conflict':
    case 'rejected':
    case 'registration-missing':
    case 'failed':
    case 'critical':
    case 'timed-out':
    case 'blocked':
    case 'oom-killed':
    case 'sigkill':
    case 'signal':
    case 'error':
    case 'launch-failure':
      return 'critical';
    default:
      return 'neutral';
  }
}

/** Renders a status label with an optional independent semantic tone. */
export function StatusBadge({ status, tone }: StatusBadgeProps) {
  const resolvedTone = tone ?? statusTone(status);
  return (
    <span
      className={cn(
        'inline-flex whitespace-nowrap rounded-full px-2 py-1 text-xs font-semibold capitalize',
        toneClasses(resolvedTone),
      )}
      data-status-tone={resolvedTone}
    >
      {status.replaceAll('-', ' ')}
    </span>
  );
}
