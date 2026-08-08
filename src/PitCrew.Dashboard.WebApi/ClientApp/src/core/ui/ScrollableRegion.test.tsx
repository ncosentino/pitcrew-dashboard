import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { ScrollableRegion } from '@/core/ui/ScrollableRegion';

describe('ScrollableRegion', () => {
  it('exposes an accessible region named from the required label', () => {
    render(
      <ScrollableRegion label="Fleet nodes for the active tenant">
        <p>Wide content</p>
      </ScrollableRegion>,
    );

    expect(
      screen.getByRole('region', { name: 'Fleet nodes for the active tenant' }),
    ).toBeInTheDocument();
  });

  it('owns horizontal overflow while containing its own width', () => {
    render(
      <ScrollableRegion label="Fleet nodes">
        <p>Wide content</p>
      </ScrollableRegion>,
    );

    const region = screen.getByRole('region', { name: 'Fleet nodes' });
    expect(region).toHaveClass('overflow-x-auto');
    expect(region).toHaveClass('overscroll-x-contain');
    expect(region).toHaveClass('min-w-0');
    expect(region).toHaveClass('max-w-full');
  });

  it('merges an additional className with the containment classes', () => {
    render(
      <ScrollableRegion className="rounded-lg border" label="Fleet nodes">
        <p>Wide content</p>
      </ScrollableRegion>,
    );

    const region = screen.getByRole('region', { name: 'Fleet nodes' });
    expect(region).toHaveClass('overflow-x-auto');
    expect(region).toHaveClass('rounded-lg');
    expect(region).toHaveClass('border');
  });

  it('is independently keyboard-focusable so it can be scrolled without a pointer', async () => {
    const user = userEvent.setup();
    render(
      <ScrollableRegion label="Fleet nodes">
        <p>Wide content</p>
      </ScrollableRegion>,
    );

    const region = screen.getByRole('region', { name: 'Fleet nodes' });
    expect(region).toHaveAttribute('tabIndex', '0');

    await user.tab();

    expect(region).toHaveFocus();
  });
});
