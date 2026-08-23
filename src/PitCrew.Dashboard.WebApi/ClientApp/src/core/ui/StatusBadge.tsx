import { cn } from '@/lib/utils';

interface StatusBadgeProps {
  readonly status: string;
}

function statusClasses(status: string): string {
  switch (status) {
    case 'available':
    case 'connected':
    case 'idle':
    case 'online':
    case 'running':
    case 'accepted':
    case 'active':
    case 'succeeded':
    case 'recovered':
    case 'resolved':
    case 'healthy':
    case 'clean':
    case 'current':
      return 'bg-status-positive text-status-positive-foreground';
    case 'partial':
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
      return 'bg-status-caution text-status-caution-foreground';
    case 'backoff':
    case 'disconnected':
    case 'degraded':
    case 'invalid':
    case 'conflict':
    case 'revoked':
    case 'rejected':
    case 'registration-missing':
    case 'failed':
    case 'critical':
    case 'triggered':
    case 'timed-out':
    case 'blocked':
    case 'unavailable':
    case 'oom-killed':
    case 'sigkill':
    case 'signal':
    case 'error':
    case 'launch-failure':
      return 'bg-status-critical text-status-critical-foreground';
    default:
      return 'bg-muted text-muted-foreground';
  }
}

/** Renders a generic semantic status label. */
export function StatusBadge({ status }: StatusBadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex rounded-full px-2 py-1 text-xs font-semibold capitalize',
        statusClasses(status),
      )}
    >
      {status.replaceAll('-', ' ')}
    </span>
  );
}
