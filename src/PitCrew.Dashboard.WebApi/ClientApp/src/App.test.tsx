import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider } from '@/core/auth';
import { createTestRouter } from '@/core/routing/createAppRouter';
import { features } from '@/features.registry';

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

    expect(await screen.findByText('No tenant access')).toBeInTheDocument();
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

    expect(await screen.findByText('No tenant access')).toBeInTheDocument();
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
    await user.click(screen.getByRole('button', { name: 'Create' }));

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

  it('declares node and profile deep-link placeholders', async () => {
    mockSession(ownerSession);
    const router = renderRoute('/tenants/local/nodes/node-1');

    expect(await screen.findByText('Node node-1')).toBeInTheDocument();

    await act(async () => {
      await router.navigate('/tenants/local/nodes/node-1/profiles/build');
    });

    expect(await screen.findByText('Profile build')).toBeInTheDocument();
  });
});
