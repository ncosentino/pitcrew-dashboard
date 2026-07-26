import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider } from '@/core/auth';
import { createTestRouter } from '@/core/routing/createAppRouter';
import { features } from '@/features.registry';

const nodeId = 'a6235ec4-2a15-4f91-a9e0-811152869a51';
const profilePath = `/tenants/local/nodes/${nodeId}/profiles/default`;

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function session(role: 'viewer' | 'administrator' | 'owner' = 'owner') {
  return {
    user: {
      githubUserId: '123',
      githubLogin: 'operator',
      displayName: 'Operator',
      avatarUrl: null,
    },
    isSystemAdministrator: false,
    tenants: [{ tenantId: 'local', displayName: 'Local', role }],
    antiforgeryToken: 'test-antiforgery-token',
  };
}

function slotResponse(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    key: 'repo-default-000001',
    repository: 'https://github.com/example/project',
    desired: true,
    processRunning: true,
    state: 'online',
    failureCount: 2,
    backoffSeconds: 0,
    updatedAt: '2026-07-19T18:30:00+00:00',
    resources: {
      cpuCores: 1.25,
      memoryWorkingSetBytes: 268_435_456,
      pids: 12,
    },
    activity: 'busy',
    target: 'scale-set-linux',
    registrationStatus: 'connected',
    ...overrides,
  };
}

function profileResponse(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    schemaVersion: 1,
    managerContractVersion: 10,
    profileId: 'default',
    managerInstanceId: 'manager-default',
    managerStatus: 'running',
    observedAt: '2026-07-19T18:30:00+00:00',
    scope: 'repo',
    generation: 4,
    desiredStateHash: 'a'.repeat(64),
    desiredStateStatus: 'accepted',
    configuredSlots: 30,
    desiredSlots: 1,
    activeSlots: 1,
    eligibleSlots: 1,
    drainingSlots: 0,
    slots: [slotResponse()],
    resourceTelemetry: {
      sampledAt: '2026-07-19T18:30:00+00:00',
      status: 'partial',
      host: null,
      manager: {
        cpuCores: 0.5,
        memoryWorkingSetBytes: 134_217_728,
        pids: 8,
      },
    },
    autoscaling: {
      mode: 'scale-set',
      status: 'degraded',
      minimumIdleSlots: 1,
      maximumSlots: 30,
      targetSlots: 3,
      assignedJobs: 3,
      runningJobs: 2,
      availableJobs: 1,
      idleRunners: 1,
      busyRunners: 2,
      scaleDownDelaySeconds: 300,
      scaleSetCount: 1,
      scaleDownAt: null,
      lastError: 'GitHub queue observation failed.',
    },
    ...overrides,
  };
}

function capacityControl(latestCommand: unknown | null = null) {
  return {
    profileId: 'default',
    generation: 7,
    currentMaximum: 30,
    maximumAllowed: 50,
    latestCommand,
  };
}

function nodeResponse(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    nodeId,
    displayName: 'Resource server',
    connectorVersion: '2.0.0',
    enrolledAt: '2026-07-18T15:00:00+00:00',
    lastSeenAt: '2026-07-19T18:30:05+00:00',
    isOnline: true,
    isRevoked: false,
    credentialRotationRequested: false,
    profiles: [profileResponse()],
    capacityControls: [capacityControl()],
    ...overrides,
  };
}

function fleetResponse(nodes: ReadonlyArray<unknown> = [nodeResponse()]) {
  return {
    generatedAt: '2026-07-19T18:30:05+00:00',
    nodes,
  };
}

function renderProfile(
  fleet: unknown,
  role: 'viewer' | 'administrator' | 'owner' = 'owner',
  fetchOverride?: typeof fetch,
) {
  vi.spyOn(globalThis, 'fetch').mockImplementation(
    fetchOverride ??
      (async (input) => {
        if (String(input).endsWith('/api/session')) return jsonResponse(session(role));
        return jsonResponse(fleet);
      }),
  );
  const router = createTestRouter(features, [profilePath]);
  render(
    <SessionProvider>
      <RouterProvider router={router} />
    </SessionProvider>,
  );
  return router;
}

