import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { FleetHistoryPanel } from './FleetHistoryPanel';

const nodeId = 'a6235ec4-2a15-4f91-a9e0-811152869a51';

function profile(profileId: string, observedAt: string) {
  return {
    profileId,
    samples: [
      {
        observedAt,
        sampledAt: observedAt,
        telemetryStatus: 'available',
        managerInstanceId: `manager-${profileId}`,
        managerStatus: 'running',
        generation: 4,
        desiredSlots: 3,
        activeSlots: 2,
        drainingSlots: 0,
        configuredSlots: 6,
        eligibleSlots: 2,
        targetSlots: 3,
        maximumSlots: 6,
        assignedJobs: 1,
        runningJobs: 1,
        availableJobs: 0,
        idleRunners: 1,
        busyRunners: 1,
        localRunningWorkers: 2,
        managerCpuCores: 0.25,
        managerMemoryBytes: 104857600,
        managerPids: 12,
        hostLogicalProcessorCount: 8,
        hostMemoryBytes: 17179869184,
        workerCpuCores: 1.5,
        workerMemoryBytes: 2147483648,
        workerPids: 64,
        networkRxBytes: 1024,
        networkTxBytes: 2048,
        blockReadBytes: 4096,
        blockWriteBytes: 8192,
        exitReports: 1,
        adverseExitReports: 0,
        localCapacityDeficit: 1,
        eligibilityCapacityDeficit: 0,
        capacityDeficitReason: 'docker-failed',
        capacityDeficitFreshness: 'current',
        workerUpdateStatus: 'rolling',
        workerTargetImage: 'ghcr.io/example/runner:2.0',
        workerTargetImageId: `sha256:${'2'.repeat(64)}`,
        workerTargetRevision: 'b'.repeat(64),
        workerCurrentWorkers: 1,
        workerStaleWorkers: 1,
        workerUpdateError: null,
      },
    ],
    rollups: [],
    events: [],
    subsystemHealthChanges: [],
    capacityDeficits: [
      {
        targetKey: `repo:contoso/${profileId}`,
        observedAt,
        repository: `contoso/${profileId}`,
        freshness: 'current',
        targetSlots: 3,
        activeWorkers: 2,
        startingWorkers: 0,
        drainingWorkers: 0,
        cleanupPendingWorkers: 0,
        eligibleWorkers: 2,
        localDeficit: 1,
        eligibilityDeficit: 0,
        reason: 'docker-failed',
        evidence: null,
      },
    ],
    workerUpdateChanges: [
      {
        kind: 'rollout-started',
        observedAt,
        status: 'rolling',
        targetImage: 'ghcr.io/example/runner:2.0',
        targetImageId: `sha256:${'2'.repeat(64)}`,
        targetRevision: 'b'.repeat(64),
        currentWorkers: 1,
        staleWorkers: 1,
        lastError: null,
      },
    ],
    pointsTruncated: false,
    eventsTruncated: false,
    subsystemHealthTruncated: false,
    capacityDeficitsTruncated: false,
    workerUpdatesTruncated: false,
    journal: {
      status: 'current',
      capacity: 32,
      managerHighestSequence: 41,
      storedLowestSequence: 1,
      storedHighestSequence: 41,
      managerDroppedEvents: 0,
      missedEvents: 0,
      undeliveredEvents: 0,
      epoch: 0,
      epochResets: 0,
      rejectedFutureEvents: 0,
      updatedAt: observedAt,
    },
    retention: {
      earliestRetainedSample: observedAt,
      droppedSamples: 0,
      earliestRetainedRollup: observedAt,
      droppedRollups: 0,
      earliestRetainedEvent: observedAt,
      droppedEvents: 0,
      earliestRetainedSubsystemHealthChange: observedAt,
      droppedSubsystemHealthChanges: 0,
      earliestRetainedCapacityDeficit: observedAt,
      droppedCapacityDeficits: 0,
      rejectedFutureSamples: 0,
      historyExpiredAt: null,
    },
  };
}

