import type { ReactNode } from 'react';

import { cn } from '@/lib/utils';

/** Semantic tone for a state banner, driving both color and default ARIA role. */
export type StateBannerTone = 'positive' | 'caution' | 'critical';

const toneClasses: Record<StateBannerTone, string> = {
  positive:
    'border-status-positive-foreground/30 bg-status-positive text-status-positive-foreground',
  caution: 'border-status-caution-foreground/30 bg-status-caution text-status-caution-foreground',
  critical:
    'border-status-critical-foreground/30 bg-status-critical text-status-critical-foreground',
};

const defaultRole: Record<StateBannerTone, 'status' | 'alert'> = {
  positive: 'status',
  caution: 'status',
  critical: 'alert',
};

/** Props for the shared operational state banner. */
export interface StateBannerProps {
  readonly tone: StateBannerTone;
  readonly children: ReactNode;
  /**
   * Overrides the tone's default ARIA role. Use this when the same banner
   * toggles between a live announcement and a passive status depending on
   * whether the underlying data is current or stale.
   */
  readonly role?: 'status' | 'alert';
  readonly className?: string;
  readonly 'data-testid'?: string;
}

/**
 * Renders a bordered, token-driven banner for stale-data notices, recovered
 * incidents, and error conditions using DESIGN.md's status-positive /
 * -caution / -critical roles rather than ad hoc amber/red utility classes.
 */
export function StateBanner({ tone, children, role, className, ...rest }: StateBannerProps) {
  return (
    <div
      className={cn('rounded-lg border p-4', toneClasses[tone], className)}
      role={role ?? defaultRole[tone]}
      data-testid={rest['data-testid']}
    >
      {children}
    </div>
  );
}
