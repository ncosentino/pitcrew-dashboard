import { act, render, screen, within } from '@testing-library/react';
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

function renderPage(fetchImpl: typeof fetch, route = '/tenants/local/incidents') {
  vi.spyOn(globalThis, 'fetch').mockImplementation(fetchImpl);
  const router = createTestRouter(features, [route]);
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
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({ ...page(), truncated: true });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    const row = await screen.findByTestId(`incident-row-${incidentId}`, undefined, {
      timeout: 5000,
    });

    expect(within(row).getByText('critical')).toBeInTheDocument();
    expect(within(row).getByText('triggered')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open owning evidence' })).toHaveAttribute(
      'href',
      `/tenants/local/nodes/${nodeId}/profiles/default`,
    );
    expect(
      screen.getByRole('heading', { name: 'default capacity is below target', level: 2 }),
    ).toBeInTheDocument();
    expect(screen.getByText(/not proof of this incident's cause/i)).toBeInTheDocument();
    expect(screen.getByText(/1 need attention · 1 critical · 0 warning/i)).toBeInTheDocument();
    expect(screen.getByText(/showing only the newest incidents/i)).toBeInTheDocument();
  });

  it('hides acknowledged incidents from the default queue and can reveal all active incidents', async () => {
    const acknowledged = {
      ...incident('acknowledged'),
      incidentId: '33333333-3333-4333-8333-333333333333',
      title: 'Acknowledged connector outage',
    };
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({
          generatedAt: '2026-07-28T01:03:00+00:00',
          incidents: [incident(), acknowledged],
          truncated: false,
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    const user = userEvent.setup();

    expect(await screen.findByTestId(`incident-row-${incidentId}`)).toBeInTheDocument();
    expect(screen.queryByText('Acknowledged connector outage')).not.toBeInTheDocument();
    expect(screen.getByText(/1 acknowledged hidden/i)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Work queue'), 'active');

    expect(
      await screen.findByTestId(`incident-row-${acknowledged.incidentId}`),
    ).toBeInTheDocument();
  });

  it('filters by severity and search text, then sorts the visible queue', async () => {
    const olderCritical = {
      ...incident(),
      incidentId: '33333333-3333-4333-8333-333333333333',
      title: 'Older critical capacity incident',
      triggeredAt: '2026-07-28T00:30:00+00:00',
      lastObservedAt: '2026-07-28T00:40:00+00:00',
    };
    const warning = {
      ...incident(),
      incidentId: '44444444-4444-4444-8444-444444444444',
      severity: 'warning' as const,
      title: 'Runner startup warning',
      reason: 'startup-delay',
      triggeredAt: '2026-07-28T00:45:00+00:00',
      lastObservedAt: '2026-07-28T00:50:00+00:00',
    };
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({
          generatedAt: '2026-07-28T01:03:00+00:00',
          incidents: [incident(), olderCritical, warning],
          truncated: false,
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    const user = userEvent.setup();

    await screen.findByTestId(`incident-row-${incidentId}`);
    await user.selectOptions(screen.getByLabelText('Sort by'), 'oldest');

    const rows = screen.getAllByTestId(/^incident-row-/);
    expect(rows[0]).toHaveAttribute('data-testid', `incident-row-${olderCritical.incidentId}`);

    await user.selectOptions(screen.getByLabelText('Severity'), 'warning');
    expect(screen.queryByTestId(`incident-row-${incidentId}`)).not.toBeInTheDocument();
    expect(screen.getByTestId(`incident-row-${warning.incidentId}`)).toBeInTheDocument();

    await user.type(screen.getByLabelText('Search incidents'), 'startup-delay');
    expect(screen.getByTestId(`incident-row-${warning.incidentId}`)).toBeInTheDocument();
  });

  it('renders incidents without waiting for connector-health enrichment', async () => {
    let resolveFleet: ((response: Response) => void) | undefined;
    const pendingFleet = new Promise<Response>((resolve) => {
      resolveFleet = resolve;
    });
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) return await pendingFleet;
      if (url.includes('/fleet/v1/incidents?status=active')) return jsonResponse(page());
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    expect(await screen.findByTestId(`incident-row-${incidentId}`)).toBeInTheDocument();
    await act(async () => {
      resolveFleet?.(jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] }));
    });
  });

  it('shows retained connector recovery evidence without changing acknowledgement semantics', async () => {
    const connectorIncident = {
      ...incident(),
      profileId: null,
      kind: 'connector-offline',
      title: 'Connector is offline',
      reason: 'connector-offline',
      summary: 'No connector synchronization has been accepted.',
    };
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({
          generatedAt: '2026-07-28T01:03:00+00:00',
          nodes: [
            {
              nodeId,
              displayName: 'Zephyr',
              connectorVersion: '10.0.0',
              enrolledAt: '2026-07-20T01:00:00+00:00',
              lastSeenAt: '2026-07-28T01:00:00+00:00',
              isOnline: false,
              isRevoked: false,
              credentialRotationRequested: false,
              profiles: [],
              capacityControls: [],
              recoveryControls: [],
              connectorHealth: {
                nodeId,
                receivedAt: '2026-07-28T01:00:00+00:00',
                snapshot: {
                  state: 'healthy',
                  processStartedAt: '2026-07-27T20:00:00+00:00',
                  updatedAt: '2026-07-28T01:00:00+00:00',
                  lastAttemptAt: '2026-07-28T01:00:00+00:00',
                  lastSuccessAt: '2026-07-28T01:00:00+00:00',
                  activeOutageId: null,
                  activeOutageStartedAt: null,
                  lastFailureAt: '2026-07-28T00:59:00+00:00',
                  lastFailureCategory: 'synchronization-network',
                  lastFailureProfileId: null,
                  lastFailureDetail: 'Connector synchronization could not reach Dashboard.',
                  consecutiveFailures: 0,
                  nextRetryAt: null,
                  lastRecoveredOutageId: '44444444-4444-4444-8444-444444444444',
                  lastRecoveredOutageStartedAt: '2026-07-28T00:55:00+00:00',
                  lastRecoveredAt: '2026-07-28T01:00:00+00:00',
                  lastRecoveredFailureCategory: 'synchronization-network',
                },
              },
            },
          ],
        });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({
          generatedAt: '2026-07-28T01:03:00+00:00',
          incidents: [connectorIncident],
          truncated: false,
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    await screen.findByTestId(`incident-row-${incidentId}`, undefined, {
      timeout: 5000,
    });

    expect(
      screen.getByRole('heading', { name: 'Connector recovery evidence' }),
    ).toBeInTheDocument();
    expect(await screen.findAllByText('synchronization-network')).toHaveLength(2);
    expect(screen.getByText('Most recent recovery')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Acknowledge incident' })).toBeInTheDocument();
  });

  it('acknowledges an active incident and refreshes its lifecycle state', async () => {
    let acknowledged = false;
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
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
    await screen.findByTestId(`incident-row-${incidentId}`, undefined, {
      timeout: 5000,
    });

    await user.click(screen.getByRole('button', { name: 'Acknowledge incident' }));

    await expect(
      screen.findByText(/now hidden from Needs attention/i),
    ).resolves.toBeInTheDocument();
    expect(screen.queryByTestId(`incident-row-${incidentId}`)).not.toBeInTheDocument();
    expect(
      screen.getByText(
        'Acknowledged default capacity is below target. It remains active and is now hidden from Needs attention.',
      ),
    ).toBeInTheDocument();
    const request = fetchMock.mock.calls.find(
      ([input, init]) =>
        String(input).endsWith(`/incidents/${incidentId}/acknowledge`) && init?.method === 'POST',
    );
    expect(new Headers(request?.[1]?.headers).get('X-PitCrew-Antiforgery')).toBe(
      'test-antiforgery-token',
    );
  });

  it('unacknowledges an acknowledged incident and refreshes its lifecycle state', async () => {
    let unacknowledged = false;
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.endsWith(`/incidents/${incidentId}/unacknowledge`) && init?.method === 'POST') {
        unacknowledged = true;
        return new Response(null, { status: 204 });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse(page(unacknowledged ? 'triggered' : 'acknowledged'));
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderPage(fetchMock, '/tenants/local/incidents?view=active');
    const user = userEvent.setup();
    await screen.findByTestId(`incident-row-${incidentId}`, undefined, {
      timeout: 5000,
    });
    expect(screen.getByText('GitHub user 123')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Unacknowledge incident' }));

    expect(
      await within(screen.getByTestId(`incident-row-${incidentId}`)).findByText(/^triggered$/i),
    ).toBeInTheDocument();
    expect(
      await screen.findByText(
        'Unacknowledged default capacity is below target. The incident returned to triggered.',
      ),
    ).toBeInTheDocument();
    const request = fetchMock.mock.calls.find(
      ([input, init]) =>
        String(input).endsWith(`/incidents/${incidentId}/unacknowledge`) && init?.method === 'POST',
    );
    expect(new Headers(request?.[1]?.headers).get('X-PitCrew-Antiforgery')).toBe(
      'test-antiforgery-token',
    );
  });

  it('announces an error when unacknowledge fails', async () => {
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.endsWith(`/incidents/${incidentId}/unacknowledge`) && init?.method === 'POST') {
        return jsonResponse(
          { error: { code: 'incident_resolved', message: 'The incident resolved.' } },
          409,
        );
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse(page('acknowledged'));
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderPage(fetchMock, '/tenants/local/incidents?view=active');
    const user = userEvent.setup();
    await screen.findByTestId(`incident-row-${incidentId}`, undefined, {
      timeout: 5000,
    });

    await user.click(screen.getByRole('button', { name: 'Unacknowledge incident' }));

    await expect(screen.findByRole('alert')).resolves.toBeInTheDocument();
  });

  it('does not show acknowledgement as resolved after unacknowledge', async () => {
    let unacknowledged = false;
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.endsWith(`/incidents/${incidentId}/unacknowledge`) && init?.method === 'POST') {
        unacknowledged = true;
        return new Response(null, { status: 204 });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse(page(unacknowledged ? 'triggered' : 'acknowledged'));
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderPage(fetchMock, '/tenants/local/incidents?view=active');
    const user = userEvent.setup();
    await screen.findByTestId(`incident-row-${incidentId}`, undefined, {
      timeout: 5000,
    });

    await user.click(screen.getByRole('button', { name: 'Unacknowledge incident' }));

    const refreshedRow = screen.getByTestId(`incident-row-${incidentId}`);
    await within(refreshedRow).findByText(/^triggered$/i);
    expect(within(refreshedRow).queryByText(/^resolved$/i)).not.toBeInTheDocument();
    expect(within(refreshedRow).queryByText(/^acknowledged$/i)).not.toBeInTheDocument();
  });

  it('deep-links one selected case file while preserving the incident queue', async () => {
    const selected = {
      ...incident(),
      incidentId: '55555555-5555-4555-8555-555555555555',
      severity: 'warning' as const,
      title: 'Selected startup incident',
      reason: 'startup-delay',
    };
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({
          generatedAt: '2026-07-28T01:03:00+00:00',
          incidents: [incident(), selected],
          truncated: false,
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    }, `/tenants/local/incidents?view=active&incident=${selected.incidentId}`);

    expect(
      await screen.findByRole('heading', { name: 'Selected startup incident', level: 2 }),
    ).toBeInTheDocument();
    expect(screen.getAllByTestId(/^incident-row-/)).toHaveLength(2);
    const selectedRow = screen.getByTestId(`incident-row-${selected.incidentId}`);
    expect(selectedRow).toHaveClass('bg-accent/60');
    expect(within(selectedRow).getByRole('link', { name: 'Selected' })).toHaveAttribute(
      'href',
      `/tenants/local/incidents?view=active&incident=${selected.incidentId}`,
    );
  });

  it('keeps a deep-linked acknowledged case selected outside the attention filter', async () => {
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse(page('acknowledged'));
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    }, `/tenants/local/incidents?incident=${incidentId}`);

    expect(
      await screen.findByRole('heading', { name: 'default capacity is below target', level: 2 }),
    ).toBeInTheDocument();
    expect(screen.queryByTestId(`incident-row-${incidentId}`)).not.toBeInTheDocument();
    expect(screen.getByText(/outside the current queue filters/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Unacknowledge incident' })).toBeInTheDocument();
  });
});