function capabilities(overrides: Record<string, unknown> = {}) {
  return {
    defaultRangeHours: 4,
    maximumRangeHours: 720,
    resolutions: ['raw', 'hourly'],
    maximumPoints: 1000,
    maximumEvents: 200,
    maximumDiagnostics: 200,
    nodePointLimit: 5000,
    nodeEventLimit: 1000,
    nodeDiagnosticLimit: 1000,
    expectedRawCadenceSeconds: 15,
    sampleRetentionHours: 336,
    rollupRetentionHours: 2160,
    ...overrides,
  };
}

function historyResponse(overrides: Record<string, unknown>) {
  return {
    nodeId,
    generatedAt: '2026-07-26T12:00:00+00:00',
    from: '2026-07-26T08:00:00+00:00',
    to: '2026-07-26T12:00:00+00:00',
    resolution: 'raw',
    profiles: [profile('default', '2026-07-26T11:59:45+00:00')],
    pointsTruncated: false,
    eventsTruncated: false,
    diagnosticsTruncated: false,
    profilePointLimit: 1000,
    profileEventLimit: 200,
    profileSubsystemHealthLimit: 200,
    profileCapacityDeficitLimit: 200,
    profileWorkerUpdateLimit: 200,
    nodePointLimit: 5000,
    nodeEventLimit: 1000,
    nodeDiagnosticLimit: 1000,
    incompletenessFloors: [],
    ...overrides,
  };
}

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    headers: { 'Content-Type': 'application/json' },
  });
}

async function openPanel(testId: string) {
  const panel = screen.getByTestId(testId);
  await act(async () => {
    panel.setAttribute('open', '');
    panel.dispatchEvent(new Event('toggle'));
  });
  return panel;
}

function isCapabilitiesRequest(url: string): boolean {
  return new URL(url, 'https://dashboard.invalid').pathname.endsWith('/history/capabilities');
}

function historyUrls(mock: ReturnType<typeof vi.spyOn>): readonly string[] {
  return (mock.mock.calls as readonly unknown[][])
    .map((call) => String(call[0]))
    .filter((url: string) => !isCapabilitiesRequest(url));
}

function mockFetch(
  handlers: readonly (() => Response | Promise<Response>)[],
  capabilityOverrides: Record<string, unknown> = {},
) {
  let index = 0;
  return vi.spyOn(globalThis, 'fetch').mockImplementation((input) => {
    if (isCapabilitiesRequest(String(input))) {
      return Promise.resolve(jsonResponse(capabilities(capabilityOverrides)));
    }
    const handler = handlers[Math.min(index, handlers.length - 1)];
    index += 1;
    return Promise.resolve(handler());
  });
}

