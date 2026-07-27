import { type ReactNode } from 'react';

interface ProfileEvidenceDisclosureProps {
  readonly title: string;
  readonly description: string;
  readonly summary: ReactNode;
  readonly testId: string;
  readonly children: ReactNode;
}

/** Groups secondary profile evidence behind a native, accessible disclosure. */
export function ProfileEvidenceDisclosure({
  title,
  description,
  summary,
  testId,
  children,
}: ProfileEvidenceDisclosureProps) {
  return (
    <details className="group border-b bg-muted/5" data-testid={testId}>
      <summary className="flex cursor-pointer list-none flex-col items-stretch gap-2 px-4 py-3 sm:flex-row sm:items-center sm:justify-between sm:gap-3 [&::-webkit-details-marker]:hidden">
        <div className="min-w-0">
          <h2 className="font-semibold">{title}</h2>
          <p className="text-xs text-muted-foreground">{description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground sm:justify-end">
          {summary}
          <span className="group-open:hidden">Show details</span>
          <span className="hidden group-open:inline">Hide details</span>
        </div>
      </summary>
      <div className="border-t px-4 py-4">{children}</div>
    </details>
  );
}
