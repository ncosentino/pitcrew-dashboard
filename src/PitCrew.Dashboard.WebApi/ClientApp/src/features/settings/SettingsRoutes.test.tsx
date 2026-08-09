import { render, screen } from '@testing-library/react';
import { RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider } from '@/core/auth';
import { createTestRouter } from '@/core/routing/createAppRouter';
import { features } from '@/features.registry';

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const ownerSession = {
  user: { githubUserId: '1', githubLogin: 'owner-user', displayName: 'Owner', avatarUrl: null },
  isSystemAdministrator: false,
  tenants: [{ tenantId: 'local', displayName: 'Local Tenant', role: 'owner' as const }],
  antiforgeryToken: 'af-token',
};

const adminSession = {
  ...ownerSession,
  user: { githubUserId: '2', githubLogin: 'admin-user', displayName: 'Admin', avatarUrl: null },
  tenants: [{ tenantId: 'local', displayName: 'Local Tenant', role: 'administrator' as const }],
};

const viewerSession = {
  ...ownerSession,
  user: { githubUserId: '3', githubLogin: 'viewer-user', displayName: 'Viewer', avatarUrl: null },
  tenants: [{ tenantId: 'local', displayName: 'Local Tenant', role: 'viewer' as const }],
};

const sysAdminSession = {
  ...ownerSession,
  user: {
    githubUserId: '4',
    githubLogin: 'sysadmin-user',
    displayName: 'SysAdmin',
    avatarUrl: null,
  },
  isSystemAdministrator: true,
};

function mockSession(session: unknown) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = String(input);
    if (url.endsWith('/api/session')) return jsonResponse(session);
    if (url.includes('/fleet/v1/nodes'))
      return jsonResponse({ generatedAt: '2026-08-08T00:00:00+00:00', nodes: [] });
    if (url.includes('/members')) return jsonResponse([]);
    if (url.includes('/available-users')) return jsonResponse([]);
    if (url.includes('/diagnostic-credentials')) return jsonResponse([]);
    if (url.includes('/incidents')) return jsonResponse([]);
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

