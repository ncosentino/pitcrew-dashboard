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

function recoveryCommand(
  status:
    | 'queued'
    | 'claimed'
    | 'started'
    | 'succeeded'
    | 'rejected'
    | 'failed'
    | 'expired'
    | 'indeterminate',
  overrides: Readonly<Record<string, unknown>> = {},
) {
  return {
    commandId: '2b7f1e3c-7b0f-4a55-9a3f-9e2a4c1d5b60',
    status,
    failureCategory: null,
    requestedByGitHubUserId: '123',
    requestedAt: '2026-07-19T18:20:00+00:00',
    expiresAt: '2026-07-19T18:30:00+00:00',
    deliveredAt: null,
    claimedAt: null,
    startedAt: null,
    completedAt: null,
    beforeManagerInstanceId: 'manager-default',
    afterManagerInstanceId: null,
    resultMessage: null,
    ...overrides,
  };
}

function recoveryControl(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    profileId: 'default',
    managerContractVersion: 10,
    managerContractSupported: true,
    expectedManagerInstanceId: 'manager-default',
    desiredGeneration: 4,
    desiredStateHash: 'a'.repeat(64),
    observedStateAgeSeconds: 5,
    observedStateMaximumAgeSeconds: 120,
    recoveryAllowed: true,
    singleManagerResolved: true,
    operationActive: false,
    latestCommand: null,
    recentCommands: [],
    ...overrides,
  };
}

function recoveryFleet(
  controlOverrides: Readonly<Record<string, unknown>> = {},
  nodeOverrides: Readonly<Record<string, unknown>> = {},
) {
  return fleetResponse([
    nodeResponse({
      recoveryControls: [recoveryControl(controlOverrides)],
      ...nodeOverrides,
    }),
  ]);
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

const imageDigest = `sha256:${'ab'.repeat(32)}`;

function statistics(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    observedAt: '2026-07-19T18:29:30+00:00',
    availableJobs: 1,
    acquiredJobs: 1,
    assignedJobs: 3,
    runningJobs: 2,
    registeredRunners: 8,
    busyRunners: 2,
    idleRunners: 1,
    ...overrides,
  };
}

function target(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    key: 'scale-set-linux',
    repository: 'https://github.com/example/project',
    maximumSlots: 30,
    targetSlots: 3,
    localActiveWorkers: 2,
    localIdleWorkers: 1,
    localBusyWorkers: 2,
    localDrainingWorkers: 0,
    statistics: statistics(),
    ...overrides,
  };
}

function contractElevenProfile(
  targets: ReadonlyArray<unknown> = [target()],
  slotOverrides: Readonly<Record<string, unknown>> = {},
) {
  return profileResponse({
    managerContractVersion: 11,
    resourcePolicy: {
      memoryBytes: 2_147_483_648,
      memorySwapBytes: 4_294_967_296,
      cpuCores: '2.5',
      pids: 512,
    },
    slots: [
      slotResponse({
        imageId: imageDigest,
        resources: {
          cpuCores: 1.25,
          memoryWorkingSetBytes: 268_435_456,
          pids: 12,
          networkRxBytes: 0,
          networkTxBytes: 1_048_576,
          blockReadBytes: null,
          blockWriteBytes: null,
        },
        lastExit: {
          observedAt: '2026-07-19T18:20:00+00:00',
          classification: 'unknown',
          exitCode: null,
          signal: null,
          dockerOomKilled: null,
          evidence: 'unavailable',
        },
        ...slotOverrides,
      }),
    ],
    autoscaling: {
      mode: 'scale-set',
      status: 'running',
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
      lastError: null,
      maximumActiveWorkers: 24,
      targets,
    },
  });
}

function contractElevenFleet(
  targets: ReadonlyArray<unknown> = [target()],
  slotOverrides: Readonly<Record<string, unknown>> = {},
) {
  return fleetResponse([
    nodeResponse({ profiles: [contractElevenProfile(targets, slotOverrides)] }),
  ]);
}

