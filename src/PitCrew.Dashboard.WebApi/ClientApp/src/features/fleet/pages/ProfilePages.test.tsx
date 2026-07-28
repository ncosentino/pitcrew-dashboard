import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider } from '@/core/auth';
import { createTestRouter } from '@/core/routing/createAppRouter';
import { features } from '@/features.registry';

const nodeId = 'a6235ec4-2a15-4f91-a9e0-811152869a51';
const profilePath = `/tenants/local/nodes/${nodeId}/profiles/default`;

function profileRoute(section?: 'capacity' | 'workers' | 'diagnostics' | 'history' | 'recovery') {
  return section ? `${profilePath}/${section}` : profilePath;
}

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
  path = profilePath,
  fetchOverride?: typeof fetch,
) {
  vi.spyOn(globalThis, 'fetch').mockImplementation(
    fetchOverride ??
      (async (input) => {
        if (String(input).endsWith('/api/session')) return jsonResponse(session(role));
        return jsonResponse(fleet);
      }),
  );
  const router = createTestRouter(features, [path]);
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

function managerEvent(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    sequence: 41,
    managerInstanceId: 'manager-default',
    observedAt: '2026-07-19T18:29:00+00:00',
    subsystem: 'docker',
    operation: 'docker-run',
    target: 'repo-default-000001',
    outcome: 'retry-scheduled',
    durationMilliseconds: 1_200,
    attempt: 3,
    consecutiveFailures: 2,
    retryAt: '2026-07-19T18:30:30+00:00',
    reason: 'docker-failed',
    evidence: 'Docker refused to start the worker container.',
    ...overrides,
  };
}

function operationJournal(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    status: 'current',
    capacity: 32,
    highestSequence: 41,
    droppedEvents: 0,
    events: [
      managerEvent({
        sequence: 40,
        managerInstanceId: 'manager-previous',
        observedAt: '2026-07-19T18:28:00+00:00',
        subsystem: 'recovery',
        operation: 'manager-start',
        target: null,
        outcome: 'succeeded',
        durationMilliseconds: 0,
        attempt: null,
        consecutiveFailures: null,
        retryAt: null,
        reason: 'none',
        evidence: null,
      }),
      managerEvent(),
    ],
    ...overrides,
  };
}

function subsystemHealth(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    docker: {
      state: 'degraded',
      observedAt: '2026-07-19T18:30:00+00:00',
      consecutiveFailures: 2,
      retryAt: '2026-07-19T18:30:30+00:00',
      lastSuccess: {
        operation: 'docker-ping',
        observedAt: '2026-07-19T18:25:00+00:00',
        durationMilliseconds: 4,
        reason: 'none',
        evidence: null,
      },
      lastFailure: {
        operation: 'docker-run',
        observedAt: '2026-07-19T18:29:00+00:00',
        durationMilliseconds: 1_200,
        reason: 'docker-failed',
        evidence: 'Docker refused to start the worker container.',
      },
    },
    github: {
      state: 'unknown',
      observedAt: '2026-07-19T18:30:00+00:00',
      consecutiveFailures: 0,
      retryAt: null,
      lastSuccess: null,
      lastFailure: null,
    },
    ...overrides,
  };
}

function targetDeficit(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    key: 'scale-set-linux',
    repository: 'https://github.com/example/project',
    observedAt: '2026-07-19T18:30:00+00:00',
    freshness: 'current',
    targetSlots: 3,
    activeWorkers: 2,
    startingWorkers: 0,
    drainingWorkers: 0,
    cleanupPendingWorkers: 0,
    eligibleWorkers: 1,
    localDeficit: 1,
    eligibilityDeficit: 2,
    reason: 'docker-failed',
    evidence: 'Docker refused to start the worker container.',
    ...overrides,
  };
}

function contractTwelveProfile(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    ...contractElevenProfile(),
    managerContractVersion: 12,
    operationJournal: operationJournal(),
    subsystemHealth: subsystemHealth(),
    capacityEvidence: { fixed: null, targets: [targetDeficit()] },
    ...overrides,
  };
}

