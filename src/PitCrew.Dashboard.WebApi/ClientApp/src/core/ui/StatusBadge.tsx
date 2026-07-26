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
    case 'succeeded':
    case 'clean':
      return 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200';
    case 'partial':
    case 'draining':
    case 'restarting':
    case 'rotation requested':
    case 'starting':
    case 'stopping':
    case 'delivered':
    case 'pending':
    case 'stale':
      return 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200';
    case 'backoff':
    case 'disconnected':
    case 'degraded':
    case 'invalid':
    case 'conflict':
    case 'revoked':
    case 'rejected':
    case 'registration-missing':
    case 'failed':
    case 'unavailable':
    case 'oom-killed':
    case 'sigkill':
    case 'signal':
    case 'error':
    case 'launch-failure':
      return 'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200';
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