function command(status: 'pending' | 'delivered' | 'succeeded' | 'rejected' | 'failed') {
  return {
    commandId: '729cb29e-21d9-4510-a285-397483891dc2',
    requestedMaximum: 40,
    status,
    requestedAt: '2026-07-24T12:00:00+00:00',
    deliveredAt: status === 'pending' ? null : '2026-07-24T12:00:01+00:00',
    completedAt:
      status === 'pending' || status === 'delivered' ? null : '2026-07-24T12:00:02+00:00',
    resultMessage: status === 'succeeded' ? 'Maximum updated.' : null,
  };
}

describe('ProfileDetailPage', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders autoscaling, partial telemetry, and complete slot diagnostics', async () => {
    renderProfile(fleetResponse());

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Profile default' }),
    ).toBeInTheDocument();
    expect(await screen.findByTestId('profile-capacity-target-default')).toHaveTextContent('3');
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(
      screen.getByText('repo scope · generation 4 · manager contract 10', { exact: false }),
    ).toBeInTheDocument();
    expect(screen.getByTestId('profile-autoscaling-error-default')).toHaveTextContent(
      'GitHub queue observation failed.',
    );
    expect(screen.getByTestId('profile-capacity-eligible-default')).toHaveTextContent('1');
    expect(screen.getByTestId('profile-resource-telemetry-default')).toHaveTextContent('partial');
    expect(screen.getByTestId('profile-resource-host-default')).toHaveTextContent('Unavailable');
    expect(screen.getByTestId('profile-resource-manager-default')).toHaveTextContent(
      '0.5 cores · 128 MiB · 8 PIDs',
    );
    expect(screen.getByTestId('profile-resource-workers-default')).toHaveTextContent(
      '1.25 cores · 256 MiB · 12 PIDs',
    );

    const table = screen.getByRole('table', { name: 'Slots for profile default' });
    expect(within(table).getByRole('columnheader', { name: 'Repository' })).toBeInTheDocument();
    expect(within(table).getByText('https://github.com/example/project')).toBeInTheDocument();
    expect(within(table).getByText('scale-set-linux')).toBeInTheDocument();
    expect(within(table).getByText('busy')).toBeInTheDocument();
    expect(within(table).getByText('connected')).toBeInTheDocument();
    expect(within(table).getByText('online')).toBeInTheDocument();
    expect(within(table).getByText('2')).toBeInTheDocument();
  });

  it('renders fixed capacity, unavailable telemetry, no slots, and no control for viewers', async () => {
    const profile = profileResponse({
      autoscaling: null,
      resourceTelemetry: null,
      slots: [],
      desiredSlots: 0,
      activeSlots: 0,
      eligibleSlots: 0,
      configuredSlots: 2,
    });
    renderProfile(fleetResponse([nodeResponse({ profiles: [profile] })]), 'viewer');

    expect(await screen.findByText('Fixed capacity')).toBeInTheDocument();
    expect(screen.getByTestId('profile-capacity-configured-default')).toHaveTextContent('2');
    expect(screen.queryByLabelText('Absolute maximum')).not.toBeInTheDocument();
    expect(screen.getByTestId('profile-resource-host-default')).toHaveTextContent('Unavailable');
    expect(screen.getByTestId('profile-resource-manager-default')).toHaveTextContent('Unavailable');
    expect(
      screen.getByText('The manager has not reported any slots for this profile.'),
    ).toBeInTheDocument();
  });

  it('treats registration eligibility from older manager contracts as unknown', async () => {
    const legacySlot = slotResponse({ registrationStatus: undefined });
    const profile = profileResponse({
      managerContractVersion: 9,
      eligibleSlots: undefined,
      slots: [legacySlot],
    });
    renderProfile(fleetResponse([nodeResponse({ profiles: [profile] })]));

    expect(await screen.findByTestId('profile-capacity-eligible-default')).toHaveTextContent(
      'Unknown',
    );
    expect(screen.getByTestId('slot-registration-repo-default-000001')).toHaveTextContent(
      'unknown',
    );
  });

  it.each([
    ['offline', { isOnline: false }, 'offline'],
    ['revoked', { isRevoked: true }, 'revoked'],
  ])('disables capacity changes for %s nodes', async (_name, nodeOverrides, status) => {
    renderProfile(fleetResponse([nodeResponse(nodeOverrides)]));

    const input = await screen.findByLabelText('Absolute maximum');
    expect(input).toBeDisabled();
    expect(screen.getByTestId('profile-node-unavailable')).toHaveTextContent(status);
  });

  it.each(['stale', 'stopped'] as const)('calls out a %s manager', async (managerStatus) => {
    renderProfile(
      fleetResponse([
        nodeResponse({ profiles: [profileResponse({ managerStatus, autoscaling: null })] }),
      ]),
    );

    expect(await screen.findByTestId('profile-manager-unavailable')).toHaveTextContent(
      `manager is ${managerStatus}`,
    );
  });

  it.each(['pending', 'delivered', 'succeeded', 'rejected', 'failed'] as const)(
    'preserves the %s capacity command state',
    async (status) => {
      renderProfile(
        fleetResponse([
          nodeResponse({
            capacityControls: [capacityControl(command(status))],
          }),
        ]),
      );

      const control = await screen.findByTestId('profile-capacity-control-default');
      expect(control).toHaveTextContent(status);
      expect(control).toHaveTextContent('Requested 40');
      const input = screen.getByLabelText('Absolute maximum');
      if (status === 'pending' || status === 'delivered') {
        expect(input).toBeDisabled();
      } else {
        expect(input).toBeEnabled();
      }
    },
  );

  it('confirms and queues an absolute maximum while exposing mutation progress', async () => {
    let completeMutation!: (response: Response) => void;
    const mutation = new Promise<Response>((resolve) => {
      completeMutation = resolve;
    });
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(session());
      if (init?.method === 'POST') return await mutation;
      return jsonResponse(fleetResponse());
    });
    renderProfile(fleetResponse(), 'owner', fetchMock);
    const user = userEvent.setup();

    const input = await screen.findByLabelText('Absolute maximum');
    await user.clear(input);
    await user.type(input, '40');
    await user.click(screen.getByRole('button', { name: 'Queue change' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('Set default capacity maximum to 40?')).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
    await user.click(within(dialog).getByRole('button', { name: 'Confirm capacity change' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Queuing capacity change');
    expect(input).toBeDisabled();
    const request = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST');
    expect(request).toBeDefined();
    const [url, init] = request ?? [];
    expect(String(url)).toMatch(new RegExp(`/nodes/${nodeId}/profiles/default/capacity-maximum$`));
    expect(new Headers(init?.headers).get('X-PitCrew-Antiforgery')).toBe('test-antiforgery-token');
    expect(JSON.parse(String(init?.body))).toEqual({ maximum: 40 });

    completeMutation(
      jsonResponse({
        commandId: '729cb29e-21d9-4510-a285-397483891dc2',
        status: 'pending',
      }),
    );
    await waitFor(() => expect(screen.queryByRole('status')).not.toBeInTheDocument());
  });

  it.each([
    ['node', fleetResponse([]), 'Node not found'],
    [
      'profile',
      fleetResponse([nodeResponse({ profiles: [], capacityControls: [] })]),
      'Profile not found',
    ],
  ])('renders an explicit %s-not-found state', async (_kind, fleet, message) => {
    renderProfile(fleet);

    expect(await screen.findByText(message)).toBeInTheDocument();
  });
});
