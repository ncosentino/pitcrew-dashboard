import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider } from '@/core/auth';
import type { FeatureManifest } from '@/core/features/FeatureManifest';
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

  it('retries a failed session bootstrap without reloading the page', async () => {
    let sessionLoads = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/session')) {
        sessionLoads++;
        return sessionLoads === 1
          ? jsonResponse({ error: { code: 'unavailable', message: 'Session outage' } }, 503)
          : jsonResponse(ownerSession);
      }
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-18T16:00:00+00:00', nodes: [] });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderRoute('/');
    const user = userEvent.setup();

    expect(await screen.findByText('Dashboard session is unavailable')).toBeInTheDocument();
    expect(screen.getByText('Session outage')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Retry session' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Fleet status' }),
    ).toBeInTheDocument();
    expect(sessionLoads).toBe(2);
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

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Create tenant' }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/admin/tenants');
  });

  it('renders explicit no-access state for a tenantless user', async () => {
    mockSession({ ...ownerSession, tenants: [] });

    renderRoute('/');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'No tenant access' }),
    ).toBeInTheDocument();
  });

  it('renders a distinct not-found route instead of a tenant-access error', async () => {
    mockSession(ownerSession);

    const router = renderRoute('/tenants/local/unknown-page');

    await waitFor(() => expect(router.state.location.pathname).toBe('/tenants/local/unknown-page'));
    expect(
      await screen.findByText('This dashboard route does not exist or is no longer available.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument();
    expect(screen.queryByText('No tenant access')).not.toBeInTheDocument();
  });

  it('contains unexpected route render failures in a dedicated error page', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const brokenFeature: FeatureManifest = {
      id: 'broken',
      routes: [
        {
          path: 'broken',
          element: <BrokenPage />,
        },
      ],
    };
    mockSession(ownerSession);
    const router = createTestRouter([...features, brokenFeature], ['/broken']);

    render(
      <SessionProvider>
        <RouterProvider router={router} />
      </SessionProvider>,
    );

    expect(await screen.findByText(/Page could not be displayed/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Return to the dashboard' })).toHaveAttribute(
      'href',
      '/',
    );
    expect(consoleError).toHaveBeenCalled();
  });

  it('does not substitute another tenant for an invalid tenant ID', async () => {
    mockSession(ownerSession);

    renderRoute('/tenants/not-authorized/fleet');

    expect(await screen.findByText('Tenant unavailable')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Fleet status' })).not.toBeInTheDocument();
  });

  it.each([
    ['viewer', '/tenants/local/settings/general', 'owner'],
    ['viewer', '/tenants/local/settings/access', 'owner'],
    ['administrator', '/tenants/local/settings/general', 'owner'],
    ['administrator', '/tenants/local/settings/access', 'owner'],
    ['viewer', '/tenants/local/settings/enrollment', 'administrator'],
  ] as const)('rejects a %s from %s', async (role, path, requiredRole) => {
    mockSession({
      ...ownerSession,
      tenants: [{ ...ownerSession.tenants[0], role }],
    });

    renderRoute(path);

    expect(await screen.findByText('Insufficient tenant role')).toBeInTheDocument();
    expect(screen.getByText(new RegExp(`requires the ${requiredRole} role`))).toBeInTheDocument();
  });

  it('rejects a non-system-administrator from tenant creation', async () => {
    mockSession(ownerSession);
    const router = renderRoute('/admin/tenants');

    await waitFor(() => expect(router.state.location.pathname).toBe('/no-access'));
    expect(
      await screen.findByRole('heading', { level: 1, name: 'No tenant access' }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/no-access');
    expect(screen.queryByText('Create tenant')).not.toBeInTheDocument();
  });

  it('renders owner settings-local navigation and access administration', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/members') || url.endsWith('/available-users')) return jsonResponse([]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderRoute('/tenants/local/settings/access');

    expect(await screen.findByText('Tenant membership')).toBeInTheDocument();
    const navigation = screen.getByRole('navigation', { name: 'Tenant settings' });
    expect(navigation).toHaveTextContent('General');
    expect(navigation).toHaveTextContent('Access');
    expect(navigation).toHaveTextContent('Enrollment');
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
    expect(screen.getByText(/Expires/)).toBeInTheDocument();
    expect(screen.getByText(/not stored in recoverable form/)).toBeInTheDocument();
    expect(screen.getByText(/PitCrew__Connector__EnrollmentCode/)).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Tenant settings' })).toHaveTextContent(
      'Enrollment',
    );
    expect(screen.getByRole('navigation', { name: 'Tenant settings' })).not.toHaveTextContent(
      'Access',
    );
  });

  it('refreshes the session and routes to the new tenant fleet after creation', async () => {
    const systemAdministratorSession = { ...ownerSession, isSystemAdministrator: true };
    const createdSession = {
      ...systemAdministratorSession,
      tenants: [
        ...ownerSession.tenants,
        { tenantId: 'new-tenant', displayName: 'New tenant', role: 'owner' as const },
      ],
    };
    let sessionLoads = 0;
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/api/session')) {
        sessionLoads++;
        return jsonResponse(sessionLoads === 1 ? systemAdministratorSession : createdSession);
      }
      if (url.endsWith('/api/tenants') && init?.method === 'POST') {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-18T16:00:00+00:00', nodes: [] });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    const router = renderRoute('/admin/tenants');
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText('Tenant ID'), 'new-tenant');
    await user.type(screen.getByLabelText('Tenant display name'), 'New tenant');
    await user.click(screen.getByRole('button', { name: 'Create tenant' }));

    expect(await screen.findByText('No servers enrolled')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/tenants/new-tenant/fleet');
    expect(sessionLoads).toBe(2);
    const creationCall = fetchMock.mock.calls.find(
      ([input, init]) => String(input).endsWith('/api/tenants') && init?.method === 'POST',
    );
    expect(new Headers(creationCall?.[1]?.headers).get('X-PitCrew-Antiforgery')).toBe(
      'test-antiforgery-token',
    );
    expect(JSON.parse(String(creationCall?.[1]?.body))).toEqual({
      tenantId: 'new-tenant',
      displayName: 'New tenant',
    });
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

  it('declares node detail and profile deep-link routes', async () => {
    mockSession(ownerSession);
    const router = renderRoute('/tenants/local/nodes/node-1');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Node node-1 overview' }),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Breadcrumb' })).getByRole('link', {
        name: 'Fleet',
      }),
    ).toHaveAttribute('href', '/tenants/local/fleet');
    expect(
      screen.getByText('Node node-1', { selector: '[aria-current="page"]' }),
    ).toBeInTheDocument();
    expect(await screen.findByText('Node not found')).toBeInTheDocument();

    await act(async () => {
      await router.navigate('/tenants/local/nodes/node-1/history');
    });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Node node-1 history' }),
    ).toBeInTheDocument();
    expect(screen.getByText('History', { selector: '[aria-current="page"]' })).toBeInTheDocument();

    await act(async () => {
      await router.navigate('/tenants/local/nodes/node-1/profiles/build');
    });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Profile build overview' }),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Breadcrumb' })).getByRole('link', {
        name: 'Node node-1',
      }),
    ).toHaveAttribute('href', '/tenants/local/nodes/node-1');

    await act(async () => {
      await router.navigate('/tenants/local/nodes/node-1/profiles/build/diagnostics');
    });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Profile build diagnostics' }),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Diagnostics', { selector: '[aria-current="page"]' }),
    ).toBeInTheDocument();
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

  it('moves focus to the main content after route navigation', async () => {
    mockSession(ownerSession);
    const router = renderRoute('/tenants/local/fleet');
    await screen.findByRole('heading', { level: 2, name: 'Fleet status' });
    const main = document.querySelector<HTMLElement>('#main-content');
    expect(main).not.toBeNull();
    await waitFor(() => expect(main).toHaveFocus());

    await act(async () => {
      await router.navigate('/tenants/local/runners');
    });

    await waitFor(() => expect(router.state.location.pathname).toBe('/tenants/local/runners'));
    await screen.findByRole('heading', { level: 1, name: 'Runners' });
    await waitFor(() => expect(main).toHaveFocus());
  });

  it('signs out with the session antiforgery token', async () => {
    mockSession(ownerSession);
    renderRoute('/tenants/local/fleet');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    expect(logoutMock).toHaveBeenCalledWith('test-antiforgery-token');
  });

  function BrokenPage(): never {
    throw new Error('Simulated route render failure.');
  }

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
    ).toEqual(['Fleet', 'Incidents', 'Runners', 'Settings', 'Enrollment', 'Diagnostics']);
    expect(within(dialog).getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
    await user.click(within(dialog).getByRole('link', { name: 'Enrollment' }));

    expect(router.state.location.pathname).toBe('/tenants/local/settings/enrollment');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(trigger).toHaveFocus();
  });

  it('reuses fleet polling across fleet routes and stops it on settings routes', async () => {
    let fleetSignal: AbortSignal | undefined;
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return Promise.resolve(jsonResponse(ownerSession));
      if (url.endsWith('/fleet/v1/nodes')) {
        fleetSignal = init?.signal as AbortSignal;
        return new Promise<Response>(() => undefined);
      }
      return Promise.resolve(
        jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404),
      );
    });
    const router = renderRoute('/tenants/local/fleet');

    await screen.findByRole('heading', { level: 2, name: 'Fleet status' });
    await waitFor(() => expect(fleetSignal).toBeDefined());
    await act(async () => {
      await router.navigate('/tenants/local/nodes/node-1');
    });
    expect(await screen.findByText('Loading node…')).toBeInTheDocument();
    expect(fleetSignal?.aborted).toBe(false);

    await act(async () => {
      await router.navigate('/tenants/local/settings/general');
    });
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Tenant settings' }),
    ).toBeInTheDocument();
    expect(fleetSignal?.aborted).toBe(true);
  });
});
