import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

import { StateBanner } from '@/core/ui/StateBanner';

describe('StateBanner', () => {
  it('defaults critical tone to an alert role', () => {
    render(<StateBanner tone="critical">Fleet refresh failed</StateBanner>);

    expect(screen.getByRole('alert')).toHaveTextContent('Fleet refresh failed');
  });

  it('defaults caution and positive tones to a status role', () => {
    const { rerender } = render(<StateBanner tone="caution">Showing stale data</StateBanner>);
    expect(screen.getByRole('status')).toHaveTextContent('Showing stale data');

    rerender(<StateBanner tone="positive">Recovered</StateBanner>);
    expect(screen.getByRole('status')).toHaveTextContent('Recovered');
  });

  it('allows the caller to override the default role', () => {
    render(
      <StateBanner role="alert" tone="caution">
        Showing stale fleet data. Fleet refresh failed.
      </StateBanner>,
    );

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Showing stale fleet data. Fleet refresh failed.',
    );
  });
});
