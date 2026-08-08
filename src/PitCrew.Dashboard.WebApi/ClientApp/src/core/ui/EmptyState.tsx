import type { ReactNode } from 'react';

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';

/** Props for the shared empty-collection state. */
export interface EmptyStateProps {
  readonly title: string;
  readonly description?: ReactNode;
  /** An optional recovery action (e.g. a link to enroll the first node). */
  readonly action?: ReactNode;
  /**
   * Heading level for the title. Defaults to `h3`, assuming the empty
   * condition sits below a route or section heading; pass `h2` when the
   * empty state is itself the primary heading for its region.
   */
  readonly headingLevel?: 'h2' | 'h3';
}

/**
 * Renders a single card explaining that a collection is legitimately empty
 * (as opposed to unavailable or failed) and what would populate it. Reach
 * for this instead of an ad hoc Card only when the empty condition is a
 * distinct, nameable state a screen reader user should be told about — for
 * a merely absent optional field, prefer omitting it or a plain divider.
 * EmptyState always renders its title as an explicit heading level rather
 * than relying on CardTitle's default so the collection's absence is
 * announced as a real landmark in the page outline.
 */
export function EmptyState({ title, description, action, headingLevel = 'h3' }: EmptyStateProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle as={headingLevel}>{title}</CardTitle>
        {description ? <CardDescription>{description}</CardDescription> : null}
      </CardHeader>
      {action ? <CardContent>{action}</CardContent> : null}
    </Card>
  );
}
