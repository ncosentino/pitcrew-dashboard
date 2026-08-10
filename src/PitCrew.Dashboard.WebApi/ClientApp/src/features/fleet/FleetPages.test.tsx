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

function profileResponse(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    schemaVersion: 1,
    managerContractVersion: 13,
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
    eligibleSlots: 1,
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
    operationJournal: {
      status: 'current',
      capacity: 32,
      highestSequence: null,
      droppedEvents: 0,
      events: [],
    },
    subsystemHealth: {
      docker: {
        state: 'unknown',
        observedAt: '2026-07-19T18:30:00+00:00',
        consecutiveFailures: 0,
        retryAt: null,
        lastSuccess: null,
        lastFailure: null,
      },
      github: {
        state: 'unknown',
        observedAt: '2026-07-19T18:30:00+00:00',
        consecutiveFailures: 0,
        retryAt: null,
        lastSuccess: null,
        lastFailure: null,
      },
    },
    capacityEvidence: {
      fixed: {
        observedAt: '2026-07-19T18:30:00+00:00',
        freshness: 'current',
        targetSlots: 3,
        activeWorkers: 2,
        startingWorkers: 0,
        drainingWorkers: 1,
        cleanupPendingWorkers: 0,
        eligibleWorkers: 1,
        localDeficit: 0,
        eligibilityDeficit: 2,
        reason: 'none',
        evidence: null,
      },
      targets: [],
    },
    update: {
      status: 'rolling',
      targetImage: null,
      targetImageId: null,
      targetRevision: 'b'.repeat(64),
      currentWorkers: 1,
      staleWorkers: 1,
      lastError: null,
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
        registrationStatus: 'connected',
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
        registrationStatus: 'disconnected',
      },
    ],
    ...overrides,
  };
}