describe('ProfileDetailPage', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders autoscaling, partial telemetry, and complete slot diagnostics', async () => {
    renderProfile(fleetResponse());

    expect(
      await screen.findByTestId('profile-capacity-target-default', {}, { timeout: 5_000 }),
    ).toHaveTextContent('3');
    expect(screen.getByRole('heading', { level: 1, name: 'Profile default' })).toBeInTheDocument();
    expect(screen.getAllByRole('heading', { name: 'Profile default' })).toHaveLength(1);
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
    expect(
      screen.getByRole('region', { name: 'Scrollable worker slots for profile default' }),
    ).toHaveAttribute('tabindex', '0');
    expect(within(table).getByRole('columnheader', { name: 'Repository' })).toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: 'Status' })).toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: 'Resources' })).toBeInTheDocument();
    expect(
      within(table).queryByRole('columnheader', { name: 'CPU cores' }),
    ).not.toBeInTheDocument();
    expect(within(table).getByText('Job')).toBeInTheDocument();
    expect(within(table).getByText('GitHub')).toBeInTheDocument();
    expect(within(table).getByText('Local')).toBeInTheDocument();
    expect(within(table).getByText('Job activity: busy.')).toHaveClass('sr-only');
    expect(within(table).getByText('GitHub registration: connected.')).toHaveClass('sr-only');
    expect(within(table).getByText('Local state: online.')).toHaveClass('sr-only');
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

  it('renders contract 11 policy, admission ceiling, image identity, I/O, and exit evidence', async () => {
    renderProfile(contractElevenFleet());

    expect(await screen.findByTestId('profile-resource-policy-default')).not.toHaveAttribute(
      'open',
    );
    expect(screen.getByTestId('profile-targets-default')).not.toHaveAttribute('open');
    expect(screen.getByTestId('profile-resource-telemetry-default')).not.toHaveAttribute('open');
    expect(await screen.findByTestId('profile-policy-memory-default')).toHaveTextContent('2 GiB');
    expect(screen.getByTestId('profile-policy-memory-plus-swap-default')).toHaveTextContent(
      '4 GiB',
    );
    expect(screen.getByTestId('profile-policy-cpu-default')).toHaveTextContent('2.5 cores');
    expect(screen.getByTestId('profile-policy-pids-default')).toHaveTextContent('512 PIDs');
    expect(screen.getByTestId('profile-admission-ceiling-default')).toHaveTextContent(
      '24 active workers',
    );

    const network = screen.getByTestId('slot-network-repo-default-000001');
    expect(network).toHaveTextContent('0 B in · 1 MiB out');
    expect(screen.getByTestId('slot-block-io-repo-default-000001')).toHaveTextContent(
      'Unavailable read · Unavailable written',
    );
    expect(screen.getByTestId('slot-image-repo-default-000001')).toHaveTextContent('ababababab');

    const lastExit = screen.getByTestId('slot-last-exit-repo-default-000001');
    expect(lastExit).toHaveTextContent('unknown');
    expect(lastExit).not.toHaveTextContent('clean');
    expect(screen.queryByText('Last error: None')).not.toBeInTheDocument();
  });

  it('describes absent exit evidence without calling it clean', async () => {
    renderProfile(contractElevenFleet([target()], { lastExit: null }));

    const lastExit = await screen.findByTestId('slot-last-exit-repo-default-000001');
    expect(within(lastExit).getByText('Not recorded')).toBeInTheDocument();
    expect(lastExit).toHaveTextContent(
      'No exit evidence has been recorded for this worker, which does not mean it exited cleanly.',
    );
    expect(within(lastExit).getByTitle(/No exit evidence has been recorded/)).toHaveClass(
      'whitespace-nowrap',
    );
  });

  it('surfaces registration divergence when GitHub reports more registrations than local workers', async () => {
    renderProfile(contractElevenFleet());

    const divergence = await screen.findByTestId('target-divergence-default-scale-set-linux');
    expect(screen.getByTestId('profile-targets-default')).toHaveTextContent('1 warning');
    expect(divergence).toHaveTextContent(
      'GitHub reports 8 registered runners while 2 local worker containers are live.',
    );
    expect(divergence).toHaveTextContent('is not proof that it can be removed');
  });

  it('surfaces divergence when GitHub reports no registrations for live workers', async () => {
    renderProfile(
      contractElevenFleet([target({ statistics: statistics({ registeredRunners: 0 }) })]),
    );

    const divergence = await screen.findByTestId('target-divergence-default-scale-set-linux');
    expect(divergence).toHaveTextContent(
      '2 local worker containers are live while GitHub reports 0 registered runners.',
    );
    expect(divergence).toHaveTextContent('is not proof that it is eligible for work');
  });

  it('marks stale GitHub statistics without collapsing them into local evidence', async () => {
    renderProfile(
      contractElevenFleet([
        target({
          statistics: statistics({
            observedAt: '2026-07-19T18:00:00+00:00',
            registeredRunners: 2,
            busyRunners: 1,
            idleRunners: 1,
          }),
        }),
      ]),
    );

    expect(await screen.findByTestId('target-freshness-default-scale-set-linux')).toHaveTextContent(
      'Stale',
    );
    expect(screen.getByTestId('profile-targets-default')).toHaveTextContent('1 stale');
    expect(screen.getByTestId('target-local-default-scale-set-linux')).toHaveTextContent(
      '2 live · 1 idle · 2 busy · 0 draining',
    );
    expect(screen.getByTestId('target-github-default-scale-set-linux')).toHaveTextContent(
      '2 registered · 1 busy · 1 idle',
    );
  });

  it('keeps unavailable GitHub statistics distinct from measured zero', async () => {
    renderProfile(contractElevenFleet([target({ statistics: null })]));

    expect(await screen.findByTestId('target-github-default-scale-set-linux')).toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId('target-jobs-default-scale-set-linux')).toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId('target-divergence-default-scale-set-linux')).toHaveTextContent(
      'GitHub statistics are unavailable, so registration divergence cannot be assessed.',
    );
  });

  it('marks contract 10 profiles as lacking contract 11 evidence', async () => {
    renderProfile(fleetResponse());

    expect(await screen.findByTestId('profile-policy-memory-default')).toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId('profile-admission-ceiling-default')).toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId('profile-targets-default')).toHaveTextContent(
      'does not report per-target scale-set evidence',
    );
  });

  it.each([
    ['insufficient authorization', 'viewer', recoveryFleet(), 'not-authorized'],
    ['read-only connector', 'owner', fleetResponse([nodeResponse()]), 'connector-read-only'],
    ['revoked node', 'owner', recoveryFleet({}, { isRevoked: true }), 'node-revoked'],
    ['offline connector', 'owner', recoveryFleet({}, { isOnline: false }), 'connector-offline'],
    [
      'locally disallowed profile',
      'owner',
      recoveryFleet({ recoveryAllowed: false }),
      'locally-disallowed',
    ],
    [
      'legacy manager contract',
      'owner',
      recoveryFleet({ managerContractVersion: 8, managerContractSupported: false }),
      'legacy-contract',
    ],
    [
      'stopped manager',
      'owner',
      fleetResponse([
        nodeResponse({
          profiles: [profileResponse({ managerStatus: 'stopped' })],
          recoveryControls: [recoveryControl()],
        }),
      ]),
      'manager-not-running',
    ],
    [
      'multiple managers',
      'owner',
      recoveryFleet({ singleManagerResolved: false }),
      'manager-unresolved',
    ],
    [
      'stale observation',
      'owner',
      recoveryFleet({ observedStateAgeSeconds: 600 }),
      'observation-stale',
    ],
    [
      'stale connector capability',
      'owner',
      recoveryFleet({}, { lastSeenAt: '2026-07-19T18:20:00+00:00' }),
      'observation-stale',
    ],
    [
      'active local operation',
      'owner',
      recoveryFleet({ operationActive: true }),
      'operation-active',
    ],
    [
      'active recovery command',
      'owner',
      recoveryFleet({ latestCommand: recoveryCommand('queued') }),
      'recovery-active',
    ],
    [
      'active capacity command',
      'owner',
      fleetResponse([
        nodeResponse({
          capacityControls: [capacityControl(command('pending'))],
          recoveryControls: [recoveryControl()],
        }),
      ]),
      'capacity-active',
    ],
  ])('explains why recovery is unavailable for %s', async (_name, role, fleet, reason) => {
    renderProfile(fleet, role as 'viewer' | 'owner');

    const action = await screen.findByTestId('profile-recovery-action-default');
    expect(action).toBeDisabled();
    const explanation = screen.getByTestId('profile-recovery-unavailable-default');
    expect(explanation).toHaveAttribute('data-reason', reason);
    expect(action).toHaveAttribute('aria-describedby', explanation.id);
  });

  it('confirms fenced recovery, queues it once, and blocks duplicate requests', async () => {
    const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(session());
      if (init?.method === 'POST') {
        return jsonResponse(
          { commandId: '2b7f1e3c-7b0f-4a55-9a3f-9e2a4c1d5b60', status: 'queued' },
          202,
        );
      }
      return jsonResponse(recoveryFleet());
    });
    renderProfile(recoveryFleet(), 'owner', fetchMock);
    const user = userEvent.setup();

    const action = await screen.findByTestId('profile-recovery-action-default');
    expect(action).toBeEnabled();
    await user.click(action);

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByTestId('profile-recovery-fences-default')).toHaveTextContent(
      'manager-default · generation 4 · hash aaaaaaaaaaaa',
    );
    expect(within(dialog).getByTestId('profile-recovery-counts-default')).toHaveTextContent(
      'configured 30 · target 3 · local 1 · GitHub eligible 1',
    );
    expect(
      within(dialog).getByText(/restarts this one profile manager exactly once/),
    ).toBeVisible();
    expect(
      within(dialog).getByText(/No worker, Docker daemon or Desktop, host, capacity/),
    ).toBeVisible();
    expect(within(dialog).getByText(/can still fail or end indeterminate/)).toBeVisible();

    const confirm = within(dialog).getByRole('button', { name: 'Queue manager recovery' });
    expect(confirm).toBeDisabled();
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);

    await user.click(within(dialog).getByRole('checkbox'));
    expect(confirm).toBeEnabled();
    await user.click(confirm);

    await waitFor(() =>
      expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1),
    );
    const request = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST');
    const [url, init] = request ?? [];
    expect(String(url)).toMatch(new RegExp(`/nodes/${nodeId}/profiles/default/manager-recovery$`));
    expect(new Headers(init?.headers).get('X-PitCrew-Antiforgery')).toBe('test-antiforgery-token');
    expect(JSON.parse(String(init?.body))).toEqual({
      expectedManagerInstanceId: 'manager-default',
      expectedGeneration: 4,
      expectedDesiredStateHash: 'a'.repeat(64),
    });

    await user.click(screen.getByTestId('profile-recovery-action-default'));
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
  });

  it.each([
    ['rejected', 'not-allowed'],
    ['failed', 'process-failure'],
    ['indeterminate', 'interrupted'],
    ['expired', 'expired'],
  ] as const)('keeps the terminal %s outcome visible', async (status, failureCategory) => {
    renderProfile(
      recoveryFleet({
        latestCommand: recoveryCommand(status, {
          failureCategory,
          completedAt: '2026-07-19T18:25:00+00:00',
          resultMessage: 'Local investigation is required.',
        }),
        recentCommands: [
          recoveryCommand(status, {
            failureCategory,
            completedAt: '2026-07-19T18:25:00+00:00',
            resultMessage: 'Local investigation is required.',
          }),
        ],
      }),
    );

    const progress = await screen.findByTestId('profile-recovery-progress-default');
    expect(progress).toHaveTextContent(status);
    expect(screen.getByTestId('profile-recovery-failure-default')).toHaveTextContent(
      failureCategory,
    );
    expect(progress).not.toHaveTextContent('succeeded');
    expect(screen.getByTestId('profile-recovery-history-default')).toHaveTextContent(status);
    expect(screen.getByTestId('profile-recovery-worker-note-default')).toHaveTextContent(
      'no worker-directed mutation',
    );
  });

  it('reports the manager instance transition and immutable history newest first', async () => {
    renderProfile(
      recoveryFleet({
        latestCommand: recoveryCommand('succeeded', {
          commandId: '5f5a4a0e-6c1e-4d1a-9a05-1f4ec1f0f0aa',
          requestedAt: '2026-07-19T18:24:00+00:00',
          afterManagerInstanceId: 'manager-default-2',
          completedAt: '2026-07-19T18:25:00+00:00',
          resultMessage: 'Manager was restarted.',
        }),
        recentCommands: [
          recoveryCommand('succeeded', {
            commandId: '5f5a4a0e-6c1e-4d1a-9a05-1f4ec1f0f0aa',
            requestedAt: '2026-07-19T18:24:00+00:00',
            afterManagerInstanceId: 'manager-default-2',
            completedAt: '2026-07-19T18:25:00+00:00',
            resultMessage: 'Manager was restarted.',
          }),
          recoveryCommand('rejected', {
            commandId: '9b1c5c6d-2f4b-4bb0-9c86-2a3d4f5e6a7b',
            failureCategory: 'stale-fence',
            requestedAt: '2026-07-19T18:10:00+00:00',
            completedAt: '2026-07-19T18:11:00+00:00',
          }),
        ],
      }),
    );

    expect(await screen.findByTestId('profile-recovery-transition-default')).toHaveTextContent(
      'manager-default → manager-default-2',
    );
    const history = screen.getByRole('table', {
      name: 'Immutable recovery history for profile default',
    });
    const rows = within(history).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('succeeded');
    expect(rows[1]).toHaveTextContent('rejected');
    expect(rows[1]).toHaveTextContent('stale-fence');
    expect(rows[0]).toHaveTextContent('123');
  });

  it('keeps capacity and recovery mutually exclusive', async () => {
    renderProfile(recoveryFleet({ latestCommand: recoveryCommand('started') }));

    expect(await screen.findByLabelText('Absolute maximum')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Queue change' })).toBeDisabled();
    expect(screen.getByTestId('profile-recovery-unavailable-default')).toHaveAttribute(
      'data-reason',
      'recovery-active',
    );
  });

  it('tells viewers that recovery has never run without offering the action', async () => {
    renderProfile(recoveryFleet(), 'viewer');

    expect(await screen.findByTestId('profile-recovery-empty-default')).toHaveTextContent(
      'No manager recovery has been requested',
    );
    expect(screen.getByRole('heading', { name: 'Manager recovery' })).toBeInTheDocument();
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