describe('settings routes', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('owner role', () => {
    it('renders general settings with section navigation', async () => {
      mockSession(ownerSession);
      renderRoute('/tenants/local/settings/general');

      const nav = await screen.findByRole(
        'navigation',
        { name: 'Tenant settings' },
        { timeout: 5000 },
      );
      expect(nav).toBeInTheDocument();
      expect(nav.querySelector('[aria-current="page"]')).toHaveTextContent('General');
    });

    it('renders tenant ID as copyable metadata on general settings', async () => {
      mockSession(ownerSession);
      renderRoute('/tenants/local/settings/general');

      expect(await screen.findByRole('button', { name: 'Copy tenant ID' })).toBeInTheDocument();
      expect(screen.getByTestId('copyable-id-value')).toHaveTextContent('local');
    });

    it('renders access settings with active navigation state', async () => {
      mockSession(ownerSession);
      renderRoute('/tenants/local/settings/access');

      const nav = await screen.findByRole('navigation', { name: 'Tenant settings' });
      expect(nav.querySelector('[aria-current="page"]')).toHaveTextContent('Access');
    });

    it('shows all four settings tabs for an owner', async () => {
      mockSession(ownerSession);
      renderRoute('/tenants/local/settings/general');

      const nav = await screen.findByRole('navigation', { name: 'Tenant settings' });
      expect(nav).toHaveTextContent('General');
      expect(nav).toHaveTextContent('Access');
      expect(nav).toHaveTextContent('Enrollment');
      expect(nav).toHaveTextContent('Diagnostics');
    });

    it('sets document title for each settings route', async () => {
      mockSession(ownerSession);
      renderRoute('/tenants/local/settings/general');

      await screen.findByRole('heading', { level: 1, name: 'Tenant settings' });
      expect(document.title).toBe('Tenant settings · PitCrew Dashboard');
    });
  });

  describe('administrator role', () => {
    it('renders enrollment page with section navigation', async () => {
      mockSession(adminSession);
      renderRoute('/tenants/local/settings/enrollment');

      const nav = await screen.findByRole('navigation', { name: 'Tenant settings' });
      expect(nav.querySelector('[aria-current="page"]')).toHaveTextContent('Enrollment');
    });

    it('shows only enrollment and diagnostics tabs for administrator', async () => {
      mockSession(adminSession);
      renderRoute('/tenants/local/settings/enrollment');

      const nav = await screen.findByRole('navigation', { name: 'Tenant settings' });
      expect(nav).not.toHaveTextContent('General');
      expect(nav).not.toHaveTextContent('Access');
      expect(nav).toHaveTextContent('Enrollment');
      expect(nav).toHaveTextContent('Diagnostics');
    });

    it('shows diagnostics page with credential form labels', async () => {
      mockSession(adminSession);
      renderRoute('/tenants/local/settings/diagnostics');

      expect(await screen.findByLabelText('Credential label')).toBeInTheDocument();
      expect(screen.getByLabelText('Expiry (hours)')).toBeInTheDocument();
      expect(screen.getByLabelText('Allowed node IDs')).toBeInTheDocument();
      expect(screen.getByLabelText('Allowed profile IDs')).toBeInTheDocument();
    });

    it('denies owner-only general settings to administrator', async () => {
      mockSession(adminSession);
      renderRoute('/tenants/local/settings/general');

      expect(await screen.findByText(/requires the owner role/)).toBeInTheDocument();
    });
  });

  describe('viewer role', () => {
    it('denies enrollment access to viewer', async () => {
      mockSession(viewerSession);
      renderRoute('/tenants/local/settings/enrollment');

      expect(await screen.findByText(/requires the administrator role/)).toBeInTheDocument();
    });

    it('denies diagnostics access to viewer', async () => {
      mockSession(viewerSession);
      renderRoute('/tenants/local/settings/diagnostics');

      expect(await screen.findByText(/requires the administrator role/)).toBeInTheDocument();
    });

    it('denies general settings access to viewer', async () => {
      mockSession(viewerSession);
      renderRoute('/tenants/local/settings/general');

      expect(await screen.findByText(/requires the owner role/)).toBeInTheDocument();
    });
  });

  describe('system administrator', () => {
    it('can access settings as an owner', async () => {
      mockSession(sysAdminSession);
      renderRoute('/tenants/local/settings/general');

      expect(
        await screen.findByRole('heading', { level: 1, name: 'Tenant settings' }),
      ).toBeInTheDocument();
    });
  });

  describe('no access', () => {
    it('shows no-access for a user without tenants', async () => {
      mockSession({ ...ownerSession, tenants: [] });
      renderRoute('/');

      expect(
        await screen.findByRole('heading', { level: 1, name: 'No tenant access' }),
      ).toBeInTheDocument();
    });
  });

  describe('loading and error states', () => {
    it('shows loading state during session bootstrap', async () => {
      vi.spyOn(globalThis, 'fetch').mockImplementation(
        () => new Promise(() => undefined), // never resolves
      );
      renderRoute('/tenants/local/settings/general');

      expect(await screen.findByText('Loading dashboard session…')).toBeInTheDocument();
    });

    it('shows error state for a failed session', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        jsonResponse({ error: { code: 'unavailable', message: 'Service down' } }, 503),
      );
      renderRoute('/tenants/local/settings/general');

      expect(await screen.findByText('Dashboard session is unavailable')).toBeInTheDocument();
    });
  });

  describe('navigation active state in primary nav', () => {
    it('marks Settings as active in the primary navigation on general', async () => {
      mockSession(ownerSession);
      renderRoute('/tenants/local/settings/general');

      await screen.findByRole('heading', { level: 1, name: 'Tenant settings' });
      const nav = screen.getByRole('navigation', { name: 'Primary navigation' });
      const activeLink = nav.querySelector('[aria-current="page"]');
      expect(activeLink).toHaveTextContent('Settings');
    });

    it('marks Settings as active in the primary navigation on access', async () => {
      mockSession(ownerSession);
      renderRoute('/tenants/local/settings/access');

      await screen.findByRole('heading', { level: 1, name: 'Tenant access' });
      const nav = screen.getByRole('navigation', { name: 'Primary navigation' });
      const activeLink = nav.querySelector('[aria-current="page"]');
      expect(activeLink).toHaveTextContent('Settings');
    });
  });
});
