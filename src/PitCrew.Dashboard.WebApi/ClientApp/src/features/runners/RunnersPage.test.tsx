import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation, useParams } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { FleetProvider, type FleetResponse } from '@/core/fleet';

import { runnersManifest } from './manifest';
import { flattenFleetSlots } from './runnerRows';
import { RunnersPage } from './RunnersPage';

const localNodeId = '10000000-0000-4000-8000-000000000001';
const remoteNodeId = '20000000-0000-4000-8000-000000000002';

function slotResponse(
  key: string,
  overrides: Readonly<Record<string, unknown>> = {},
): Readonly<Record<string, unknown>> {
  return {
    key,
    repository: 'octo/example',
    desired: true,
    processRunning: true,
    state: 'running',
    failureCount: 0,
    backoffSeconds: 0,
    updatedAt: '2026-07-26T02:00:00+00:00',
    resources: { cpuCores: 1.5, memoryWorkingSetBytes: 1_048_576, pids: 4 },
    activity: 'idle',
    target: 'ubuntu-latest',
    registrationStatus: 'connected',
    ...overrides,
  };
}

function profileResponse(
  profileId: string,
  slots: ReadonlyArray<Readonly<Record<string, unknown>>>,
  telemetryStatus: 'available' | 'partial' | 'unavailable' = 'available',
  overrides: Readonly<Record<string, unknown>> = {},
) {
  const eligibleSlots = slots.filter((slot) => slot.registrationStatus === 'connected').length;
  return {
    schemaVersion: 1,
    managerContractVersion: 10,
    profileId,
    managerInstanceId: `manager-${profileId}`,
    managerStatus: 'running',
    observedAt: '2026-07-26T02:00:00+00:00',
    scope: 'repo',
    generation: 1,
    desiredStateHash: null,
    desiredStateStatus: 'accepted',
    desiredSlots: slots.length,
    activeSlots: slots.length,
    eligibleSlots,
    drainingSlots: 0,
    slots,
    resourceTelemetry: {
      sampledAt: '2026-07-26T02:00:00+00:00',
      status: telemetryStatus,
      host: null,
      manager: null,
    },
    ...overrides,
  };
}

function nodeResponse(
  nodeId: string,
  displayName: string,
  profiles: ReadonlyArray<ReturnType<typeof profileResponse>>,
  isOnline = true,
) {
  return {
    nodeId,
    displayName,
    connectorVersion: '1.0.0',
    enrolledAt: '2026-07-26T01:00:00+00:00',
    lastSeenAt: '2026-07-26T02:00:00+00:00',
    isOnline,
    isRevoked: false,
    credentialRotationRequested: false,
    profiles,
    capacityControls: [],
  };
}

function fleetResponse(nodes: ReadonlyArray<ReturnType<typeof nodeResponse>>) {
  return { generatedAt: '2026-07-26T02:00:00+00:00', nodes };
}

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function LocationProbe() {
  return <output aria-label="Current query">{useLocation().search}</output>;
}

function TestRoute() {
  const { tenantId = '' } = useParams();
  return (
    <FleetProvider tenantId={tenantId}>
      <RunnersPage tenantId={tenantId} />
      <LocationProbe />
    </FleetProvider>
  );
}

function renderRunners(fleet: ReturnType<typeof fleetResponse>, path = '/tenants/local/runners') {
  const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(fleet));
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/tenants/:tenantId/runners" element={<TestRoute />} />
      </Routes>
    </MemoryRouter>,
  );
  return fetchMock;
}

function standardFleet() {
  return fleetResponse([
    nodeResponse(localNodeId, 'Alpha', [
      profileResponse('build', [
        slotResponse('slot-a', {
          repository: 'octo/alpha',
          activity: 'busy',
          state: 'degraded',
          failureCount: 2,
        }),
      ]),
      profileResponse('deploy', [
        slotResponse('slot-b', {
          repository: 'octo/beta',
          activity: 'draining',
          registrationStatus: 'disconnected',
          state: 'draining',
        }),
      ]),
    ]),
    nodeResponse(remoteNodeId, 'Zulu', [
      profileResponse('build', [
        slotResponse('slot-c', {
          repository: 'other/gamma',
          activity: 'idle',
          registrationStatus: 'registration-missing',
          state: 'running',
        }),
      ]),
    ]),
  ]);
}