function contractTwelveFleet(overrides: Readonly<Record<string, unknown>> = {}) {
  return fleetResponse([nodeResponse({ profiles: [contractTwelveProfile(overrides)] })]);
}

describe('profile detail routes', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders autoscaling, partial telemetry, and complete slot diagnostics', async () => {
    const router = renderProfile(fleetResponse());

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Profile default overview' }),
    ).toBeInTheDocument();
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(
      await screen.findByTestId('profile-overview-maximum-default', {}, { timeout: 5_000 }),
    ).toHaveTextContent('30');
    expect(screen.getByTestId('profile-overview-target-default')).toHaveTextContent('3');
    expect(screen.getByTestId('profile-overview-local-default')).toHaveTextContent('1');
    expect(screen.getByTestId('profile-overview-eligible-default')).toHaveTextContent('1');
    expect(screen.getByTestId('profile-overview-resources-default')).toHaveTextContent('partial');
    expect(screen.getByTestId('profile-overview-operations-default')).toHaveTextContent(
      'unavailable',
    );
    expect(screen.getByRole('link', { name: 'View diagnostics' })).toHaveAttribute(
      'href',
      `${profilePath}/diagnostics`,
    );
    expect(screen.getByRole('link', { name: 'View workers' })).toHaveAttribute(
      'href',
      `${profilePath}/workers`,
    );
    expect(screen.getByRole('link', { name: 'View recovery' })).toHaveAttribute(
      'href',
      `${profilePath}/recovery`,
    );
    expect(screen.queryByTestId('profile-overview-history')).not.toBeInTheDocument();
    const navigation = screen.getByRole('navigation', {
      name: 'default profile navigation',
    });
    expect(within(navigation).getByRole('link', { name: 'Overview' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(within(navigation).getByRole('link', { name: 'Capacity' })).toHaveAttribute(
      'href',
      `${profilePath}/capacity`,
    );
    expect(document.querySelector('details')).toBeNull();

    await act(async () => {
      await router.navigate(profileRoute('capacity'));
    });
    expect(
      await screen.findByTestId('profile-capacity-target-default', {}, { timeout: 5_000 }),
    ).toHaveTextContent('3');
    expect(screen.getByTestId('profile-autoscaling-error-default')).toHaveTextContent(
      'GitHub queue observation failed.',
    );
    expect(screen.getByTestId('profile-capacity-eligible-default')).toHaveTextContent('1');

    await act(async () => {
      await router.navigate(profileRoute('diagnostics'));
    });
    expect(
      await screen.findByTestId('profile-resource-telemetry-default', {}, { timeout: 5_000 }),
    ).toHaveTextContent('partial');
    expect(screen.getByTestId('profile-resource-telemetry-default')).toHaveTextContent('partial');
    expect(screen.getByTestId('profile-resource-host-default')).toHaveTextContent('Unavailable');
    expect(screen.getByTestId('profile-resource-manager-default')).toHaveTextContent(
      '0.5 cores · 128 MiB · 8 PIDs',
    );
    expect(screen.getByTestId('profile-resource-workers-default')).toHaveTextContent(
      '1.25 cores · 256 MiB · 12 PIDs',
    );

    await act(async () => {
      await router.navigate(profileRoute('workers'));
    });
    const table = await screen.findByRole('table', { name: 'Slots for profile default' });
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
    const router = renderProfile(
      fleetResponse([nodeResponse({ profiles: [profile] })]),
      'viewer',
      profileRoute('capacity'),
    );

    expect(await screen.findByText('Fixed capacity')).toBeInTheDocument();
    expect(screen.getByTestId('profile-capacity-configured-default')).toHaveTextContent('2');
    expect(screen.queryByLabelText('Absolute maximum')).not.toBeInTheDocument();

    await act(async () => {
      await router.navigate(profileRoute('diagnostics'));
    });
    await screen.findByTestId('profile-resource-telemetry-default');
    expect(screen.getByTestId('profile-resource-host-default')).toHaveTextContent('Unavailable');
    expect(screen.getByTestId('profile-resource-manager-default')).toHaveTextContent('Unavailable');

    await act(async () => {
      await router.navigate(profileRoute('workers'));
    });
    expect(
      await screen.findByText('The manager has not reported any slots for this profile.'),
    ).toBeInTheDocument();
  });

  it('treats registration eligibility from older manager contracts as unknown', async () => {
    const legacySlot = slotResponse({ registrationStatus: undefined });
    const profile = profileResponse({
      managerContractVersion: 9,
      eligibleSlots: undefined,
      slots: [legacySlot],
    });
    const router = renderProfile(
      fleetResponse([nodeResponse({ profiles: [profile] })]),
      'owner',
      profileRoute('capacity'),
    );

    expect(await screen.findByTestId('profile-capacity-eligible-default')).toHaveTextContent(
      'Unknown',
    );
    await act(async () => {
      await router.navigate(profileRoute('workers'));
    });
    expect(screen.getByTestId('slot-registration-repo-default-000001')).toHaveTextContent(
      'unknown',
    );
  });

  it.each([
    ['offline', { isOnline: false }, 'offline'],
    ['revoked', { isRevoked: true }, 'revoked'],
  ])('disables capacity changes for %s nodes', async (_name, nodeOverrides, status) => {
    renderProfile(fleetResponse([nodeResponse(nodeOverrides)]), 'owner', profileRoute('capacity'));

    const input = await screen.findByLabelText('Absolute maximum');
    expect(input).toBeDisabled();
    expect(screen.getByTestId('profile-node-unavailable')).toHaveTextContent(status);
  });

  it.each(['stale', 'stopped'] as const)('calls out a %s manager', async (managerStatus) => {
    renderProfile(
      fleetResponse([
        nodeResponse({ profiles: [profileResponse({ managerStatus, autoscaling: null })] }),
      ]),
      'owner',
      profileRoute('capacity'),
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
        'owner',
        profileRoute('capacity'),
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
    renderProfile(fleetResponse(), 'owner', profileRoute('capacity'), fetchMock);
    const user = userEvent.setup();

    const input = await screen.findByLabelText('Absolute maximum');
    await user.clear(input);
    await user.type(input, '40');
    await user.click(screen.getByRole('button', { name: 'Queue change' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('Set default capacity maximum to 40?')).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
    await user.click(within(dialog).getByRole('button', { name: 'Confirm capacity change' }));

    expect(
      await screen.findByText('Queuing capacity change…', { selector: '[role="status"]' }),
    ).toBeInTheDocument();
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
    await waitFor(() =>
      expect(screen.queryByText('Queuing capacity change…')).not.toBeInTheDocument(),
    );
  });

  it('renders contract 11 policy, admission ceiling, image identity, I/O, and exit evidence', async () => {
    const router = renderProfile(contractElevenFleet(), 'owner', profileRoute('workers'));

    expect(await screen.findByTestId('profile-resource-policy-default')).toBeInTheDocument();
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

    await act(async () => {
      await router.navigate(profileRoute('capacity'));
    });
    expect(await screen.findByTestId('profile-targets-default')).toBeInTheDocument();

    await act(async () => {
      await router.navigate(profileRoute('diagnostics'));
    });
    expect(await screen.findByTestId('profile-resource-telemetry-default')).toBeInTheDocument();
    expect(screen.queryByText('Last error: None')).not.toBeInTheDocument();
  });

  it('describes absent exit evidence without calling it clean', async () => {
    renderProfile(
      contractElevenFleet([target()], { lastExit: null }),
      'owner',
      profileRoute('workers'),
    );

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
    renderProfile(contractElevenFleet(), 'owner', profileRoute('capacity'));

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
      'owner',
      profileRoute('capacity'),
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
      'owner',
      profileRoute('capacity'),
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
    renderProfile(
      contractElevenFleet([target({ statistics: null })]),
      'owner',
      profileRoute('capacity'),
    );

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
    const router = renderProfile(fleetResponse(), 'owner', profileRoute('workers'));

    expect(await screen.findByTestId('profile-policy-memory-default')).toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId('profile-admission-ceiling-default')).toHaveTextContent(
      'Unavailable',
    );
    await act(async () => {
      await router.navigate(profileRoute('capacity'));
    });
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
    renderProfile(fleet, role as 'viewer' | 'owner', profileRoute('recovery'));

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
    renderProfile(recoveryFleet(), 'owner', profileRoute('recovery'), fetchMock);
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
      'owner',
      profileRoute('recovery'),
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
      'owner',
      profileRoute('recovery'),
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
    const router = renderProfile(
      recoveryFleet({ latestCommand: recoveryCommand('started') }),
      'owner',
      profileRoute('capacity'),
    );

    expect(await screen.findByLabelText('Absolute maximum')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Queue change' })).toBeDisabled();
    await act(async () => {
      await router.navigate(profileRoute('recovery'));
    });
    expect(screen.getByTestId('profile-recovery-unavailable-default')).toHaveAttribute(
      'data-reason',
      'recovery-active',
    );
  });

  it('tells viewers that recovery has never run without offering the action', async () => {
    renderProfile(recoveryFleet(), 'viewer', profileRoute('recovery'));

    expect(await screen.findByTestId('profile-recovery-empty-default')).toHaveTextContent(
      'No manager recovery has been requested',
    );
    expect(screen.getByRole('heading', { name: 'Manager recovery' })).toBeInTheDocument();
  });

  it('shows contract-12 subsystem health, capacity deficits, and manager chronology', async () => {
    const router = renderProfile(contractTwelveFleet(), 'owner', profileRoute('diagnostics'));

    expect(
      await screen.findByTestId('profile-subsystem-docker-default', {}, { timeout: 5_000 }),
    ).toHaveTextContent('degraded');
    expect(screen.getByTestId('profile-subsystem-docker-default-state')).toHaveTextContent(
      'degraded',
    );
    expect(screen.getByTestId('profile-subsystem-docker-default')).toHaveTextContent(
      'not the health of Docker itself',
    );
    expect(screen.getByTestId('profile-subsystem-docker-default-last-success')).toHaveTextContent(
      'docker-ping',
    );
    expect(screen.getByTestId('profile-subsystem-docker-default-last-failure')).toHaveTextContent(
      'Manager evidence: Docker refused to start the worker container.',
    );
    expect(screen.getByTestId('profile-subsystem-docker-default-backoff')).toHaveTextContent(
      'Retry scheduled for',
    );
    expect(screen.getByTestId('profile-subsystem-github-default-state')).toHaveTextContent(
      'unknown',
    );

    const chronology = screen.getByRole('list', {
      name: 'Manager operations for profile default, newest first',
    });
    const events = within(chronology).getAllByRole('listitem');
    expect(events).toHaveLength(2);
    expect(events[0]).toHaveTextContent('docker-run');
    expect(events[0]).toHaveTextContent('Retry scheduled for');
    expect(events[1]).toHaveTextContent('manager-start');
    expect(screen.getByTestId('profile-operations-availability-default')).toHaveTextContent(
      'intact window',
    );

    await act(async () => {
      await router.navigate(profileRoute('capacity'));
    });
    expect(
      await screen.findByTestId('profile-capacity-deficit-target-default-scale-set-linux'),
    ).toHaveTextContent('3');
    expect(
      screen.getByTestId('profile-capacity-deficit-label-default-scale-set-linux'),
    ).toHaveTextContent('1 short of target');
    expect(
      screen.getByTestId('profile-capacity-deficit-reason-default-scale-set-linux'),
    ).toHaveTextContent('docker-failed');
    expect(
      screen.getByTestId('profile-capacity-deficit-description-default-scale-set-linux'),
    ).toHaveTextContent('The manager-supplied blocking reason is docker-failed.');
    expect(
      screen.getByTestId('profile-capacity-deficit-description-default-scale-set-linux'),
    ).not.toHaveTextContent('30');
  });

  it('reports contract-11 observations as unavailable rather than healthy or empty', async () => {
    const router = renderProfile(contractElevenFleet(), 'owner', profileRoute('capacity'));

    expect(
      await screen.findByTestId('profile-capacity-evidence-default', {}, { timeout: 5_000 }),
    ).toHaveTextContent('unavailable rather than zero');

    await act(async () => {
      await router.navigate(profileRoute('diagnostics'));
    });
    expect(screen.getByTestId('profile-subsystem-docker-default-state')).toHaveTextContent(
      'unavailable',
    );
    expect(screen.getByTestId('profile-subsystem-github-default')).toHaveTextContent(
      'unavailable rather than healthy',
    );
    expect(screen.getByTestId('profile-operations-availability-default')).toHaveTextContent(
      'unavailable rather than absent',
    );
    expect(
      screen.queryByRole('list', {
        name: 'Manager operations for profile default, newest first',
      }),
    ).not.toBeInTheDocument();
  });

  it.each([
    [
      'truncated',
      { status: 'truncated', droppedEvents: 4 },
      'discarded 4 older or rejected entries',
    ],
    [
      'unavailable',
      { status: 'unavailable', highestSequence: null, droppedEvents: 0, events: [] },
      'could not read or restore its durable journal',
    ],
  ])('explains a %s manager journal', async (_name, overrides, message) => {
    renderProfile(
      contractTwelveFleet({ operationJournal: operationJournal(overrides) }),
      'owner',
      profileRoute('diagnostics'),
    );

    expect(
      await screen.findByTestId('profile-operations-availability-default', {}, { timeout: 5_000 }),
    ).toHaveTextContent(message);
  });

  it('keeps unavailable and measured-zero capacity evidence distinct', async () => {
    renderProfile(
      contractTwelveFleet({
        capacityEvidence: {
          fixed: null,
          targets: [
            targetDeficit({
              freshness: 'unavailable',
              eligibleWorkers: null,
              eligibilityDeficit: null,
              localDeficit: 0,
              reason: 'unknown',
            }),
            targetDeficit({
              key: 'scale-set-windows',
              eligibleWorkers: 0,
              eligibilityDeficit: 3,
              localDeficit: 0,
              reason: 'none',
            }),
          ],
        },
      }),
      'owner',
      profileRoute('capacity'),
    );

    expect(
      await screen.findByTestId(
        'profile-capacity-deficit-eligible-default-scale-set-linux',
        {},
        { timeout: 5_000 },
      ),
    ).toHaveTextContent('Unavailable');
    expect(
      screen.getByTestId('profile-capacity-deficit-description-default-scale-set-linux'),
    ).toHaveTextContent('unavailable rather than zero');
    expect(
      screen.getByTestId('profile-capacity-deficit-eligible-default-scale-set-windows'),
    ).toHaveTextContent('0');
    expect(
      screen.getByTestId('profile-capacity-deficit-label-default-scale-set-windows'),
    ).toHaveTextContent('3 short of eligibility');
    expect(
      screen.getByTestId('profile-capacity-deficit-description-default-scale-set-windows'),
    ).toHaveTextContent('local capacity meets the target');
  });

  it('reports an eligibility-only shortfall and keeps a met target current', async () => {
    renderProfile(
      contractTwelveFleet({
        capacityEvidence: {
          fixed: null,
          targets: [
            targetDeficit({
              activeWorkers: 3,
              eligibleWorkers: 1,
              eligibilityDeficit: 2,
              localDeficit: 0,
              reason: 'none',
              evidence: null,
            }),
            targetDeficit({
              key: 'scale-set-windows',
              activeWorkers: 3,
              eligibleWorkers: null,
              eligibilityDeficit: null,
              localDeficit: 0,
              reason: 'none',
              evidence: null,
            }),
          ],
        },
      }),
      'owner',
      profileRoute('capacity'),
    );

    expect(
      await screen.findByTestId(
        'profile-capacity-deficit-label-default-scale-set-linux',
        {},
        { timeout: 5_000 },
      ),
    ).toHaveTextContent('2 short of eligibility');
    expect(
      screen.getByTestId('profile-capacity-deficit-label-default-scale-set-windows'),
    ).toHaveTextContent('No reported shortfall');
    expect(
      screen.getByTestId('profile-capacity-deficit-description-default-scale-set-windows'),
    ).toHaveTextContent('unavailable rather than zero');
  });

  it('surfaces adverse manager outcomes and degraded subsystems in the collapsed summaries', async () => {
    renderProfile(
      contractTwelveFleet({
        operationJournal: operationJournal({
          events: [
            managerEvent({ sequence: 41, outcome: 'timed-out', reason: 'timeout' }),
            managerEvent({
              sequence: 40,
              outcome: 'blocked',
              reason: 'capacity-ceiling',
              retryAt: null,
            }),
            managerEvent({
              sequence: 39,
              outcome: 'recovered',
              reason: 'recovered',
              retryAt: null,
            }),
          ],
        }),
      }),
      'owner',
      profileRoute('diagnostics'),
    );

    expect(
      await screen.findByTestId('profile-operations-adverse-default', {}, { timeout: 5_000 }),
    ).toHaveTextContent('2 adverse events');
    expect(screen.getByTestId('profile-operations-availability-default')).toHaveTextContent(
      '2 adverse events it did not complete',
    );
    expect(
      screen.getByTestId('profile-operation-outcome-default-41').firstElementChild,
    ).toHaveClass('bg-red-100');
    expect(
      screen.getByTestId('profile-operation-outcome-default-40').firstElementChild,
    ).toHaveClass('bg-red-100');
    expect(
      screen.getByTestId('profile-operation-outcome-default-39').firstElementChild,
    ).toHaveClass('bg-emerald-100');
    expect(
      screen.getByTestId('profile-subsystem-summary-docker-default').lastElementChild,
    ).toHaveClass('bg-red-100');
    expect(screen.getByTestId('profile-subsystem-summary-docker-default')).toHaveTextContent(
      'degraded',
    );
    expect(screen.getByTestId('profile-subsystem-summary-github-default')).toHaveTextContent(
      'unknown',
    );
  });

  it('renders fixed capacity evidence against desired slots', async () => {
    const router = renderProfile(
      fleetResponse([
        nodeResponse({
          profiles: [
            profileResponse({
              managerContractVersion: 12,
              autoscaling: null,
              resourceTelemetry: null,
              slots: [],
              desiredSlots: 2,
              activeSlots: 0,
              eligibleSlots: 0,
              configuredSlots: 2,
              operationJournal: operationJournal({ events: [], highestSequence: null }),
              subsystemHealth: subsystemHealth(),
              capacityEvidence: {
                fixed: {
                  observedAt: '2026-07-19T18:30:00+00:00',
                  freshness: 'current',
                  targetSlots: 2,
                  activeWorkers: 0,
                  startingWorkers: 1,
                  drainingWorkers: 0,
                  cleanupPendingWorkers: 0,
                  eligibleWorkers: null,
                  localDeficit: 2,
                  eligibilityDeficit: null,
                  reason: 'launch-pending',
                  evidence: null,
                },
                targets: [],
              },
            }),
          ],
        }),
      ]),
      'owner',
      profileRoute('capacity'),
    );

    expect(
      await screen.findByTestId(
        'profile-capacity-deficit-label-default-default',
        {},
        {
          timeout: 5_000,
        },
      ),
    ).toHaveTextContent('2 short of target');
    expect(screen.getByTestId('profile-capacity-deficit-target-default-default')).toHaveTextContent(
      '2',
    );
    expect(
      screen.getByTestId('profile-capacity-deficit-description-default-default'),
    ).toHaveTextContent('The manager-supplied blocking reason is launch-pending.');

    await act(async () => {
      await router.navigate(profileRoute('diagnostics'));
    });
    expect(screen.getByTestId('profile-operations-availability-default')).toHaveTextContent(
      'no notable operation',
    );
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
