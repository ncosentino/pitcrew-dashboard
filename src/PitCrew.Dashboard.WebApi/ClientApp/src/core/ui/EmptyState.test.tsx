import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

import { EmptyState } from '@/core/ui/EmptyState';

describe('EmptyState', () => {
  it('renders the title as an h3 heading by default and the description as supporting text', () => {
    render(
      <EmptyState
        description="Create a one-time code, configure it on a connector, and start the connector."
        title="No servers enrolled"
      />,
    );

    expect(
      screen.getByRole('heading', { level: 3, name: 'No servers enrolled' }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'Create a one-time code, configure it on a connector, and start the connector.',
      ),
    ).toBeInTheDocument();
  });

  it('renders the title at an explicitly requested heading level', () => {
    render(<EmptyState headingLevel="h2" title="No matching nodes" />);

    expect(
      screen.getByRole('heading', { level: 2, name: 'No matching nodes' }),
    ).toBeInTheDocument();
  });

  it('renders an optional recovery action', () => {
    render(
      <EmptyState
        action={<button type="button">Create a one-time code</button>}
        title="No servers enrolled"
      />,
    );

    expect(screen.getByRole('button', { name: 'Create a one-time code' })).toBeInTheDocument();
  });

  it('omits the description and action when not provided', () => {
    render(<EmptyState title="No matching nodes" />);

    expect(screen.getByRole('heading', { name: 'No matching nodes' })).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
