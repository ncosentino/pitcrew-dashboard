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
});
