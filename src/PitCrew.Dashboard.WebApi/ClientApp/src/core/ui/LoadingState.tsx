/** Props for the shared in-progress loading indicator. */
export interface LoadingStateProps {
  readonly label: string;
}

/**
 * Renders a single `role="status"` line announcing that data is loading.
 * Prefer this over a bespoke paragraph so every loading notice is announced
 * to assistive technology consistently; it is not a replacement for
 * skeletons or spinners inside an already-rendered layout.
 */
export function LoadingState({ label }: LoadingStateProps) {
  return (
    <p className="text-muted-foreground" role="status">
      {label}
    </p>
  );
}
