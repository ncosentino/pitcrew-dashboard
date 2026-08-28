import { useId } from 'react';
import {
  ActivityIcon,
  Building2Icon,
  LifeBuoyIcon,
  ServerIcon,
  SettingsIcon,
  TriangleAlertIcon,
  type LucideIcon,
} from 'lucide-react';
import { Link, matchPath } from 'react-router-dom';

import type {
  FeatureNavigationGroup,
  FeatureNavigationIcon,
} from '@/core/features/FeatureManifest';
import { cn } from '@/lib/utils';

export interface ShellNavigationItem {
  readonly label: string;
  readonly description: string;
  readonly path: string;
  readonly group: FeatureNavigationGroup;
  readonly order: number;
  readonly icon: FeatureNavigationIcon;
  readonly activePaths: ReadonlyArray<string>;
  readonly badge?: {
    readonly label: string;
    readonly accessibleLabel: string;
    readonly tone: 'critical' | 'caution' | 'neutral';
  };
}

interface ShellNavigationProps {
  readonly items: ReadonlyArray<ShellNavigationItem>;
  readonly pathname: string;
  readonly onNavigate: () => void;
  readonly compact?: boolean;
}

const navigationGroups: ReadonlyArray<{
  readonly key: FeatureNavigationGroup;
  readonly label: string;
}> = [
  { key: 'monitor', label: 'Monitor' },
  { key: 'operate', label: 'Operate' },
  { key: 'configure', label: 'Configure' },
];

const navigationIcons: Record<FeatureNavigationIcon, LucideIcon> = {
  fleet: ActivityIcon,
  incidents: TriangleAlertIcon,
  runners: ServerIcon,
  support: LifeBuoyIcon,
  settings: SettingsIcon,
  tenants: Building2Icon,
};

function badgeClassName(tone: NonNullable<ShellNavigationItem['badge']>['tone']): string {
  switch (tone) {
    case 'critical':
      return 'bg-status-critical text-status-critical-foreground';
    case 'caution':
      return 'bg-status-caution text-status-caution-foreground';
    case 'neutral':
      return 'bg-muted text-muted-foreground';
  }
}

/** Renders the same authorized, intent-grouped destinations in desktop and mobile shell surfaces. */
export function ShellNavigation({
  items,
  pathname,
  onNavigate,
  compact = false,
}: ShellNavigationProps) {
  const navigationId = useId();

  return (
    <nav
      aria-label="Primary navigation"
      className={cn('grid gap-5', compact && 'gap-4')}
      data-rail-mode={compact ? 'compact' : 'expanded'}
    >
      {navigationGroups.map((group, groupIndex) => {
        const groupItems = items
          .filter((item) => item.group === group.key)
          .sort((left, right) => left.order - right.order || left.label.localeCompare(right.label));
        if (groupItems.length === 0) return null;

        const groupLabelId = `${navigationId}-group-${groupIndex}`;
        return (
          <div className="grid gap-1.5" key={group.key}>
            <p
              id={groupLabelId}
              className={cn(
                'px-3 text-[0.6875rem] font-semibold tracking-[0.12em] text-sidebar-foreground/65 uppercase',
                compact && 'px-2 tracking-[0.08em]',
              )}
            >
              {group.label}
            </p>
            <ul aria-labelledby={groupLabelId} className="grid gap-1">
              {groupItems.map((item, itemIndex) => {
                const isActive = item.activePaths.some((path) =>
                  matchPath({ path, end: true }, pathname),
                );
                const Icon = navigationIcons[item.icon];
                const descriptionId = `${navigationId}-${groupIndex}-${itemIndex}-description`;
                const badgeDescriptionId = `${navigationId}-${groupIndex}-${itemIndex}-badge`;

                return (
                  <li key={`${item.label}-${item.path}`}>
                    <Link
                      aria-current={isActive ? 'page' : undefined}
                      aria-describedby={
                        item.badge ? `${descriptionId} ${badgeDescriptionId}` : descriptionId
                      }
                      aria-label={item.label}
                      className={cn(
                        'grid min-h-12 grid-cols-[1.25rem_minmax(0,1fr)_auto] items-center gap-2 rounded-lg border border-transparent px-3 py-2 text-sm outline-none transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:ring-2 focus-visible:ring-sidebar-ring',
                        compact && 'min-h-11 px-2',
                        isActive &&
                          'border-sidebar-border bg-sidebar-accent text-sidebar-accent-foreground',
                      )}
                      title={compact ? `${item.label} — ${item.description}` : undefined}
                      to={item.path}
                      onClick={onNavigate}
                    >
                      <Icon aria-hidden="true" className="size-4 shrink-0" strokeWidth={1.8} />
                      <span className="min-w-0">
                        <span className="block min-w-0 font-medium [overflow-wrap:anywhere]">
                          {item.label}
                        </span>
                        <span
                          id={descriptionId}
                          className={cn(
                            'mt-0.5 block text-xs leading-4 text-sidebar-foreground/65',
                            compact && 'sr-only',
                          )}
                        >
                          {item.description}
                        </span>
                      </span>
                      {item.badge ? (
                        <>
                          <span
                            aria-hidden="true"
                            className={cn(
                              'min-w-6 rounded-full px-1.5 py-0.5 text-center text-xs font-semibold tabular-nums',
                              badgeClassName(item.badge.tone),
                            )}
                          >
                            {item.badge.label}
                          </span>
                          <span id={badgeDescriptionId} className="sr-only">
                            {item.badge.accessibleLabel}
                          </span>
                        </>
                      ) : null}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        );
      })}
    </nav>
  );
}
