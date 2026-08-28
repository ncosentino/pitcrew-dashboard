import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { ShellNavigation, type ShellNavigationItem } from './ShellNavigation';

const items: ReadonlyArray<ShellNavigationItem> = [
  {
    label: 'Settings',
    description: 'Tenant identity, access, and credentials',
    path: '/tenants/local/settings',
    group: 'configure',
    order: 10,
    icon: 'settings',
    activePaths: ['/tenants/local/settings/*'],
  },
  {
    label: 'Incidents',
    description: 'Active exceptions and bounded history',
    path: '/tenants/local/incidents',
    group: 'monitor',
    order: 20,
    icon: 'incidents',
    activePaths: ['/tenants/local/incidents'],
    badge: {
      label: '3',
      accessibleLabel: '3 active incidents; highest severity critical',
      tone: 'critical',
    },
  },
  {
    label: 'Fleet',
    description: 'Readiness, nodes, and profile health',
    path: '/tenants/local/fleet',
    group: 'monitor',
    order: 10,
    icon: 'fleet',
    activePaths: ['/tenants/local/fleet'],
  },
  {
    label: 'Runners',
    description: 'Runner slots and current job correlation',
    path: '/tenants/local/runners',
    group: 'operate',
    order: 10,
    icon: 'runners',
    activePaths: ['/tenants/local/runners'],
  },
];

function renderNavigation(compact = false) {
  const onNavigate = vi.fn();
  render(
    <MemoryRouter>
      <ShellNavigation
        compact={compact}
        items={items}
        pathname="/tenants/local/incidents"
        onNavigate={onNavigate}
      />
    </MemoryRouter>,
  );
  return onNavigate;
}

describe('ShellNavigation', () => {
  it('groups and orders destinations by operator intent', () => {
    renderNavigation();

    const navigation = screen.getByRole('navigation', { name: 'Primary navigation' });
    expect(
      within(within(navigation).getByRole('list', { name: 'Monitor' }))
        .getAllByRole('link')
        .map((link) => link.getAttribute('aria-label')),
    ).toEqual(['Fleet', 'Incidents']);
    expect(
      within(within(navigation).getByRole('list', { name: 'Operate' }))
        .getAllByRole('link')
        .map((link) => link.getAttribute('aria-label')),
    ).toEqual(['Runners']);
    expect(
      within(within(navigation).getByRole('list', { name: 'Configure' }))
        .getAllByRole('link')
        .map((link) => link.getAttribute('aria-label')),
    ).toEqual(['Settings']);

    expect(screen.getByRole('link', { name: 'Incidents' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'Fleet' })).toHaveAccessibleDescription(
      'Readiness, nodes, and profile health',
    );
    expect(screen.getByRole('link', { name: 'Incidents' })).toHaveAccessibleDescription(
      'Active exceptions and bounded history 3 active incidents; highest severity critical',
    );
  });

  it('keeps labels, descriptions, and attention state available in compact mode', async () => {
    const onNavigate = renderNavigation(true);
    const user = userEvent.setup();

    expect(screen.getByRole('navigation', { name: 'Primary navigation' })).toHaveAttribute(
      'data-rail-mode',
      'compact',
    );
    expect(screen.getByText('Runner slots and current job correlation')).toHaveClass('sr-only');
    expect(screen.getByRole('link', { name: 'Runners' })).toHaveAttribute(
      'title',
      'Runners — Runner slots and current job correlation',
    );
    expect(screen.getByText('3')).toBeVisible();

    await user.click(screen.getByRole('link', { name: 'Fleet' }));
    expect(onNavigate).toHaveBeenCalledOnce();
  });
});
