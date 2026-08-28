import { useId, type ReactNode } from 'react';

interface SettingsTaskProps {
  readonly title: string;
  readonly description: ReactNode;
  readonly children: ReactNode;
}

/** Gives one settings task a stable heading without adding another card layer. */
export function SettingsTask({ title, description, children }: SettingsTaskProps) {
  const titleId = useId();

  return (
    <section aria-labelledby={titleId} className="grid min-w-0 gap-4">
      <div>
        <h2 id={titleId} className="[overflow-wrap:anywhere] text-lg font-semibold">
          {title}
        </h2>
        <div className="mt-1 max-w-[72ch] [overflow-wrap:anywhere] text-sm text-muted-foreground">
          {description}
        </div>
      </div>
      {children}
    </section>
  );
}
