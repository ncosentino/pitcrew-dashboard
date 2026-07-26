import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider } from '@/core/auth';
import { createTestRouter } from '@/core/routing/createAppRouter';
import { features } from '@/features.registry';

const logoutMock = vi.hoisted(() => vi.fn(() => new Promise<void>(() => undefined)));

vi.mock('@/core/auth', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/core/auth')>();
  return { ...actual, logout: logoutMock };
});

const ownerSession = {
  user: {
    githubUserId: '123',
    githubLogin: 'operator',
    displayName: 'Operator',
    avatarUrl: null,
  },
  isSystemAdministrator: false,
  tenants: [{ tenantId: 'local', displayName: 'Local', role: 'owner' as const }],
  antiforgeryToken: 'test-antiforgery-token',
};

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function mockSession(session: unknown, status = 200) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = String(input);
    if (url.endsWith('/api/session')) return jsonResponse(session, status);
    if (url.endsWith('/fleet/v1/nodes')) {
      return jsonResponse({ generatedAt: '2026-07-18T16:00:00+00:00', nodes: [] });
    }
    return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
  });
}

function renderRoute(path: string) {
  const router = createTestRouter(features, [path]);
  render(
    <SessionProvider>
      <RouterProvider router={router} />
    </SessionProvider>,
  );
  return router;
}

