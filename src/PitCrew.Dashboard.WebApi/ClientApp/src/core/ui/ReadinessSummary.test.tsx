import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ReadinessSummary } from './ReadinessSummary';

describe('ReadinessSummary', () => {
  it('groups readiness evidence under one named region', () => {
    render(
      <ReadinessSummary
        title="Support readiness"
        description="Current evidence"
        status={<span>Ready</span>}
        items={[
          { label: 'Active nodes', value: '2', detail: 'One reported recently' },
          { label: 'Active sessions', value: '1' },
        ]}
      />,
    );

    expect(screen.getByRole('region', { name: 'Support readiness' })).toBeVisible();
    expect(screen.getByText('Ready')).toBeVisible();
    expect(screen.getByText('One reported recently')).toBeVisible();
  });

  it('generates collision-safe labels for separate readiness regions', () => {
    render(
      <>
        <ReadinessSummary title="First readiness" description="First" items={[]} />
        <ReadinessSummary title="Second readiness" description="Second" items={[]} />
      </>,
    );

    const first = screen.getByRole('region', { name: 'First readiness' });
    const second = screen.getByRole('region', { name: 'Second readiness' });
    expect(first.getAttribute('aria-labelledby')).not.toBe(second.getAttribute('aria-labelledby'));
  });

  it('supports a compact two-column narrow layout', () => {
    render(
      <ReadinessSummary
        title="Incident readiness"
        description="Current incident evidence"
        narrowColumns={2}
        items={[]}
      />,
    );

    expect(
      screen.getByRole('region', { name: 'Incident readiness' }).querySelector('dl'),
    ).toHaveClass('grid-cols-2');
  });
});