function hostAdmissionResponse(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    status: 'available',
    namespace: 'primary',
    epoch: 3,
    decisionSequence: 42,
    capacityUnits: 12,
    safetyMarginUnits: 2,
    effectiveTotalUnits: 10,
    availableUnits: 4,
    hostPolicyFingerprint: 'host-policy',
    accounting: {
      unitCost: 2,
      reservedUnits: 4,
      borrowable: false,
      profilePolicyFingerprint: 'profile-policy',
      activeUnits: 5,
      provisionalUnits: 0,
      heldUnits: 5,
      borrowedUnits: 1,
      pendingUnits: 4,
      withheldUnits: 4,
    },
    lastDecision: null,
    ...overrides,
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
        hardware: {
          status: 'current',
          collectedAt: '2026-07-19T18:00:00+00:00',
          attemptedAt: '2026-07-19T18:30:00+00:00',
          inventoryHash: 'a'.repeat(64),
          processorModel: 'Example Processor 9000',
          architecture: 'riscv64',
          physicalCoreCount: 10,
          logicalProcessorCount: 20,
          performanceCoreCount: null,
          efficiencyCoreCount: null,
          memoryBytes: 34359738368,
          operatingSystem: 'Docker Desktop',
          kernelVersion: '6.12.34',
          dockerServerVersion: '28.3.3',
          dockerStorageDriver: 'overlayfs',
          dockerBackingFilesystem: 'extfs',
        },
        connectorHealth: {
          nodeId: alphaId,
          receivedAt: '2026-07-19T17:31:00+00:00',
          snapshot: {
            state: 'healthy',
            processStartedAt: '2026-07-18T12:00:00+00:00',
            updatedAt: '2026-07-19T17:31:00+00:00',
            lastAttemptAt: '2026-07-19T17:31:00+00:00',
            lastSuccessAt: '2026-07-19T17:31:00+00:00',
            activeOutageId: null,
            activeOutageStartedAt: null,
            lastFailureAt: '2026-07-19T17:30:00+00:00',
            lastFailureCategory: 'synchronization-network',
            lastFailureProfileId: null,
            lastFailureDetail: 'Connector synchronization could not reach Dashboard.',
            consecutiveFailures: 0,
            nextRetryAt: null,
            lastRecoveredOutageId: 'd6235ec4-2a15-4f91-a9e0-811152869a54',
            lastRecoveredOutageStartedAt: '2026-07-19T17:20:00+00:00',
            lastRecoveredAt: '2026-07-19T17:31:00+00:00',
            lastRecoveredFailureCategory: 'synchronization-network',
          },
        },
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
        hardware: {
          status: 'stale',
          collectedAt: '2026-07-18T18:00:00+00:00',
          attemptedAt: '2026-07-19T17:30:00+00:00',
          inventoryHash: 'b'.repeat(64),
          processorModel: 'Other Processor',
          architecture: 'amd64',
          physicalCoreCount: 6,
          logicalProcessorCount: 12,
          performanceCoreCount: null,
          efficiencyCoreCount: null,
          memoryBytes: 17179869184,
          operatingSystem: 'Docker Desktop',
          kernelVersion: '6.12.34',
          dockerServerVersion: '28.3.3',
          dockerStorageDriver: 'overlayfs',
          dockerBackingFilesystem: 'extfs',
        },
        connectorHealth: {
          nodeId: bravoId,
          receivedAt: '2026-07-19T17:31:00+00:00',
          snapshot: {
            state: 'healthy',
            processStartedAt: '2026-07-18T12:00:00+00:00',
            updatedAt: '2026-07-19T17:31:00+00:00',
            lastAttemptAt: '2026-07-19T17:31:00+00:00',
            lastSuccessAt: '2026-07-19T17:31:00+00:00',
            activeOutageId: null,
            activeOutageStartedAt: null,
            lastFailureAt: '2026-07-19T17:30:00+00:00',
            lastFailureCategory: 'synchronization-network',
            lastFailureProfileId: null,
            lastFailureDetail: 'Connector synchronization could not reach Dashboard.',
            consecutiveFailures: 0,
            nextRetryAt: null,
            lastRecoveredOutageId: 'd6235ec4-2a15-4f91-a9e0-811152869a54',
            lastRecoveredOutageStartedAt: '2026-07-19T17:20:00+00:00',
            lastRecoveredAt: '2026-07-19T17:31:00+00:00',
            lastRecoveredFailureCategory: 'synchronization-network',
          },
        },
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

async function openDisclosure(
  user: ReturnType<typeof userEvent.setup>,
  testId: string,
): Promise<void> {
  const summary = screen.getByTestId(testId).querySelector('summary');
  if (summary == null) throw new Error(`Disclosure ${testId} has no summary.`);
  await user.click(summary);
}

describe('fleet overview and node detail', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
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
    expect(row).toHaveTextContent('Configured 4');
    expect(row).toHaveTextContent('Local 2');
    expect(row).toHaveTextContent('Eligible 1');
    expect(row).toHaveTextContent('1.5 cores / 3 KiB');
    expect(row).toHaveTextContent('2 of 3 sources');
    expect(row).toHaveTextContent('partial');
    expect(screen.queryByText('Absolute maximum')).not.toBeInTheDocument();
    expect(screen.queryByText('build-000001')).not.toBeInTheDocument();
  });

  it('summarizes host admission without duplicating host-wide capacity', async () => {
    const response = fleetResponse();
    response.nodes[1].profiles = [
      profileResponse({ hostAdmission: hostAdmissionResponse() }),
      profileResponse({
        profileId: 'analysis',
        managerInstanceId: 'manager-analysis',
        hostAdmission: hostAdmissionResponse({
          accounting: {
            ...hostAdmissionResponse().accounting,
            activeUnits: 6,
            heldUnits: 6,
            borrowedUnits: 2,
            pendingUnits: 0,
            withheldUnits: 0,
          },
        }),
      }),
    ];
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) return jsonResponse(response);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    render(
      <SessionProvider>
        <RouterProvider router={createTestRouter(features, ['/tenants/local/fleet'])} />
      </SessionProvider>,
    );

    const row = await screen.findByTestId(`fleet-node-${alphaId}`);
    expect(row).toHaveTextContent('available');
    expect(row).toHaveTextContent('4 withheld');
    expect(row).toHaveTextContent('3 borrowed');
    expect(row).not.toHaveTextContent('20 withheld');
  });

  it('labels every offline fleet value as last known and shows the retained cause', async () => {
    renderRoute('/tenants/local/fleet');

    const row = await screen.findByTestId(`fleet-node-${bravoId}`);

    expect(row).toHaveTextContent('Retained cause: synchronization-network');
    expect(within(row).getAllByText(/Last known/).length).toBeGreaterThanOrEqual(3);
    expect(row).toHaveTextContent('Unavailable');
    expect(row).toHaveTextContent('No last-known resource sample');
  });

  it('compares selected node hardware without inferring unavailable fields', async () => {
    const user = userEvent.setup();
    renderRoute('/tenants/local/fleet');

    await screen.findByTestId(`fleet-node-${alphaId}`);
    await user.click(screen.getByLabelText('Compare Alpha'));
    await user.click(screen.getByLabelText('Compare Bravo'));

    const comparison = screen.getByTestId('hardware-comparison');
    expect(within(comparison).getByText('Example Processor 9000')).toBeInTheDocument();
    expect(within(comparison).getByText('Other Processor')).toBeInTheDocument();
    expect(within(comparison).getByText('10 / 20')).toBeInTheDocument();
    expect(within(comparison).getByText('6 / 12')).toBeInTheDocument();
    expect(within(comparison).getByText('32 GiB')).toBeInTheDocument();
    expect(within(comparison).getByText('16 GiB')).toBeInTheDocument();
  });

  it('renders latest reported hardware without implying host liveness', async () => {
    const user = userEvent.setup();
    renderRoute(`/tenants/local/nodes/${alphaId}`);

    await screen.findByRole('heading', { level: 2, name: 'Alpha' });
    await openDisclosure(user, 'node-overview-section-hardware');
    const hardware = await screen.findByTestId('node-hardware');
    expect(hardware).toHaveTextContent('Example Processor 9000');
    expect(hardware).toHaveTextContent('riscv64');
    expect(hardware).toHaveTextContent('10 / 20');
    expect(hardware).toHaveTextContent('Docker Desktop');
    expect(hardware).toHaveTextContent('overlayfs / extfs');
    expect(hardware).toHaveTextContent('latest reported');
    expect(hardware).not.toHaveTextContent(/^current$/i);
  });

  it('does not infer partial node eligibility from older manager contracts', async () => {
    const response = fleetResponse();
    response.nodes[1].profiles = [
      profileResponse(),
      profileResponse({
        profileId: 'legacy',
        managerInstanceId: 'manager-legacy',
        managerContractVersion: 9,
        eligibleSlots: undefined,
        slots: [
          {
            ...profileResponse().slots[0],
            key: 'legacy-000001',
            registrationStatus: undefined,
          },
        ],
      }),
    ];
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/fleet/v1/nodes')) return jsonResponse(response);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    const router = createTestRouter(features, ['/tenants/local/fleet']);
    render(
      <SessionProvider>
        <RouterProvider router={router} />
      </SessionProvider>,
    );

    const row = await screen.findByTestId(`fleet-node-${alphaId}`);
    expect(row).toHaveTextContent('Configured 8');
    expect(row).toHaveTextContent('Local 4');
    expect(row).toHaveTextContent('Eligible Unknown');
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
    const user = userEvent.setup();
    renderRoute(`/tenants/local/nodes/${alphaId}`);

    expect(await screen.findByRole('heading', { level: 2, name: 'Alpha' })).toBeInTheDocument();
    await openDisclosure(user, 'node-overview-section-profiles');
    await openDisclosure(user, 'node-profile-disclosure-build');
    const profile = screen.getByTestId('node-profile-build');
    expect(within(profile).getByRole('link', { name: 'Build' })).toHaveAttribute(
      'href',
      `/tenants/local/nodes/${alphaId}/profiles/build`,
    );
    expect(profile).toHaveTextContent('Configured4');
    expect(profile).toHaveTextContent('Local slots2');
    expect(profile).toHaveTextContent('GitHub eligible1');
    expect(profile).toHaveTextContent('Partial telemetry');
    expect(screen.queryByText('build-000001')).not.toBeInTheDocument();
    const navigation = screen.getByRole('navigation', { name: 'Alpha navigation' });
    expect(within(navigation).getByRole('link', { name: 'Overview' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(within(navigation).getByRole('link', { name: 'History' })).toHaveAttribute(
      'href',
      `/tenants/local/nodes/${alphaId}/history`,
    );
    expect(within(navigation).getByRole('link', { name: 'Administration' })).toHaveAttribute(
      'href',
      `/tenants/local/nodes/${alphaId}/administration`,
    );
    expect(screen.queryByTestId('node-history')).not.toBeInTheDocument();
  });

  it('renders node-not-found, offline, revoked, and empty-profile states', async () => {
    const user = userEvent.setup();
    const router = renderRoute('/tenants/local/nodes/00000000-0000-0000-0000-000000000000');

    expect(await screen.findByText('Node not found')).toBeInTheDocument();
    await act(async () => {
      await router.navigate(`/tenants/local/nodes/${bravoId}`);
    });
    expect(await screen.findByText(/Every connector, profile, capacity/)).toBeInTheDocument();
    await openDisclosure(user, 'node-overview-section-profiles');
    await openDisclosure(user, 'node-overview-section-hardware');
    await openDisclosure(user, 'node-overview-section-connector');
    expect(screen.getByText('No profiles reported')).toBeInTheDocument();
    expect(screen.getByTestId('node-hardware')).toHaveTextContent('last known');
    expect(screen.getByTestId('connector-health-summary')).toHaveTextContent('Recovered outage');
    expect(screen.getByTestId('connector-health-summary')).toHaveTextContent(
      'synchronization-network',
    );

    await act(async () => {
      await router.navigate(`/tenants/local/nodes/${charlieId}`);
    });
    expect(await screen.findByText(/This node is revoked/)).toBeInTheDocument();
  });

  it('prepares a schema-bound diagnostics context for read-only users', async () => {
    const viewerSession = {
      ...ownerSession,
      tenants: [{ tenantId: 'local', displayName: 'Local', role: 'viewer' as const }],
    };
    const createObjectURL = vi.fn((value: Blob | MediaSource) => {
      void value;
      return 'blob:diagnostics-context';
    });
    const revokeObjectURL = vi.fn();
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    const user = userEvent.setup();
    renderRoute(`/tenants/local/nodes/${bravoId}`, viewerSession);
    vi.stubGlobal('URL', {
      createObjectURL,
      revokeObjectURL,
    });

    await user.click(await screen.findByTestId(`prepare-diagnostics-${bravoId}`));

    expect(click).toHaveBeenCalledOnce();
    expect(createObjectURL).toHaveBeenCalledOnce();
    const downloadedBlob = createObjectURL.mock.calls[0]?.[0];
    if (!(downloadedBlob instanceof Blob)) {
      throw new Error('Diagnostics blob was not captured.');
    }
    const context = JSON.parse(await downloadedBlob.text()) as {
      schemaVersion: number;
      diagnosticMode: string;
      dashboard: { nodeId: string; incident: string };
    };
    expect(context.schemaVersion).toBe(1);
    expect(context.diagnosticMode).toBe('ConnectorOffline');
    expect(context.dashboard.nodeId).toBe(bravoId);
    expect(context.dashboard.incident).toBe('synchronization-network');
    expect(await screen.findByText(/Diagnostics context downloaded/)).toHaveTextContent(
      'Diagnostics context downloaded',
    );

    await openDisclosure(user, 'node-overview-section-profiles');
    expect(screen.getByText('No profiles reported')).toBeInTheDocument();
  });

  it('shows a compact mobile section index before detailed node evidence', async () => {
    const user = userEvent.setup();
    renderRoute(`/tenants/local/nodes/${alphaId}`);

    await screen.findByRole('heading', { level: 2, name: 'Alpha' });
    for (const testId of [
      'node-overview-section-identity',
      'node-overview-section-pressure',
      'node-overview-section-connector',
      'node-overview-section-hardware',
      'node-overview-section-profiles',
    ]) {
      const summary = screen.getByTestId(testId).querySelector('summary');
      expect(summary).not.toBeNull();
      expect(summary).toBeVisible();
    }

    expect(screen.getByTestId('node-overview-section-identity')).not.toHaveAttribute('open');
    expect(screen.getByTestId('node-overview-section-pressure')).not.toHaveAttribute('open');
    expect(screen.getByTestId('node-overview-section-profiles')).not.toHaveAttribute('open');

    await openDisclosure(user, 'node-overview-section-profiles');
    expect(screen.getByTestId('node-overview-section-profiles')).toHaveAttribute('open');
    expect(
      screen.getByTestId('node-profile-disclosure-build').querySelector('summary'),
    ).toBeVisible();
    expect(screen.getByTestId('node-profile-disclosure-build')).not.toHaveAttribute('open');

    await openDisclosure(user, 'node-profile-disclosure-build');
    expect(screen.getByTestId('node-profile-disclosure-build')).toHaveAttribute('open');
    expect(
      within(screen.getByTestId('node-profile-build')).getByRole('link', { name: 'Build' }),
    ).toBeVisible();
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
    const router = createTestRouter(features, [`/tenants/local/nodes/${alphaId}/administration`]);
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
    const router = renderRoute(`/tenants/local/nodes/${alphaId}`, {
      ...ownerSession,
      tenants: [{ ...ownerSession.tenants[0], role: 'viewer' }],
    });

    expect(await screen.findByRole('heading', { level: 2, name: 'Alpha' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Server display name')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rotate credential' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Revoke' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Administration' })).not.toBeInTheDocument();

    await act(async () => {
      await router.navigate(`/tenants/local/nodes/${alphaId}/administration`);
    });
    expect(await screen.findByText('Insufficient tenant role')).toBeInTheDocument();
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
    const router = createTestRouter(features, [`/tenants/local/nodes/${alphaId}/administration`]);
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