describe('authenticated routing', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    logoutMock.mockClear();
  });

  it('preserves a deep local path and query in the login URL', async () => {
    mockSession({ error: { code: 'unauthorized', message: 'Authentication required' } }, 401);

    renderRoute('/tenants/local/nodes/node-1?tab=activity');

    expect(await screen.findByText('Sign in to PitCrew Dashboard')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sign in with GitHub' })).toHaveAttribute(
      'href',
      '/auth/login?returnUrl=%2Ftenants%2Flocal%2Fnodes%2Fnode-1%3Ftab%3Dactivity',
    );
  });

  it('redirects the root to the first authorized tenant and loads the session once', async () => {
    const fetchMock = mockSession(ownerSession);
    const router = renderRoute('/');

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Fleet status' }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/tenants/local/fleet');
    expect(
      fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/api/session')),
    ).toHaveLength(1);
  });

  it('redirects a tenantless system administrator to tenant creation', async () => {
    mockSession({ ...ownerSession, isSystemAdministrator: true, tenants: [] });
    const router = renderRoute('/');

    expect(await screen.findByText('Create tenant')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/admin/tenants');
  });

  it('renders explicit no-access state for a tenantless user', async () => {
    mockSession({ ...ownerSession, tenants: [] });

    renderRoute('/');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'No tenant access' }),
    ).toBeInTheDocument();
  });

  it('does not substitute another tenant for an invalid tenant ID', async () => {
    mockSession(ownerSession);

    renderRoute('/tenants/not-authorized/fleet');

    expect(await screen.findByText('Tenant unavailable')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Fleet status' })).not.toBeInTheDocument();
  });

  it('enforces owner and administrator settings guards', async () => {
    mockSession({
      ...ownerSession,
      tenants: [{ ...ownerSession.tenants[0], role: 'administrator' }],
    });

    renderRoute('/tenants/local/settings/general');

    expect(await screen.findByText('Insufficient tenant role')).toBeInTheDocument();
    expect(screen.getByText(/requires the owner role/)).toBeInTheDocument();
  });

  it('allows an administrator to reach enrollment settings', async () => {
    const administratorSession = {
      ...ownerSession,
      tenants: [{ ...ownerSession.tenants[0], role: 'administrator' as const }],
    };
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(administratorSession);
      if (url.endsWith('/fleet/v1/enrollment-codes') && init?.method === 'POST') {
        return jsonResponse({
          enrollmentCodeId: '0bd3014f-81a3-44c6-9660-93bfc5e55f6f',
          code: 'one-time-code',
          expiresAt: '2026-07-18T16:05:00+00:00',
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderRoute('/tenants/local/settings/enrollment');
    const user = userEvent.setup();

    expect(await screen.findByText('Enroll a connector')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Create one-time code' }));
    expect(await screen.findByText('one-time-code')).toBeInTheDocument();
  });

  it('refreshes the tenant selector after an owner renames the tenant', async () => {
    let sessionLoads = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/api/session')) {
        sessionLoads++;
        return jsonResponse(
          sessionLoads === 1
            ? ownerSession
            : {
                ...ownerSession,
                tenants: [{ ...ownerSession.tenants[0], displayName: 'Renamed tenant' }],
              },
        );
      }
      if (url.endsWith('/api/tenants/local') && init?.method === 'PUT') {
        return new Response(null, { status: 204 });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderRoute('/tenants/local/settings/general');
    const user = userEvent.setup();

    const input = await screen.findByLabelText('Tenant display name');
    await user.clear(input);
    await user.type(input, 'Renamed tenant');
    await user.click(screen.getByRole('button', { name: 'Rename tenant' }));

    expect(
      await screen.findByRole('option', { name: 'Renamed tenant · owner' }),
    ).toBeInTheDocument();
    expect(sessionLoads).toBe(2);
  });

  it('switches tenants to the selected fleet route', async () => {
    mockSession({
      ...ownerSession,
      tenants: [
        ownerSession.tenants[0],
        { tenantId: 'remote', displayName: 'Remote', role: 'viewer' },
      ],
    });
    const router = renderRoute('/tenants/local/fleet');
    const user = userEvent.setup();

    await user.selectOptions(await screen.findByLabelText('Tenant'), 'remote');

    expect(router.state.location.pathname).toBe('/tenants/remote/fleet');
    expect(await screen.findByRole('option', { name: 'Remote · viewer' })).toBeInTheDocument();
  });

  it('declares node and profile deep-link placeholders', async () => {
    mockSession(ownerSession);
    const router = renderRoute('/tenants/local/nodes/node-1');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Node node-1' }),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Breadcrumb' })).getByRole('link', {
        name: 'Fleet',
      }),
    ).toHaveAttribute('href', '/tenants/local/fleet');
    expect(
      screen.getByText('Node node-1', { selector: '[aria-current="page"]' }),
    ).toBeInTheDocument();

    await act(async () => {
      await router.navigate('/tenants/local/nodes/node-1/profiles/build');
    });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Profile build' }),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Breadcrumb' })).getByRole('link', {
        name: 'Node node-1',
      }),
    ).toHaveAttribute('href', '/tenants/local/nodes/node-1');
  });

  it.each([
    {
      role: 'viewer',
      visible: ['Fleet'],
      hidden: ['Settings', 'Enrollment'],
    },
    {
      role: 'administrator',
      visible: ['Fleet', 'Enrollment'],
      hidden: ['Settings'],
    },
    {
      role: 'owner',
      visible: ['Fleet', 'Settings', 'Enrollment'],
      hidden: [],
    },
  ] as const)('shows only $role tenant navigation', async ({ role, visible, hidden }) => {
    mockSession({
      ...ownerSession,
      tenants: [{ ...ownerSession.tenants[0], role }],
    });
    renderRoute('/tenants/local/fleet');

    const navigation = await screen.findByRole('navigation', { name: 'Primary navigation' });
    for (const label of visible) {
      expect(within(navigation).getByRole('link', { name: label })).toBeInTheDocument();
    }
    for (const label of hidden) {
      expect(within(navigation).queryByRole('link', { name: label })).not.toBeInTheDocument();
    }
  });

  it('marks nested fleet routes active and exposes system administration from its manifest', async () => {
    mockSession({ ...ownerSession, isSystemAdministrator: true });
    renderRoute('/tenants/local/nodes/node-1');

    const navigation = await screen.findByRole('navigation', { name: 'Primary navigation' });
    expect(within(navigation).getByRole('link', { name: 'Fleet' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(
      within(navigation).getByRole('link', { name: 'Tenant administration' }),
    ).toBeInTheDocument();
  });

  it('renders settings and administration breadcrumbs with their route headings', async () => {
    mockSession({ ...ownerSession, isSystemAdministrator: true });
    const router = renderRoute('/tenants/local/settings/access');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Tenant access' }),
    ).toBeInTheDocument();
    const settingsBreadcrumbs = screen.getByRole('navigation', { name: 'Breadcrumb' });
    expect(within(settingsBreadcrumbs).getByRole('link', { name: 'Settings' })).toHaveAttribute(
      'href',
      '/tenants/local/settings/general',
    );
    expect(within(settingsBreadcrumbs).getByText('Access')).toHaveAttribute('aria-current', 'page');
    expect(
      within(screen.getByRole('navigation', { name: 'Primary navigation' })).getByRole('link', {
        name: 'Settings',
      }),
    ).toHaveAttribute('aria-current', 'page');

    await act(async () => {
      await router.navigate('/admin/tenants');
    });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Tenant administration' }),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Breadcrumb' })).getByText(
        'Tenant administration',
      ),
    ).toHaveAttribute('aria-current', 'page');
    expect(
      within(screen.getByRole('navigation', { name: 'Primary navigation' })).getByRole('link', {
        name: 'Fleet',
      }),
    ).toBeInTheDocument();
  });

  it('renders accessible shell landmarks and a skip target', async () => {
    mockSession(ownerSession);
    renderRoute('/tenants/local/fleet');

    const skipLink = await screen.findByRole('link', { name: 'Skip to content' });
    expect(skipLink).toHaveAttribute('href', '#main-content');
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main-content');
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
  });

  it('signs out with the session antiforgery token', async () => {
    mockSession(ownerSession);
    renderRoute('/tenants/local/fleet');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    expect(logoutMock).toHaveBeenCalledWith('test-antiforgery-token');
  });

  it('closes mobile navigation after keyboard navigation and restores trigger focus', async () => {
    mockSession(ownerSession);
    const router = renderRoute('/tenants/local/fleet');
    const user = userEvent.setup();
    const trigger = await screen.findByRole('button', { name: 'Open navigation' });

    trigger.focus();
    await user.keyboard('{Enter}');
    const dialog = await screen.findByRole('dialog', { name: 'Navigation' });
    expect(
      within(dialog)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['Fleet', 'Settings', 'Enrollment']);
    expect(within(dialog).getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
    await user.click(within(dialog).getByRole('link', { name: 'Enrollment' }));

    expect(router.state.location.pathname).toBe('/tenants/local/settings/enrollment');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(trigger).toHaveFocus();
  });
});
