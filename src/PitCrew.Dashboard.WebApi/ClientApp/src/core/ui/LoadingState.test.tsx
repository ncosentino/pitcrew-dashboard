import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

import { LoadingState } from '@/core/ui/LoadingState';

describe('LoadingState', () => {
  it('announces the loading label through a status role', () => {
    render(<LoadingState label="Loading fleet status…" />);

    expect(screen.getByRole('status')).toHaveTextContent('Loading fleet status…');
  });
});
