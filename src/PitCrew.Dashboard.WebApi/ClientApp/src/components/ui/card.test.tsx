import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

import { Card, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';

describe('CardTitle', () => {
  it('renders as a non-heading div by default, preserving every existing call site', () => {
    render(
      <Card>
        <CardHeader>
          <CardTitle>Node identity</CardTitle>
          <CardDescription>Stable identifiers for this node.</CardDescription>
        </CardHeader>
      </Card>,
    );

    expect(screen.queryByRole('heading')).not.toBeInTheDocument();
    expect(screen.getByText('Node identity').tagName).toBe('DIV');
  });

  it('renders as the requested heading level when a card stands in for a route heading', () => {
    render(<CardTitle as="h1">Sign in to PitCrew Dashboard</CardTitle>);

    expect(
      screen.getByRole('heading', { level: 1, name: 'Sign in to PitCrew Dashboard' }),
    ).toBeInTheDocument();
  });

  it('renders at any explicitly requested heading level', () => {
    render(<CardTitle as="h2">Fleet status</CardTitle>);

    expect(screen.getByRole('heading', { level: 2, name: 'Fleet status' })).toBeInTheDocument();
  });

  it('renders as a non-heading element when the slot holds a value rather than a title', () => {
    render(<CardTitle as="p">42</CardTitle>);

    expect(screen.queryByRole('heading')).not.toBeInTheDocument();
    expect(screen.getByText('42').tagName).toBe('P');
  });
});
