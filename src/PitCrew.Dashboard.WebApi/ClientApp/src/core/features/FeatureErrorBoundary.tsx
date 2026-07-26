import { Component, type ErrorInfo, type ReactNode } from 'react';

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
        <section role="alert" className="rounded-lg border border-red-300 p-4 text-red-900">
          This feature could not be loaded. Refresh the page to try again.
        </section>
      );
    }
    return this.props.children;
  }
}
