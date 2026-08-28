import type { ReactNode } from 'react';

import { TaskNavigation, type TaskNavigationItem } from './TaskNavigation';

export interface TaskWorkspaceProps {
  readonly navigationLabel: string;
  readonly navigationItems: ReadonlyArray<TaskNavigationItem>;
  readonly children: ReactNode;
}

/**
 * Keeps a multi-task Operate surface in one predictable topology: task
 * navigation first, then the focused work region. The rail collapses to the
 * shared contained horizontal navigation on constrained layouts.
 */
export function TaskWorkspace({ navigationLabel, navigationItems, children }: TaskWorkspaceProps) {
  return (
    <div className="grid min-w-0 gap-5 lg:grid-cols-[15rem_minmax(0,1fr)] lg:items-start">
      <TaskNavigation label={navigationLabel} items={navigationItems} />
      <div className="min-w-0">{children}</div>
    </div>
  );
}
