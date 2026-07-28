import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider } from '@/core/auth';
import { createTestRouter } from '@/core/routing/createAppRouter';
import { features } from '@/features.registry';

const nodeId = '11111111-1111-4111-8111-111111111111';
const incidentId = '22222222-2222-4222-8222-222222222222';
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

function incident(status: 'triggered' | 'acknowledged' | 'resolved' = 'triggered') {
  return {
    incidentId,
    nodeId,
    profileId: 'default',
    kind: 'capacity-deficit',
    severity: 'critical',
    status,
    title: 'default capacity is below target',
    summary: 'Target fixed reports local deficit 2 and eligibility deficit 0.',
    reason: 'docker-unavailable',
    evidence: 'daemon unavailable',
    link: `/tenants/local/nodes/${nodeId}/profiles/default`,
    firstObservedAt: '2026-07-28T01:00:00+00:00',
    triggeredAt: '2026-07-28T01:02:00+00:00',
    lastObservedAt: '2026-07-28T01:03:00+00:00',
    acknowledgedAt: status === 'acknowledged' ? '2026-07-28T01:04:00+00:00' : null,
    acknowledgedByGitHubUserId: status === 'acknowledged' ? '123' : null,
    resolvedAt: status === 'resolved' ? '2026-07-28T01:05:00+00:00' : null,
  };
}

function page(status: 'triggered' | 'acknowledged' | 'resolved' = 'triggered') {
  return {
    generatedAt: '2026-07-28T01:03:00+00:00',
    incidents: [incident(status)],
    truncated: false,
  };
}

function renderPage(fetchImpl: typeof fetch) {
  vi.spyOn(globalThis, 'fetch').mockImplementation(fetchImpl);
  const router = createTestRouter(features, ['/tenants/local/incidents']);
  render(
    <SessionProvider>
      <RouterProvider router={router} />
    </SessionProvider>,
  );
}

describe('IncidentsPage', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders a compact active incident with a direct evidence link', async () => {
    renderPage(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({ ...page(), truncated: true });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    const row = await screen.findByTestId(`incident-row-${incidentId}`);

    expect(within(row).getByText('critical')).toBeInTheDocument();
    expect(within(row).getByText('triggered')).toBeInTheDocument();
    expect(
      within(row).getByRole('link', { name: 'default capacity is below target' }),
    ).toHaveAttribute('href', `/tenants/local/nodes/${nodeId}/profiles/default`);
    expect(screen.getByText('1', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.getByText(/showing only the newest incidents/i)).toBeInTheDocument();
  });

  it('acknowledges an active incident and refreshes its lifecycle state', async () => {
    let acknowledged = false;
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.endsWith(`/incidents/${incidentId}/acknowledge`) && init?.method === 'POST') {
        acknowledged = true;
        return new Response(null, { status: 204 });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse(page(acknowledged ? 'acknowledged' : 'triggered'));
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderPage(fetchMock);
    const user = userEvent.setup();
    const row = await screen.findByTestId(`incident-row-${incidentId}`);

    await user.click(within(row).getByRole('button', { name: 'Acknowledge' }));

    expect(await within(row).findByText(/^acknowledged$/i)).toBeInTheDocument();
    const request = fetchMock.mock.calls.find(
      ([input, init]) =>
        String(input).endsWith(`/incidents/${incidentId}/acknowledge`) && init?.method === 'POST',
    );
    expect(new Headers(request?.[1]?.headers).get('X-PitCrew-Antiforgery')).toBe(
      'test-antiforgery-token',
    );
  });
});
