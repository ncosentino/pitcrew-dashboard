import { type ReactNode } from 'react';

interface ProfileEvidencePanelProps {
  readonly title: string;
  readonly description: string;
  readonly summary: ReactNode;
  readonly testId: string;
  readonly children: ReactNode;
}

/** Presents one focused block of profile evidence within its route-level destination. */
export function ProfileEvidencePanel({
  title,
  description,
  summary,
  testId,
  children,
}: ProfileEvidencePanelProps) {
  return (
    <section className="overflow-hidden rounded-lg border bg-card shadow-sm" data-testid={testId}>
      <div className="flex flex-col items-stretch gap-2 px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:gap-3">
        <div className="min-w-0">
          <h2 className="font-semibold">{title}</h2>
          <p className="text-xs text-muted-foreground">{description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground sm:justify-end">
          {summary}
        </div>
      </div>
      <div className="border-t px-4 py-4">{children}</div>
    </section>
  );
}
