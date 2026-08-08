import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

import { SectionNavigation } from '@/core/ui/SectionNavigation';

const items = [
  { label: 'Overview', path: '/nodes/node-1' },
  { label: 'History', path: '/nodes/node-1/history' },
  { label: 'Administration', path: '/nodes/node-1/administration' },
];

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route
          element={<SectionNavigation label="Alpha navigation" items={items} />}
          path="/nodes/node-1/*"
        />
      </Routes>
    </MemoryRouter>,
  );
}

describe('SectionNavigation', () => {
  it('exposes a labeled navigation region with a link per destination', () => {
    renderAt('/nodes/node-1');

    const navigation = screen.getByRole('navigation', { name: 'Alpha navigation' });
    expect(within(navigation).getByRole('link', { name: 'Overview' })).toBeInTheDocument();
    expect(within(navigation).getByRole('link', { name: 'History' })).toBeInTheDocument();
    expect(within(navigation).getByRole('link', { name: 'Administration' })).toBeInTheDocument();
  });

  it('marks only the current destination with aria-current', () => {
    renderAt('/nodes/node-1/history');

    const navigation = screen.getByRole('navigation', { name: 'Alpha navigation' });
    expect(within(navigation).getByRole('link', { name: 'History' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(within(navigation).getByRole('link', { name: 'Overview' })).not.toHaveAttribute(
      'aria-current',
    );
  });

  it('contains horizontal overflow inside its own scroll region', () => {
    renderAt('/nodes/node-1');

    const navigation = screen.getByRole('navigation', { name: 'Alpha navigation' });
    expect(navigation).toHaveClass('overflow-x-auto');
    expect(navigation).toHaveClass('overscroll-x-contain');
  });

  it('suppresses vertical overflow and hides the scrollbar while remaining scrollable', () => {
    renderAt('/nodes/node-1');

    const navigation = screen.getByRole('navigation', { name: 'Alpha navigation' });
    expect(navigation).toHaveClass('overflow-y-hidden');
    expect(navigation.className).toContain('[scrollbar-width:none]');
    expect(navigation.className).toContain('[&::-webkit-scrollbar]:hidden');
  });
});
