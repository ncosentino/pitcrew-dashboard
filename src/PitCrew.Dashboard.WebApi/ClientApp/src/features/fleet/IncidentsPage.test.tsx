import { act, render, screen, waitFor, within } from '@testing-library/react';
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
    sourceObservedAt: '2026-07-28T01:02:30+00:00',
    dashboardReceivedAt: '2026-07-28T01:02:45+00:00',
    evaluatedAt: '2026-07-28T01:03:00+00:00',
    resolutionEvidence: status === 'resolved' ? ('fresh' as const) : null,
    conditionState: status === 'resolved' ? ('resolved' as const) : ('confirmed' as const),
    operatorState: status === 'acknowledged' ? ('acknowledged' as const) : ('unowned' as const),
    currentSeverity: status === 'resolved' ? null : ('critical' as const),
    lastConfirmedSeverity: 'critical' as const,
    peakSeverity: 'critical' as const,
    revision: 1,
  };
}

function page(status: 'triggered' | 'acknowledged' | 'resolved' = 'triggered') {
  return {
    generatedAt: '2026-07-28T01:03:00+00:00',
    incidents: [incident(status)],
    truncated: false,
    totalCount: 1,
    criticalCount: 1,
    warningCount: 0,
    nextCursor: null,
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

  it('renders a compact active incident without duplicating shared page requests', async () => {
    let fleetRequestCount = 0;
    let incidentRequestCount = 0;
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        fleetRequestCount += 1;
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        incidentRequestCount += 1;
        return jsonResponse({
          ...page(),
          truncated: true,
          totalCount: 2,
          nextCursor: 'next-page',
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    const row = await screen.findByTestId(`incident-row-${incidentId}`, undefined, {
      timeout: 5000,
    });

    expect(within(row).getByText('critical')).toBeInTheDocument();
    expect(within(row).getByText('Confirmed problem')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open owning evidence' })).toHaveAttribute(
      'href',
      `/tenants/local/nodes/${nodeId}/profiles/default`,
    );
    expect(
      screen.getByRole('heading', { name: 'default capacity is below target', level: 2 }),
    ).toBeInTheDocument();
    expect(screen.getByText(/not proof of this incident's cause/i)).toBeInTheDocument();
    expect(screen.getByText(/1 require action · 1 critical · 0 warning/i)).toBeInTheDocument();
    expect(
      screen.getByText(/showing 1 of 2 authoritative matching incidents/i),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Load more incidents' })).toBeInTheDocument();
    await waitFor(() => {
      expect(fleetRequestCount).toBe(1);
      expect(incidentRequestCount).toBe(1);
    });
  });

  it('ignores a late continuation response after the active query changes', async () => {
    const lateIncidentId = '77777777-7777-4777-8777-777777777777';
    let resolveContinuation: ((response: Response) => void) | undefined;
    const continuation = new Promise<Response>((resolve) => {
      resolveContinuation = resolve;
    });
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('cursor=next-page')) return continuation;
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({
          ...page(),
          truncated: true,
          totalCount: 2,
          nextCursor: 'next-page',
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    const user = userEvent.setup();
    await screen.findByTestId(`incident-row-${incidentId}`);

    await user.click(screen.getByRole('button', { name: 'Load more incidents' }));
    await user.selectOptions(screen.getByLabelText('Severity'), 'warning');
    resolveContinuation?.(
      jsonResponse({
        ...page(),
        incidents: [
          {
            ...incident(),
            incidentId: lateIncidentId,
            title: 'Late critical continuation',
          },
        ],
        truncated: false,
        nextCursor: null,
      }),
    );

    await act(async () => {
      await continuation;
    });
    expect(screen.queryByTestId(`incident-row-${lateIncidentId}`)).not.toBeInTheDocument();
  });

  it('keeps waiting-for-evidence incidents out of the default action queue', async () => {
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({
          ...page(),
          incidents: [
            {
              ...incident(),
              conditionState: 'waiting-for-evidence',
              currentSeverity: null,
            },
          ],
          criticalCount: 0,
          warningCount: 0,
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    const user = userEvent.setup();
    await screen.findByRole('region', { name: 'Incident action queue' });
    expect(screen.queryByTestId(`incident-row-${incidentId}`)).not.toBeInTheDocument();
    expect(await screen.findByText(/No incidents require action/i)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Work queue'), 'active');

    const row = await screen.findByTestId(`incident-row-${incidentId}`);
    expect(within(row).getByText(/waiting for evidence/i)).toBeInTheDocument();
    expect(
      within(row).getByText('Last confirmed critical; current impact unavailable'),
    ).toBeInTheDocument();
    expect(screen.getByText(/Current rule-specific evidence is unavailable/i)).toBeInTheDocument();
  });

  it('keeps recovery hysteresis discoverable without labeling it as legacy evidence', async () => {
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({
          ...page(),
          incidents: [
            {
              ...incident(),
              conditionState: 'recovering',
              currentSeverity: null,
            },
          ],
          criticalCount: 0,
          warningCount: 0,
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    const user = userEvent.setup();
    expect(screen.queryByTestId(`incident-row-${incidentId}`)).not.toBeInTheDocument();

    await user.selectOptions(await screen.findByLabelText('Work queue'), 'active');

    const row = await screen.findByTestId(`incident-row-${incidentId}`);
    expect(within(row).getByText('Recovery observed')).toBeInTheDocument();
    expect(
      within(screen.getByRole('region', { name: incident().title })).getByText('Recovery observed'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Legacy evidence')).not.toBeInTheDocument();
  });

  it('offers a support diagnostic request for an unresolved incident with its mode preselected', async () => {
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) return jsonResponse(page());
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    const request = await screen.findByTestId(`incident-request-support-${incidentId}`, undefined, {
      timeout: 5000,
    });

    expect(request).toHaveAttribute(
      'href',
      `/tenants/local/support/run?mode=CapacityMismatch&profileId=default&incidentId=${incidentId}&returnTo=%2Ftenants%2Flocal%2Fincidents%3Fview%3Dactive%26incident%3D${incidentId}`,
    );
    expect(
      screen.getByText(/enrolled separately from connector node identity/i),
    ).toBeInTheDocument();
  });

  it('keeps node and profile route scope while showing the matching actionable queue', async () => {
    const otherNodeIncident = {
      ...incident(),
      incidentId: '88888888-8888-4888-8888-888888888888',
      nodeId: '99999999-9999-4999-8999-999999999999',
      profileId: 'other',
      title: 'Other node incident',
    };
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({
          ...page(),
          incidents: [otherNodeIncident, incident()],
          totalCount: 2,
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    }, `/tenants/local/incidents?view=active&nodeId=${nodeId}&profileId=default`);

    expect(await screen.findByText(/Scoped to node .* profile default/i)).toBeInTheDocument();
    expect(await screen.findByTestId(`incident-row-${incidentId}`)).toBeInTheDocument();
    expect(
      screen.queryByTestId(`incident-row-${otherNodeIncident.incidentId}`),
    ).not.toBeInTheDocument();
  });

  it('qualifies scoped loaded counts against a truncated mixed-scope global response', async () => {
    const otherNodeIncident = {
      ...incident(),
      incidentId: '88888888-8888-4888-8888-888888888888',
      nodeId: '99999999-9999-4999-8999-999999999999',
      profileId: 'other',
      title: 'Other node incident',
    };
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) {
        return jsonResponse({
          ...page(),
          incidents: [incident(), otherNodeIncident],
          truncated: true,
          totalCount: 25,
          criticalCount: 14,
          warningCount: 11,
          nextCursor: 'next-page',
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    }, `/tenants/local/incidents?view=active&nodeId=${nodeId}&profileId=default`);

    expect(await screen.findByText(/1 scoped incident loaded/i)).toBeInTheDocument();
    expect(
      screen.getByText(/2 records loaded from a bounded global response/i),
    ).toBeInTheDocument();
    expect(screen.queryByText(/25 authoritative matching incidents/i)).not.toBeInTheDocument();
  });

  it('labels legacy resolutions without inventing clearing provenance', async () => {
    const legacy = {
      ...incident('resolved'),
      sourceObservedAt: null,
      dashboardReceivedAt: null,
      evaluatedAt: null,
      resolutionEvidence: 'legacy-unverified' as const,
    };
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:05:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=resolved')) {
        return jsonResponse({
          ...page('resolved'),
          incidents: [legacy],
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    }, '/tenants/local/incidents?view=resolved');

    expect(
      await screen.findByText(/legacy resolution predates clearing-provenance tracking/i),
    ).toBeInTheDocument();
    expect(screen.getAllByText(/unavailable for legacy evidence/i)).toHaveLength(2);
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
    expect(screen.getByText(/1 operator-owned hidden/i)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Work queue'), 'active');

    expect(
      await screen.findByTestId(`incident-row-${acknowledged.incidentId}`),
    ).toBeInTheDocument();
  });

  it('does not present an acknowledged-only queue as critical action', async () => {
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
          incidents: [acknowledged],
          totalCount: 1,
          criticalCount: 1,
          warningCount: 0,
          truncated: false,
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    const status = await screen.findByText('Open records');
    expect(status).toHaveAttribute('data-status-tone', 'neutral');
    const readiness = screen.getByRole('region', { name: 'Incident action queue' });
    expect(within(readiness).queryByText('Critical action required')).not.toBeInTheDocument();
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
      currentSeverity: 'warning' as const,
      lastConfirmedSeverity: 'warning' as const,
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
    expect(screen.getAllByText('Node identity loading…')).toHaveLength(2);
    expect(screen.getByText(/node connector context is still loading/i)).toBeInTheDocument();
    await act(async () => {
      resolveFleet?.(jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] }));
    });
    expect(await screen.findAllByText('Node not present')).toHaveLength(2);
  });

  it('distinguishes unavailable fleet enrichment from a pending request', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse(
          { error: { code: 'fleet_unavailable', message: 'Fleet enrichment failed.' } },
          503,
        );
      }
      if (url.includes('/fleet/v1/incidents?status=active')) return jsonResponse(page());
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    expect(await screen.findAllByText('Node identity unavailable')).toHaveLength(2);
    expect(screen.getByText(/node connector context could not be loaded/i)).toBeInTheDocument();
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
      if (url.endsWith(`/fleet/v1/incidents/${incidentId}`)) {
        return jsonResponse({
          generatedAt: '2026-07-28T01:04:00+00:00',
          incident: incident('acknowledged'),
        });
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

    await expect(screen.findByText(/now hidden from Needs action/i)).resolves.toBeInTheDocument();
    expect(screen.queryByTestId(`incident-row-${incidentId}`)).not.toBeInTheDocument();
    expect(
      screen.getByText(
        'Acknowledged default capacity is below target. It remains open and is now hidden from Needs action.',
      ),
    ).toBeInTheDocument();
    const request = fetchMock.mock.calls.find(
      ([input, init]) =>
        String(input).endsWith(`/incidents/${incidentId}/acknowledge`) && init?.method === 'POST',
    );
    expect(new Headers(request?.[1]?.headers).get('X-PitCrew-Antiforgery')).toBe(
      'test-antiforgery-token',
    );
    expect(
      fetchMock.mock.calls.some(([input]) =>
        String(input).endsWith(`/fleet/v1/incidents/${incidentId}`),
      ),
    ).toBe(true);
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
      if (url.endsWith(`/fleet/v1/incidents/${incidentId}`)) {
        return jsonResponse({
          generatedAt: '2026-07-28T01:05:00+00:00',
          incident: incident('triggered'),
        });
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
      await within(screen.getByTestId(`incident-row-${incidentId}`)).findByText(
        /^Confirmed problem$/i,
      ),
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
    expect(
      fetchMock.mock.calls.some(([input]) =>
        String(input).endsWith(`/fleet/v1/incidents/${incidentId}`),
      ),
    ).toBe(true);
  });

  it('does not retrieve or apply state when acknowledgement is forbidden', async () => {
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.endsWith(`/incidents/${incidentId}/acknowledge`) && init?.method === 'POST') {
        return jsonResponse(
          { error: { code: 'forbidden', message: 'Administrator access is required.' } },
          403,
        );
      }
      if (url.includes('/fleet/v1/incidents?status=active')) return jsonResponse(page());
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderPage(fetchMock);
    const user = userEvent.setup();
    await screen.findByTestId(`incident-row-${incidentId}`);

    await user.click(screen.getByRole('button', { name: 'Acknowledge incident' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Administrator access is required.');
    expect(
      fetchMock.mock.calls.some(([input]) =>
        String(input).endsWith(`/fleet/v1/incidents/${incidentId}`),
      ),
    ).toBe(false);
    expect(screen.getByTestId(`incident-row-${incidentId}`)).toBeInTheDocument();
  });

  it('ignores a late exact refresh after incident query navigation', async () => {
    let resolveExact: ((response: Response) => void) | undefined;
    const pendingExact = new Promise<Response>((resolve) => {
      resolveExact = resolve;
    });
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.endsWith(`/incidents/${incidentId}/acknowledge`) && init?.method === 'POST') {
        return new Response(null, { status: 204 });
      }
      if (url.endsWith(`/fleet/v1/incidents/${incidentId}`)) return pendingExact;
      if (url.includes('/fleet/v1/incidents?status=resolved')) {
        return jsonResponse({
          generatedAt: '2026-07-28T01:05:00+00:00',
          incidents: [],
          truncated: false,
          totalCount: 0,
          criticalCount: 0,
          warningCount: 0,
          nextCursor: null,
        });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) return jsonResponse(page());
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderPage(fetchMock);
    const user = userEvent.setup();
    await screen.findByTestId(`incident-row-${incidentId}`);

    await user.click(screen.getByRole('button', { name: 'Acknowledge incident' }));
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([input]) =>
          String(input).endsWith(`/fleet/v1/incidents/${incidentId}`),
        ),
      ).toBe(true),
    );
    await user.selectOptions(screen.getByLabelText('Work queue'), 'resolved');
    resolveExact?.(
      jsonResponse({
        generatedAt: '2026-07-28T01:04:00+00:00',
        incident: incident('acknowledged'),
      }),
    );
    await act(async () => {
      await pendingExact;
    });

    expect(screen.queryByText(/Acknowledged default capacity/i)).not.toBeInTheDocument();
    expect(await screen.findByText(/No resolved incidents/i)).toBeInTheDocument();
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
        return jsonResponse({
          generatedAt: '2026-07-28T01:05:00+00:00',
          incident: incident('triggered'),
        });
      }
      if (url.endsWith(`/fleet/v1/incidents/${incidentId}`)) {
        return jsonResponse({
          generatedAt: '2026-07-28T01:05:00+00:00',
          incident: incident('triggered'),
        });
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
    await within(refreshedRow).findByText(/^Confirmed problem$/i);
    expect(within(refreshedRow).queryByText(/^resolved$/i)).not.toBeInTheDocument();
    expect(within(refreshedRow).queryByText(/^acknowledged$/i)).not.toBeInTheDocument();
  });

  it('deep-links one selected case file while preserving the incident queue', async () => {
    const selected = {
      ...incident(),
      incidentId: '55555555-5555-4555-8555-555555555555',
      severity: 'warning' as const,
      currentSeverity: 'warning' as const,
      lastConfirmedSeverity: 'warning' as const,
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
          incidents: [incident()],
          truncated: true,
          totalCount: 2,
          criticalCount: 1,
          warningCount: 1,
          nextCursor: '2026-07-28T01:02:00.0000000+00:00|22222222-2222-4222-8222-222222222222',
        });
      }
      if (url.endsWith(`/fleet/v1/incidents/${selected.incidentId}`)) {
        return jsonResponse({
          generatedAt: '2026-07-28T01:03:00+00:00',
          incident: selected,
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    }, `/tenants/local/incidents?view=active&incident=${selected.incidentId}`);

    expect(
      await screen.findByRole('heading', { name: 'Selected startup incident', level: 2 }),
    ).toBeInTheDocument();
    expect(screen.getAllByTestId(/^incident-row-/)).toHaveLength(1);
    expect(screen.queryByTestId(`incident-row-${selected.incidentId}`)).not.toBeInTheDocument();
    expect(screen.getByText(/outside the current queue filters/i)).toBeInTheDocument();
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

  it('does not substitute another incident when a deep-linked record is unavailable', async () => {
    const missingIncidentId = '66666666-6666-4666-8666-666666666666';
    renderPage(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        return jsonResponse({ generatedAt: '2026-07-28T01:03:00+00:00', nodes: [] });
      }
      if (url.includes('/fleet/v1/incidents?status=active')) return jsonResponse(page());
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    }, `/tenants/local/incidents?view=active&incident=${missingIncidentId}`);
    const user = userEvent.setup();

    expect(await screen.findByText('Selected incident is unavailable')).toBeInTheDocument();
    expect(screen.getByTestId(`incident-row-${incidentId}`)).toBeInTheDocument();
    expect(
      screen.queryByRole('heading', { name: 'default capacity is below target', level: 2 }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Search all history' })).toHaveAttribute(
      'href',
      `/tenants/local/incidents?view=history&incident=${missingIncidentId}`,
    );

    await user.click(screen.getByRole('button', { name: 'Clear selection' }));

    expect(
      await screen.findByRole('heading', { name: 'default capacity is below target', level: 2 }),
    ).toBeInTheDocument();
  });

  it('collapses the mobile queue and focuses the selected case after investigation', async () => {
    const secondIncident = {
      ...incident(),
      incidentId: '77777777-7777-4777-8777-777777777777',
      severity: 'warning' as const,
      title: 'Runner startup warning',
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
          incidents: [incident(), secondIncident],
          truncated: false,
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    }, '/tenants/local/incidents?view=active');
    const user = userEvent.setup();

    const queueSummary = await screen.findByText('Choose incident', { exact: true });
    const queueDisclosure = queueSummary.closest('details');
    if (queueDisclosure == null) throw new Error('Incident queue disclosure is required.');
    await user.click(queueSummary);
    expect(queueDisclosure).toHaveAttribute('open');

    await user.click(
      within(screen.getByTestId(`incident-row-${secondIncident.incidentId}`)).getByRole('link', {
        name: 'Investigate',
      }),
    );

    expect(
      await screen.findByRole('heading', { name: 'Runner startup warning', level: 2 }),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Selected incident case' })).toHaveFocus(),
    );
    expect(queueDisclosure).not.toHaveAttribute('open');
  });
});
