import { Component, type ErrorInfo, type ReactNode } from 'react';

import { Button } from '@/components/ui/button';

interface FeatureErrorBoundaryProps {
  readonly children: ReactNode;
  readonly featureId: string;
}

interface FeatureErrorBoundaryState {
  readonly failed: boolean;
}

/** Keeps a failed lazy feature from taking down the authenticated shell. */
export class FeatureErrorBoundary extends Component<
  FeatureErrorBoundaryProps,
  FeatureErrorBoundaryState
> {
  public state: FeatureErrorBoundaryState = { failed: false };

  public static getDerivedStateFromError(): FeatureErrorBoundaryState {
    return { failed: true };
  }

  public componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(`Feature "${this.props.featureId}" failed to render.`, error, info);
  }

  public render() {
    if (this.state.failed) {
      return (
        <section
          role="alert"
          className="grid gap-3 rounded-lg border border-red-300 bg-red-50 p-4 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
        >
          <p>The {this.props.featureId} feature could not be displayed.</p>
          <div>
            <Button asChild variant="outline">
              <a href={globalThis.location.href}>Reload page</a>
            </Button>
          </div>
        </section>
      );
    }
    return this.props.children;
  }
}
