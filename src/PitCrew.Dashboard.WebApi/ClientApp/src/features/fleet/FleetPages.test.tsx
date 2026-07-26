import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider } from '@/core/auth';
import { createTestRouter } from '@/core/routing/createAppRouter';
import { features } from '@/features.registry';

import { fleetDensityStorageKey } from './FleetOverviewPage';

const alphaId = 'a6235ec4-2a15-4f91-a9e0-811152869a51';
const bravoId = 'b6235ec4-2a15-4f91-a9e0-811152869a52';
const charlieId = 'c6235ec4-2a15-4f91-a9e0-811152869a53';

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

function profileResponse() {
  return {
    schemaVersion: 1,
    managerContractVersion: 7,
    profileId: 'build',
    managerInstanceId: 'manager-build',
    managerStatus: 'running',
    observedAt: '2026-07-19T18:30:00+00:00',
    scope: 'repository',
    generation: 4,
    desiredStateHash: 'a'.repeat(64),
    desiredStateStatus: 'accepted',
    desiredSlots: 3,
    configuredSlots: 4,
    activeSlots: 2,
    drainingSlots: 1,
    resourceTelemetry: {
      sampledAt: '2026-07-19T18:30:00+00:00',
      status: 'partial',
      host: null,
      manager: {
        cpuCores: 0.5,
        memoryWorkingSetBytes: 1024,
        pids: 10,
      },
    },
    slots: [
      {
        key: 'build-000001',
        repository: 'https://github.com/example/project',
        desired: true,
        processRunning: true,
        state: 'online',
        failureCount: 0,
        backoffSeconds: 0,
        updatedAt: '2026-07-19T18:30:00+00:00',
        resources: {
          cpuCores: 1,
          memoryWorkingSetBytes: 2048,
          pids: 20,
        },
      },
      {
        key: 'build-000002',
        repository: null,
        desired: true,
        processRunning: true,
        state: 'online',
        failureCount: 0,
        backoffSeconds: 0,
        updatedAt: null,
        resources: null,
      },
    ],
  };
}

function fleetResponse() {
  return {
    generatedAt: '2026-07-19T18:30:05+00:00',
    nodes: [
      {
        nodeId: charlieId,
        displayName: 'Charlie',
        connectorVersion: '3.0.0',
        enrolledAt: '2026-07-17T15:00:00+00:00',
        lastSeenAt: '2026-07-17T16:00:00+00:00',
        isOnline: false,
        isRevoked: true,
        credentialRotationRequested: false,
        profiles: [],
        capacityControls: [],
      },
      {
        nodeId: alphaId,
        displayName: 'Alpha',
        connectorVersion: '2.0.0',
        enrolledAt: '2026-07-18T15:00:00+00:00',
        lastSeenAt: '2026-07-19T18:30:05+00:00',
        isOnline: true,
        isRevoked: false,
        credentialRotationRequested: false,
        profiles: [profileResponse()],
        capacityControls: [],
      },
      {
        nodeId: bravoId,
        displayName: 'Bravo',
        connectorVersion: '1.5.0',
        enrolledAt: '2026-07-16T15:00:00+00:00',
        lastSeenAt: null,
        isOnline: false,
        isRevoked: false,
        credentialRotationRequested: false,
        profiles: [],
        capacityControls: [],
      },
    ],
  };
}

function renderRoute(path: string, session: unknown = ownerSession) {
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = String(input);
    if (url.endsWith('/api/session')) return jsonResponse(session);
    if (url.endsWith('/fleet/v1/nodes')) return jsonResponse(fleetResponse());
    return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
  });
  const router = createTestRouter(features, [path]);
  render(
    <SessionProvider>
      <RouterProvider router={router} />
    </SessionProvider>,
  );
  return router;
}

