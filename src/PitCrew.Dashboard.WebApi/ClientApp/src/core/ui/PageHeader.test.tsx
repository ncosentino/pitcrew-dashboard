import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

import { PageHeader } from '@/core/ui/PageHeader';

describe('PageHeader', () => {
  it('renders the route title as the sole H1', () => {
    render(<PageHeader title="Fleet status" />);

    expect(screen.getByRole('heading', { level: 1, name: 'Fleet status' })).toBeInTheDocument();
  });

  it('renders breadcrumbs, description, and actions alongside the title', () => {
    render(
      <PageHeader
        title="Node node-1 overview"
        breadcrumbs={<nav aria-label="Breadcrumb">Fleet / Node node-1</nav>}
        description="Latest reported evidence for this node."
        actions={<button type="button">Rotate credential</button>}
      />,
    );

    expect(
      screen.getByRole('heading', { level: 1, name: 'Node node-1 overview' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Fleet / Node node-1')).toBeInTheDocument();
    expect(screen.getByText('Latest reported evidence for this node.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Rotate credential' })).toBeInTheDocument();
  });
});
