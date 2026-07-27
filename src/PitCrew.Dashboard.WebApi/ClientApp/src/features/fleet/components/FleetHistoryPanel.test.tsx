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
    pointsTruncated: false,
    eventsTruncated: false,
    subsystemHealthTruncated: false,
    capacityDeficitsTruncated: false,
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
    },
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
    profileDiagnosticLimit: 200,
    nodePointLimit: 5000,
    nodeEventLimit: 1000,
    nodeDiagnosticLimit: 1000,
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

function requestedUrl(mock: ReturnType<typeof vi.spyOn>, call: number): string {
  return String(mock.mock.calls[call]?.[0]);
}

describe('FleetHistoryPanel', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('requests a bounded range with explicit caps only after the panel is opened', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(jsonResponse(historyResponse({})));

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    expect(fetchMock).not.toHaveBeenCalled();

    await openPanel('history');
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    const url = requestedUrl(fetchMock, 0);
    expect(url).toContain('resolution=raw');
    expect(url).toContain('points=1000');
    expect(url).toContain('events=200');
    expect(url).toContain('diagnostics=200');
    expect(await screen.findByTestId('history-disclosure-default')).toBeInTheDocument();
  });

  it('switches to hour-aligned hourly requests and keeps the previous range visible while loading', async () => {
    let releaseSecond: (value: Response) => void = () => undefined;
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(jsonResponse(historyResponse({})))
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            releaseSecond = resolve;
          }),
      );

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');
    await screen.findByTestId('history-disclosure-default');

    await userEvent.selectOptions(screen.getByLabelText('Time range'), '168');
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));

    const url = new URL(requestedUrl(fetchMock, 1), 'https://dashboard.invalid');
    expect(url.searchParams.get('resolution')).toBe('hourly');
    expect(url.searchParams.get('points')).toBe('200');
    expect(url.searchParams.get('from')?.endsWith(':00:00.000Z')).toBe(true);
    expect(url.searchParams.get('to')?.endsWith(':00:00.000Z')).toBe(true);
    expect(screen.getByTestId('history-disclosure-default')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('showing the previous range');

    await act(async () => {
      releaseSecond(jsonResponse(historyResponse({ resolution: 'hourly' })));
    });
  });

  it('states the per-profile and node-wide limits it reached rather than implying completeness', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(historyResponse({ pointsTruncated: true, diagnosticsTruncated: true })),
    );

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');

    const banner = await screen.findByText(/reached its limits/);
    expect(banner).toHaveTextContent('points (1000 per profile, 5000 across all profiles)');
    expect(banner).toHaveTextContent('diagnostics (200 per profile, 1000 across all profiles)');
    expect(banner).toHaveTextContent('older retained data inside the same range is hidden');
  });

  it('announces loading and truncation through a single live region', async () => {
    let releaseSecond: (value: Response) => void = () => undefined;
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(jsonResponse(historyResponse({ pointsTruncated: true })))
      .mockImplementationOnce(
        () =>
          new Promise<Response>((resolve) => {
            releaseSecond = resolve;
          }),
      );

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');
    await screen.findByTestId('history-disclosure-default');

    await userEvent.selectOptions(screen.getByLabelText('Time range'), '168');
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));

    const regions = screen.getAllByRole('status');
    expect(regions).toHaveLength(1);
    expect(regions[0]).toHaveTextContent('showing the previous range');
    expect(regions[0]).not.toHaveTextContent('reached its limits');

    await act(async () => {
      releaseSecond(jsonResponse(historyResponse({ resolution: 'hourly' })));
    });
  });

  it('accepts a target repository at the full contract length', async () => {
    const repository = `contoso/${'a'.repeat(2000)}`;
    const retained = profile('default', '2026-07-26T11:59:45+00:00');
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
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
    );

    render(
      <FleetHistoryPanel nodeId={nodeId} profileId={null} tenantId="local" testId="history" />,
    );
    await openPanel('history');

    const deficits = await screen.findByTestId('history-deficits-default');
    expect(within(deficits).getByText(repository)).toBeInTheDocument();
  });

  it('reports a failed history load as an error instead of an empty range', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response('{"title":"History is unavailable."}', {
        status: 503,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    );

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
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        historyResponse({
          profiles: [
            profile('default', '2026-07-26T11:59:45+00:00'),
            profile('builds', '2026-07-26T11:59:30+00:00'),
          ],
        }),
      ),
    );

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
  });
});