describe('FleetHistoryPanel', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('requests a server-advertised bounded range only after the panel is opened', async () => {
    const fetchMock = mockFetch([() => jsonResponse(historyResponse({}))]);

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    expect(fetchMock).not.toHaveBeenCalled();

    await openPanel('history');
    await waitFor(() => expect(historyUrls(fetchMock)).toHaveLength(1));

    const url = historyUrls(fetchMock)[0];
    expect(url).toContain('resolution=raw');
    expect(url).toContain('points=960');
    expect(url).not.toContain('events=');
    expect(url).not.toContain('diagnostics=');
    expect(await screen.findByTestId('history-disclosure-default')).toBeInTheDocument();
  });

  it('loads a profile history route immediately without another disclosure', async () => {
    const fetchMock = mockFetch([() => jsonResponse(historyResponse({}))]);

    render(
      <FleetHistoryPanel
        nodeId={nodeId}
        presentation="page"
        profileId="default"
        tenantId="local"
        testId="history"
      />,
    );

    await waitFor(() => expect(historyUrls(fetchMock)).toHaveLength(1));
    expect(await screen.findByTestId('history-profile-default')).toBeInTheDocument();
    expect(screen.getByTestId('history').tagName).toBe('SECTION');
    expect(screen.queryByTestId('history-disclosure-default')).not.toBeInTheDocument();
  });

  it('offers a valid preset when the server advertises a maximum range under four hours', async () => {
    const fetchMock = mockFetch([() => jsonResponse(historyResponse({}))], {
      maximumRangeHours: 2,
      maximumPoints: 120,
      maximumEvents: 20,
      maximumDiagnostics: 20,
    });

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');
    await waitFor(() => expect(historyUrls(fetchMock)).toHaveLength(1));

    const url = new URL(historyUrls(fetchMock)[0], 'https://dashboard.invalid');
    const from = Date.parse(url.searchParams.get('from') ?? '');
    const to = Date.parse(url.searchParams.get('to') ?? '');
    expect(url.searchParams.get('resolution')).toBe('raw');
    expect(to - from).toBeLessThanOrEqual(2 * 60 * 60 * 1000);
    expect(Number(url.searchParams.get('points'))).toBeLessThanOrEqual(120);
    expect(url.searchParams.get('events')).toBeNull();
    expect(url.searchParams.get('diagnostics')).toBeNull();
  });

  it('switches to hour-aligned hourly requests and keeps the previous range visible while loading', async () => {
    let releaseSecond: (value: Response) => void = () => undefined;
    const fetchMock = mockFetch([
      () => jsonResponse(historyResponse({})),
      () =>
        new Promise<Response>((resolve) => {
          releaseSecond = resolve;
        }),
    ]);

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');
    await screen.findByTestId('history-disclosure-default');

    await userEvent.selectOptions(screen.getByLabelText('Time range'), 'hourly-168');
    await waitFor(() => expect(historyUrls(fetchMock)).toHaveLength(2));

    const url = new URL(historyUrls(fetchMock)[1], 'https://dashboard.invalid');
    expect(url.searchParams.get('resolution')).toBe('hourly');
    expect(url.searchParams.get('points')).toBe('168');
    expect(url.searchParams.get('from')?.endsWith(':00:00.000Z')).toBe(true);
    expect(url.searchParams.get('to')?.endsWith(':00:00.000Z')).toBe(true);
    expect(screen.getByTestId('history-disclosure-default')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('showing the previous range');

    await act(async () => {
      releaseSecond(jsonResponse(historyResponse({ resolution: 'hourly' })));
    });
  });

  it('states the per-profile and node-wide limits it reached rather than implying completeness', async () => {
    mockFetch([
      () => jsonResponse(historyResponse({ pointsTruncated: true, diagnosticsTruncated: true })),
    ]);

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');

    const banner = await screen.findByText(/reached its limits/);
    expect(banner).toHaveTextContent('points (1000 per profile, 5000 across all profiles)');
    expect(banner).toHaveTextContent(
      'diagnostics (200 subsystem-health, 200 capacity-deficit, and 200 worker-rollout rows per profile, 1000 combined across all profiles)',
    );
    expect(banner).toHaveTextContent('older retained data inside the same range is hidden');
  });

  it('announces loading and truncation through a single live region', async () => {
    let releaseSecond: (value: Response) => void = () => undefined;
    const fetchMock = mockFetch([
      () => jsonResponse(historyResponse({ pointsTruncated: true })),
      () =>
        new Promise<Response>((resolve) => {
          releaseSecond = resolve;
        }),
    ]);

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');
    await screen.findByTestId('history-disclosure-default');
    expect(screen.getAllByRole('status')).toHaveLength(1);

    await userEvent.selectOptions(screen.getByLabelText('Time range'), 'hourly-168');
    await waitFor(() => expect(historyUrls(fetchMock)).toHaveLength(2));

    const regions = screen.getAllByRole('status');
    expect(regions).toHaveLength(1);
    expect(regions[0]).toHaveTextContent('showing the previous range');

    await act(async () => {
      releaseSecond(jsonResponse(historyResponse({ resolution: 'hourly' })));
    });
  });

  it('keeps the status region mounted before the first response arrives', async () => {
    mockFetch([
      () =>
        new Promise<Response>(() => {
          // Never resolves: the first announcement must not depend on a settled response.
        }),
    ]);

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');

    expect(screen.getAllByRole('status')).toHaveLength(1);
  });

  it('reports compacted incompleteness floors instead of an apparently complete range', async () => {
    mockFetch([
      () =>
        jsonResponse(
          historyResponse({
            incompletenessFloors: [
              {
                scope: 'node',
                earliestExpiredAt: '2026-07-20T00:00:00+00:00',
                latestExpiredAt: '2026-07-25T00:00:00+00:00',
                expiredProfiles: 3,
                droppedSamples: 40,
                droppedRollups: 5,
                droppedEvents: 9,
                droppedSubsystemHealthChanges: 2,
                droppedCapacityDeficits: 1,
              },
            ],
          }),
        ),
    ]);

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');
    await screen.findByTestId('history-disclosure-default');

    expect(screen.getByText(/expired 3 profile histories for this node/i)).toBeInTheDocument();
  });

  it('renders an expired profile history as expired rather than complete', async () => {
    const retained = profile('default', '2026-07-26T11:59:45+00:00');
    mockFetch([
      () =>
        jsonResponse(
          historyResponse({
            profiles: [
              {
                ...retained,
                journal: { ...retained.journal, status: 'expired' },
                retention: {
                  ...retained.retention,
                  historyExpiredAt: '2026-07-26T10:00:00+00:00',
                },
              },
            ],
          }),
        ),
    ]);

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');
    const disclosure = await screen.findByTestId('history-disclosure-default');

    expect(within(disclosure).getAllByText('Expired').length).toBeGreaterThan(0);
    expect(within(disclosure).queryByText('Complete')).not.toBeInTheDocument();
  });

  it('accepts a target repository at the full contract length', async () => {
    const repository = `contoso/${'a'.repeat(2000)}`;
    const retained = profile('default', '2026-07-26T11:59:45+00:00');
    mockFetch([
      () =>
        jsonResponse(
          historyResponse({
            profiles: [
              {
                ...retained,
                capacityDeficits: [{ ...retained.capacityDeficits[0], repository }],
              },
            ],
          }),
        ),
    ]);

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');

    const deficits = await screen.findByTestId('history-deficits-default');
    expect(within(deficits).getByText(repository)).toBeInTheDocument();
  });

  it('reports a failed history load as an error instead of an empty range', async () => {
    mockFetch([
      () =>
        new Response('{"title":"History is unavailable."}', {
          status: 503,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
    ]);

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(
      screen.queryByText('No retained history exists for this range.'),
    ).not.toBeInTheDocument();
  });

  it('groups every profile behind its own disclosure instead of rendering every chart at once', async () => {
    mockFetch([
      () =>
        jsonResponse(
          historyResponse({
            profiles: [
              profile('default', '2026-07-26T11:59:45+00:00'),
              profile('builds', '2026-07-26T11:59:30+00:00'),
            ],
          }),
        ),
    ]);

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');

    const first = await screen.findByTestId('history-disclosure-default');
    const second = screen.getByTestId('history-disclosure-builds');
    expect(first).not.toHaveAttribute('open');
    expect(second).not.toHaveAttribute('open');
    expect(screen.getByRole('heading', { level: 3, name: 'default' })).toBeInTheDocument();
    expect(screen.queryByTestId('history-chart-default-capacity')).not.toBeInTheDocument();

    await act(async () => {
      first.setAttribute('open', '');
      first.dispatchEvent(new Event('toggle'));
    });

    const opened = within(first);
    expect(opened.getByTestId('history-chart-default-capacity')).toBeInTheDocument();
    expect(
      opened.getByRole('region', { name: /Capacity-deficit reason changes for profile default/ }),
    ).toHaveAttribute('tabindex', '0');
    expect(
      opened.getByRole('region', { name: /Worker image rollout changes for profile default/ }),
    ).toHaveAttribute('tabindex', '0');
  });
});
