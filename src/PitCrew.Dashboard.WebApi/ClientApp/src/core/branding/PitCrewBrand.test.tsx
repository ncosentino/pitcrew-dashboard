import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';

import { PitCrewBrand } from './PitCrewBrand';

describe('PitCrewBrand', () => {
  it('renders the canonical logo for the hero lockup', () => {
    render(<PitCrewBrand variant="hero" />);

    expect(screen.getByRole('img', { name: 'PitCrew' })).toHaveAttribute(
      'src',
      '/pitcrew-logo.png',
    );
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
  });

  it('renders the compact dashboard wordmark', () => {
    render(<PitCrewBrand variant="compact" />);

    expect(screen.getByText('Pit')).toBeInTheDocument();
    expect(screen.getByText('Crew')).toBeInTheDocument();
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
  });
});
