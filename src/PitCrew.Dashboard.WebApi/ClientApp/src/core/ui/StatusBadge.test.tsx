import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { StatusBadge } from './StatusBadge';

describe('StatusBadge', () => {
  it.each([
    ['critical', 'critical'],
    ['warning', 'caution'],
    ['unavailable', 'neutral'],
    ['triggered', 'neutral'],
    ['acknowledged', 'caution'],
    ['resolved', 'neutral'],
  ])('maps %s to the %s urgency role', (status, tone) => {
    render(<StatusBadge status={status} />);

    expect(screen.getByText(status)).toHaveAttribute('data-status-tone', tone);
  });
});
