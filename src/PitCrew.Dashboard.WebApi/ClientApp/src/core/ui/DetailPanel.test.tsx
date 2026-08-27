import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { DetailPanel } from './DetailPanel';

describe('DetailPanel', () => {
  it('names the selected investigation and contains its evidence', () => {
    render(
      <DetailPanel title="Connector offline" status={<span>Rejected</span>}>
        <p>Evidence unavailable</p>
      </DetailPanel>,
    );

    expect(screen.getByRole('region', { name: 'Connector offline' })).toBeVisible();
    expect(screen.getByText('Rejected')).toBeVisible();
    expect(screen.getByText('Evidence unavailable')).toBeVisible();
  });
});