describe('runners feature', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('registers its viewer route and primary navigation contribution', () => {
    expect(runnersManifest.id).toBe('runners');
    expect(runnersManifest.navigation).toEqual([
      { label: 'Runners', path: '/tenants/:tenantId/runners' },
    ]);
    expect(runnersManifest.routes).toHaveLength(1);
  });

  it('flattens every node and profile while preserving route context', () => {
    const rows = flattenFleetSlots(standardFleet() as unknown as FleetResponse);

    expect(rows).toHaveLength(3);
    expect(rows.map((row) => [row.nodeId, row.nodeName, row.profileId, row.slot.key])).toEqual([
      [localNodeId, 'Alpha', 'build', 'slot-a'],
      [localNodeId, 'Alpha', 'deploy', 'slot-b'],
      [remoteNodeId, 'Zulu', 'build', 'slot-c'],
    ]);
  });

  it('loads only the active tenant and links rows to tenant-scoped profile details', async () => {
    const fetchMock = renderRunners(standardFleet());

    const table = await screen.findByRole('table', {
      name: 'Runner slots for the active tenant',
    });
    expect(within(table).getAllByRole('row')).toHaveLength(4);
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:3000/api/tenants/local/fleet/v1/nodes',
      expect.objectContaining({ method: 'GET' }),
    );
    expect(fetchMock).not.toHaveBeenCalledWith(
      expect.stringContaining('/api/tenants/remote/'),
      expect.anything(),
    );
    expect(
      within(screen.getByTestId(`runner-row-${localNodeId}-build-slot-a`)).getByRole('link'),
    ).toHaveAttribute('href', `/tenants/local/nodes/${localNodeId}/profiles/build`);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('restores every filter and sorting field from a bookmarked URL', async () => {
    renderRunners(
      standardFleet(),
      `/tenants/local/runners?node=${localNodeId}&profile=build&repository=ALPHA&activity=busy&registration=connected&state=degraded&sort=failures&direction=desc`,
    );

    expect(await screen.findByLabelText('Node')).toHaveValue(localNodeId);
    expect(screen.getByLabelText('Profile')).toHaveValue('build');
    expect(screen.getByLabelText('Repository')).toHaveValue('ALPHA');
    expect(screen.getByLabelText('Activity')).toHaveValue('busy');
    expect(screen.getByLabelText('GitHub registration')).toHaveValue('connected');
    expect(screen.getByLabelText('Lifecycle state')).toHaveValue('degraded');
    expect(screen.getByLabelText('Sort by')).toHaveValue('failures');
    expect(screen.getByLabelText('Sort direction')).toHaveValue('desc');
    expect(screen.getAllByTestId(/^runner-row-/)).toHaveLength(1);
    expect(screen.getByText('slot-a')).toBeInTheDocument();
  });

  it('writes each filter and sorting choice to the URL', async () => {
    renderRunners(standardFleet());
    const user = userEvent.setup();

    await screen.findByLabelText('Node');
    await user.selectOptions(screen.getByLabelText('Node'), localNodeId);
    await user.selectOptions(screen.getByLabelText('Profile'), 'deploy');
    await user.type(screen.getByLabelText('Repository'), 'beta');
    await user.selectOptions(screen.getByLabelText('Activity'), 'draining');
    await user.selectOptions(screen.getByLabelText('GitHub registration'), 'disconnected');
    await user.selectOptions(screen.getByLabelText('Lifecycle state'), 'draining');
    await user.selectOptions(screen.getByLabelText('Sort by'), 'slot');
    await user.selectOptions(screen.getByLabelText('Sort direction'), 'desc');

    await waitFor(() =>
      expect(screen.getByLabelText('Current query')).toHaveTextContent(
        `node=${localNodeId}&profile=deploy&repository=beta&activity=draining&registration=disconnected&state=draining&sort=slot&direction=desc`,
      ),
    );
    expect(screen.getAllByTestId(/^runner-row-/)).toHaveLength(1);
    expect(screen.getByText('slot-b')).toBeInTheDocument();
  });

  it('sorts deterministically with route context as the tie breaker', async () => {
    renderRunners(standardFleet(), '/tenants/local/runners?sort=profile&direction=desc');

    const table = await screen.findByRole('table');
    const rows = within(table).getAllByTestId(/^runner-row-/);
    expect(rows.map((row) => within(row).getAllByRole('cell')[2]?.textContent)).toEqual([
      'slot-b',
      'slot-c',
      'slot-a',
    ]);
  });

  it('keeps missing metrics unavailable while preserving reported zero values', async () => {
    renderRunners(
      fleetResponse([
        nodeResponse(localNodeId, 'Alpha', [
          profileResponse(
            'build',
            [
              slotResponse('missing', { resources: null }),
              slotResponse('zero', {
                resources: { cpuCores: 0, memoryWorkingSetBytes: 0, pids: 0 },
              }),
            ],
            'partial',
          ),
        ]),
      ]),
    );

    expect(await screen.findByText(/Partial resource data: 1 of 2/)).toBeInTheDocument();
    const missing = screen.getByTestId(`runner-row-${localNodeId}-build-missing`);
    expect(within(missing).getAllByText('Unavailable')).toHaveLength(6);
    expect(screen.getByTestId(`runner-network-${localNodeId}-build-missing`)).toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId(`runner-block-io-${localNodeId}-build-missing`)).toHaveTextContent(
      'Unavailable',
    );
    const zero = screen.getByTestId(`runner-row-${localNodeId}-build-zero`);
    expect(within(zero).getByText('0 cores')).toBeInTheDocument();
    expect(within(zero).getByText('0 B')).toBeInTheDocument();
    expect(within(zero).getByText('0 PIDs')).toBeInTheDocument();
  });

  it('filters and displays contract 11 image, I/O, and exit evidence', async () => {
    const digest = `sha256:${'cd'.repeat(32)}`;
    renderRunners(
      fleetResponse([
        nodeResponse(localNodeId, 'Alpha', [
          profileResponse('build', [
            slotResponse('oom', {
              imageId: digest,
              resources: {
                cpuCores: 1,
                memoryWorkingSetBytes: 1_048_576,
                pids: 4,
                networkRxBytes: 0,
                networkTxBytes: null,
                blockReadBytes: 2_097_152,
                blockWriteBytes: 0,
              },
              lastExit: {
                observedAt: '2026-07-26T01:59:00+00:00',
                classification: 'oom-killed',
                exitCode: 137,
                signal: 9,
                dockerOomKilled: true,
                evidence: 'docker-inspect',
              },
            }),
            slotResponse('quiet', { imageId: null, lastExit: null }),
          ]),
        ]),
      ]),
    );

    const oomRow = await screen.findByTestId(`runner-row-${localNodeId}-build-oom`);
    expect(within(oomRow).getByTestId(`runner-image-${localNodeId}-build-oom`)).toHaveTextContent(
      'cdcdcdcdcdcd',
    );
    expect(within(oomRow).getByTestId(`runner-network-${localNodeId}-build-oom`)).toHaveTextContent(
      '0 B in · Unavailable out',
    );
    expect(
      within(oomRow).getByTestId(`runner-block-io-${localNodeId}-build-oom`),
    ).toHaveTextContent('2 MiB read · 0 B written');
    expect(
      within(oomRow).getByTestId(`runner-last-exit-${localNodeId}-build-oom`),
    ).toHaveTextContent('Docker confirmed an out-of-memory kill');

    await userEvent.selectOptions(screen.getByLabelText('Last exit'), 'oom-killed');

    expect(screen.getByTestId(`runner-row-${localNodeId}-build-oom`)).toBeInTheDocument();
    expect(screen.queryByTestId(`runner-row-${localNodeId}-build-quiet`)).not.toBeInTheDocument();
    expect(screen.getByLabelText('Current query')).toHaveTextContent('exit=oom-killed');
  });

  it('shows legacy slots as unknown instead of GitHub-eligible', async () => {
    const legacySlot = slotResponse('legacy', { registrationStatus: undefined });
    renderRunners(
      fleetResponse([
        nodeResponse(localNodeId, 'Alpha', [
          profileResponse('build', [legacySlot], 'available', {
            managerContractVersion: 9,
            eligibleSlots: undefined,
          }),
        ]),
      ]),
    );

    const registration = await screen.findByTestId(
      `runner-registration-${localNodeId}-build-legacy`,
    );
    expect(registration).toHaveTextContent('unknown');
  });

  it('rejects inconsistent contract-ten eligible capacity', async () => {
    renderRunners(
      fleetResponse([
        nodeResponse(localNodeId, 'Alpha', [
          profileResponse('build', [slotResponse('invalid')], 'available', {
            eligibleSlots: 0,
          }),
        ]),
      ]),
    );

    expect(
      await screen.findByText(/Runner data is unavailable: Response did not match expected schema/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('distinguishes fleet-empty, filtered-empty, and offline-node states', async () => {
    const { unmount } = render(
      <MemoryRouter initialEntries={['/tenants/local/runners']}>
        <Routes>
          <Route path="/tenants/:tenantId/runners" element={<TestRoute />} />
        </Routes>
      </MemoryRouter>,
    );
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(fleetResponse([])));

    expect(
      await screen.findByText('No runner slots have been reported for this tenant.'),
    ).toBeInTheDocument();
    unmount();
    vi.restoreAllMocks();

    renderRunners(
      fleetResponse([
        nodeResponse(
          localNodeId,
          'Offline',
          [profileResponse('build', [slotResponse('offline-slot')])],
          false,
        ),
      ]),
      '/tenants/local/runners?repository=no-match',
    );
    expect(
      await screen.findByText('No runner slots match the current filters.'),
    ).toBeInTheDocument();
  });

  it('shows explicit loading, unavailable-resource, and offline-node states', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(() => new Promise<Response>(() => undefined));
    const { unmount } = render(
      <MemoryRouter initialEntries={['/tenants/local/runners']}>
        <Routes>
          <Route path="/tenants/:tenantId/runners" element={<TestRoute />} />
        </Routes>
      </MemoryRouter>,
    );
    expect(await screen.findByText('Loading runners…')).toBeInTheDocument();
    unmount();
    vi.restoreAllMocks();

    renderRunners(
      fleetResponse([
        nodeResponse(
          localNodeId,
          'Offline',
          [
            profileResponse(
              'build',
              [slotResponse('offline-slot', { resources: undefined })],
              'unavailable',
            ),
          ],
          false,
        ),
      ]),
    );
    expect(
      await screen.findByText('Resource data unavailable for all displayed slots.'),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/1 displayed slot is from offline nodes and may be stale/),
    ).toBeInTheDocument();
    expect(screen.getByText('offline')).toBeInTheDocument();
  });

  it('shows an unavailable state when the fleet resource cannot be loaded', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse({ error: { code: 'unavailable', message: 'Fleet unavailable' } }, 503),
    );
    render(
      <MemoryRouter initialEntries={['/tenants/local/runners']}>
        <Routes>
          <Route path="/tenants/:tenantId/runners" element={<TestRoute />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(
      await screen.findByText(/Runner data is unavailable: Fleet unavailable/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('keeps rows visible with an explicit stale state after a refresh failure', async () => {
    vi.useFakeTimers();
    let request = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(() => {
      request++;
      return request === 1
        ? Promise.resolve(jsonResponse(standardFleet()))
        : Promise.reject(new Error('temporary outage'));
    });
    render(
      <MemoryRouter initialEntries={['/tenants/local/runners']}>
        <Routes>
          <Route path="/tenants/:tenantId/runners" element={<TestRoute />} />
        </Routes>
      </MemoryRouter>,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(screen.getAllByTestId(/^runner-row-/)).toHaveLength(3);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(
      screen.getByText(
        /Showing stale runner data because the latest fleet refresh failed: temporary outage/,
      ),
    ).toBeInTheDocument();
    expect(screen.getAllByTestId(/^runner-row-/)).toHaveLength(3);
  });
});