describe('fleet overview and node detail', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders node-only summaries with honest partial aggregate telemetry', async () => {
    renderRoute('/tenants/local/fleet');

    const row = await screen.findByTestId(`fleet-node-${alphaId}`);
    expect(within(row).getByRole('link', { name: 'Alpha' })).toHaveAttribute(
      'href',
      `/tenants/local/nodes/${alphaId}`,
    );
    expect(row).toHaveTextContent('2.0.0');
    expect(row).toHaveTextContent('1');
    expect(row).toHaveTextContent('4 / 2');
    expect(row).toHaveTextContent('1.5 cores / 3 KiB');
    expect(row).toHaveTextContent('2 of 3 sources');
    expect(row).toHaveTextContent('partial');
    expect(screen.queryByText('Absolute maximum')).not.toBeInTheDocument();
    expect(screen.queryByText('build-000001')).not.toBeInTheDocument();
  });

  it('filters and sorts deterministically and persists density', async () => {
    const user = userEvent.setup();
    renderRoute('/tenants/local/fleet');

    const table = await screen.findByRole('table');
    expect(
      within(table)
        .getAllByRole('row')
        .slice(1)
        .map((row) => row.textContent),
    ).toEqual([
      expect.stringContaining('Alpha'),
      expect.stringContaining('Bravo'),
      expect.stringContaining('Charlie'),
    ]);

    await user.selectOptions(screen.getByLabelText('Status'), 'offline');
    expect(screen.getByTestId(`fleet-node-${bravoId}`)).toBeInTheDocument();
    expect(screen.queryByTestId(`fleet-node-${alphaId}`)).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Status'), 'revoked');
    expect(screen.getByTestId(`fleet-node-${charlieId}`)).toBeInTheDocument();
    expect(screen.queryByTestId(`fleet-node-${bravoId}`)).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Status'), 'all');
    await user.selectOptions(screen.getByLabelText('Sort by'), 'status');
    expect(
      within(table)
        .getAllByRole('row')
        .slice(1)
        .map((row) => row.textContent),
    ).toEqual([
      expect.stringContaining('Alpha'),
      expect.stringContaining('Bravo'),
      expect.stringContaining('Charlie'),
    ]);

    await user.type(screen.getByLabelText('Search nodes'), '3.0.0');
    expect(screen.getByTestId(`fleet-node-${charlieId}`)).toBeInTheDocument();
    expect(screen.queryByTestId(`fleet-node-${bravoId}`)).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Density'), 'compact');
    expect(localStorage.getItem(fleetDensityStorageKey)).toBe('compact');
  });

  it('loads the persisted density preference', async () => {
    localStorage.setItem(fleetDensityStorageKey, 'compact');
    renderRoute('/tenants/local/fleet');

    expect(await screen.findByLabelText('Density')).toHaveValue('compact');
  });

  it('renders profile triage links and partial telemetry without runner tables', async () => {
    renderRoute(`/tenants/local/nodes/${alphaId}`);

    expect(await screen.findByRole('heading', { level: 2, name: 'Alpha' })).toBeInTheDocument();
    const profile = screen.getByTestId('node-profile-build');
    expect(within(profile).getByRole('link', { name: 'build' })).toHaveAttribute(
      'href',
      `/tenants/local/nodes/${alphaId}/profiles/build`,
    );
    expect(profile).toHaveTextContent('Configured4');
    expect(profile).toHaveTextContent('Active2');
    expect(profile).toHaveTextContent('Partial telemetry');
    expect(screen.queryByText('build-000001')).not.toBeInTheDocument();
  });

  it('renders node-not-found, offline, revoked, and empty-profile states', async () => {
    const router = renderRoute('/tenants/local/nodes/00000000-0000-0000-0000-000000000000');

    expect(await screen.findByText('Node not found')).toBeInTheDocument();
    await act(async () => {
      await router.navigate(`/tenants/local/nodes/${bravoId}`);
    });
    expect(await screen.findByText(/This node is offline/)).toBeInTheDocument();
    expect(screen.getByText('No profiles reported')).toBeInTheDocument();

    await act(async () => {
      await router.navigate(`/tenants/local/nodes/${charlieId}`);
    });
    expect(await screen.findByText(/This node is revoked/)).toBeInTheDocument();
    expect(screen.getByText('No profiles reported')).toBeInTheDocument();
  });

  it('preserves rename, rotation, and revoke behavior for administrators', async () => {
    let response = fleetResponse();
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) return jsonResponse(response);
      if (init?.method === 'PUT') {
        const body = JSON.parse(String(init.body)) as { displayName: string };
        response = {
          ...response,
          nodes: response.nodes.map((node) =>
            node.nodeId === alphaId ? { ...node, displayName: body.displayName } : node,
          ),
        };
        return new Response(null, { status: 204 });
      }
      if (url.endsWith('/credential-rotation') && init?.method === 'POST') {
        response = {
          ...response,
          nodes: response.nodes.map((node) =>
            node.nodeId === alphaId ? { ...node, credentialRotationRequested: true } : node,
          ),
        };
        return new Response(null, { status: 204 });
      }
      if (url.endsWith('/revoke') && init?.method === 'POST') {
        response = {
          ...response,
          nodes: response.nodes.map((node) =>
            node.nodeId === alphaId ? { ...node, isOnline: false, isRevoked: true } : node,
          ),
        };
        return new Response(null, { status: 204 });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    const router = createTestRouter(features, [`/tenants/local/nodes/${alphaId}`]);
    render(
      <SessionProvider>
        <RouterProvider router={router} />
      </SessionProvider>,
    );
    const user = userEvent.setup();

    const input = await screen.findByLabelText('Server display name');
    await user.clear(input);
    await user.type(input, 'Renamed Alpha');
    await user.click(screen.getByRole('button', { name: 'Rename server' }));
    expect(
      await screen.findByRole('heading', { level: 2, name: 'Renamed Alpha' }),
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Rotate credential' }));
    await waitFor(() => expect(screen.getByText('rotation requested')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'Revoke' }));
    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText(
        'Revoke Renamed Alpha? The connector will stop synchronizing until it re-enrolls with a new one-time code.',
      ),
    ).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Revoke node' }));
    expect(await screen.findByText(/This node is revoked/)).toBeInTheDocument();

    const mutationCalls = fetchMock.mock.calls.filter(([, init]) =>
      ['PUT', 'POST'].includes(init?.method ?? ''),
    );
    expect(mutationCalls).toHaveLength(3);
    for (const [, init] of mutationCalls) {
      expect(new Headers(init?.headers).get('X-PitCrew-Antiforgery')).toBe(
        'test-antiforgery-token',
      );
    }
  });

  it('hides node administration from viewers', async () => {
    renderRoute(`/tenants/local/nodes/${alphaId}`, {
      ...ownerSession,
      tenants: [{ ...ownerSession.tenants[0], role: 'viewer' }],
    });

    expect(await screen.findByRole('heading', { level: 2, name: 'Alpha' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Server display name')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rotate credential' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Revoke' })).not.toBeInTheDocument();
  });

  it('keeps stale node data visible when a refresh fails', async () => {
    let fleetLoads = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) {
        fleetLoads++;
        return fleetLoads === 1
          ? jsonResponse(fleetResponse())
          : jsonResponse({ error: { code: 'unavailable', message: 'Fleet refresh failed.' } }, 503);
      }
      if (url.endsWith('/credential-rotation') && init?.method === 'POST') {
        return new Response(null, { status: 204 });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    const router = createTestRouter(features, [`/tenants/local/nodes/${alphaId}`]);
    render(
      <SessionProvider>
        <RouterProvider router={router} />
      </SessionProvider>,
    );
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Rotate credential' }));

    expect(
      await screen.findByText(/Showing stale fleet data.*Fleet refresh failed/),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Alpha' })).toBeInTheDocument();
  });
});
