import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { FleetDashboard } from './FleetDashboard';

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

const omittedProperty = Symbol('omitted-property');

function slotResponse(key: string, resources: unknown | typeof omittedProperty) {
  return {
    key,
    repository: 'https://github.com/example/project',
    desired: true,
    processRunning: true,
    state: 'online',
    failureCount: 0,
    backoffSeconds: 0,
    updatedAt: '2026-07-19T18:30:00+00:00',
    ...(resources === omittedProperty ? {} : { resources }),
  };
}

function profileResponse(
  profileId: string,
  slots: ReadonlyArray<unknown>,
  resourceTelemetry: unknown | typeof omittedProperty,
) {
  return {
    schemaVersion: 1,
    managerContractVersion: 7,
    profileId,
    managerInstanceId: `manager-${profileId}`,
    managerStatus: 'running',
    observedAt: '2026-07-19T18:30:00+00:00',
    scope: 'repo',
    generation: 1,
    desiredStateHash: 'a'.repeat(64),
    desiredStateStatus: 'accepted',
    desiredSlots: slots.length,
    activeSlots: slots.length,
    drainingSlots: 0,
    slots,
    ...(resourceTelemetry === omittedProperty ? {} : { resourceTelemetry }),
  };
}

function fleetResponse(profiles: ReadonlyArray<unknown>) {
  return {
    generatedAt: '2026-07-19T18:30:05+00:00',
    nodes: [
      {
        nodeId: 'a6235ec4-2a15-4f91-a9e0-811152869a51',
        displayName: 'Resource server',
        connectorVersion: '2.0.0',
        enrolledAt: '2026-07-18T15:00:00+00:00',
        lastSeenAt: '2026-07-19T18:30:05+00:00',
        isOnline: true,
        isRevoked: false,
        credentialRotationRequested: false,
        profiles,
      },
    ],
  };
}

function autoscalingResponse(overrides: Readonly<Record<string, unknown>> = {}) {
  return {
    mode: 'scale-set',
    status: 'running',
    minimumIdleSlots: 0,
    maximumSlots: 30,
    targetSlots: 0,
    assignedJobs: 0,
    runningJobs: 0,
    availableJobs: 0,
    idleRunners: 0,
    busyRunners: 0,
    scaleDownDelaySeconds: 300,
    scaleSetCount: 1,
    scaleDownAt: null,
    lastError: null,
    ...overrides,
  };
}

describe('FleetDashboard', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renames a revoked server and updates the fleet card immediately', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (_input, init) => {
      if (init?.method === 'PUT') {
        return new Response(null, { status: 204 });
      }
      return jsonResponse({
        generatedAt: '2026-07-18T16:00:00+00:00',
        nodes: [
          {
            nodeId: 'a6235ec4-2a15-4f91-a9e0-811152869a51',
            displayName: 'Original server',
            connectorVersion: '2.0.0',
            enrolledAt: '2026-07-18T15:00:00+00:00',
            lastSeenAt: '2026-07-18T15:30:00+00:00',
            isOnline: false,
            isRevoked: true,
            credentialRotationRequested: false,
            profiles: [],
          },
        ],
      });
    });
    const user = userEvent.setup();

    render(<FleetDashboard tenantId="local" canAdminister antiforgeryToken="token" />);

    const input = await screen.findByLabelText('Server display name');
    await user.clear(input);
    await user.type(input, '  Renamed server  ');
    await user.click(screen.getByRole('button', { name: 'Rename server' }));

    await waitFor(() => expect(screen.getByText('Renamed server')).toBeInTheDocument());
    const renameCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(renameCall).toBeDefined();
    const [url, init] = renameCall ?? [];
    expect(String(url)).toMatch(
      /\/api\/tenants\/local\/fleet\/v1\/nodes\/a6235ec4-2a15-4f91-a9e0-811152869a51$/,
    );
    expect(new Headers(init?.headers).get('X-PitCrew-Antiforgery')).toBe('token');
    expect(JSON.parse(String(init?.body))).toEqual({
      displayName: 'Renamed server',
    });
    expect(screen.getByRole('status')).toHaveTextContent('Server name updated.');
  });

  it('shows idle-at-zero autoscaling as healthy capacity below the configured maximum', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        fleetResponse([
          {
            ...profileResponse('idle-zero', [], null),
            managerContractVersion: 8,
            configuredSlots: 30,
            desiredSlots: 0,
            activeSlots: 0,
            autoscaling: autoscalingResponse(),
          },
        ]),
      ),
    );

    render(<FleetDashboard tenantId="local" canAdminister={false} antiforgeryToken="" />);

    const capacity = await screen.findByTestId('profile-capacity-autoscaled-idle-zero');
    expect(within(capacity).getByText('Demand-driven autoscaling')).toBeInTheDocument();
    expect(screen.getByTestId('profile-capacity-maximum-idle-zero')).toHaveTextContent('30');
    expect(screen.getByTestId('profile-capacity-target-idle-zero')).toHaveTextContent('0');
    expect(screen.getByTestId('profile-capacity-active-idle-zero')).toHaveTextContent('0');
    expect(screen.getByTestId('profile-capacity-idle-idle-zero')).toHaveTextContent('0');
    expect(screen.getByTestId('profile-autoscaling-status-idle-zero')).toHaveTextContent('running');
    expect(within(capacity).getByText('running')).toHaveClass('text-emerald-800');
    expect(within(capacity).queryByText(/unhealthy|shortfall/i)).not.toBeInTheDocument();
  });

  it('shows active demand metrics plus per-slot activity and targets', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        fleetResponse([
          {
            ...profileResponse(
              'active-demand',
              [
                {
                  ...slotResponse('repo-demand-000001', null),
                  activity: 'busy',
                  target: 'scale-set-linux',
                },
                {
                  ...slotResponse('repo-demand-000002', null),
                  activity: 'idle',
                  target: 'scale-set-windows',
                },
              ],
              null,
            ),
            managerContractVersion: 8,
            configuredSlots: 30,
            desiredSlots: 3,
            autoscaling: autoscalingResponse({
              minimumIdleSlots: 1,
              targetSlots: 3,
              assignedJobs: 5,
              runningJobs: 2,
              availableJobs: 3,
              idleRunners: 1,
              busyRunners: 1,
              scaleSetCount: 2,
            }),
          },
        ]),
      ),
    );

    render(<FleetDashboard tenantId="local" canAdminister={false} antiforgeryToken="" />);

    await screen.findByTestId('profile-capacity-autoscaled-active-demand');
    expect(screen.getByTestId('profile-capacity-target-active-demand')).toHaveTextContent('3');
    expect(screen.getByTestId('profile-capacity-active-active-demand')).toHaveTextContent('2');
    expect(screen.getByTestId('profile-capacity-assigned-active-demand')).toHaveTextContent('5');
    expect(screen.getByTestId('profile-capacity-running-active-demand')).toHaveTextContent('2');
    expect(screen.getByTestId('profile-capacity-available-active-demand')).toHaveTextContent('3');
    expect(screen.getByTestId('profile-capacity-idle-active-demand')).toHaveTextContent('1');
    expect(screen.getByTestId('profile-capacity-busy-active-demand')).toHaveTextContent('1');
    expect(screen.getByTestId('profile-capacity-minimum-idle-active-demand')).toHaveTextContent(
      '1',
    );
    expect(screen.getByTestId('profile-capacity-scale-set-count-active-demand')).toHaveTextContent(
      '2',
    );
    expect(screen.getByTestId('slot-target-repo-demand-000001')).toHaveTextContent(
      'scale-set-linux',
    );
    expect(screen.getByTestId('slot-activity-repo-demand-000001')).toHaveTextContent('busy');
    expect(screen.getByTestId('slot-target-repo-demand-000002')).toHaveTextContent(
      'scale-set-windows',
    );
    expect(screen.getByTestId('slot-activity-repo-demand-000002')).toHaveTextContent('idle');
  });

  it('shows a pending scale-down delay and countdown', async () => {
    vi.spyOn(Date, 'now').mockReturnValue(new Date('2026-07-20T12:00:00+00:00').getTime());
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        fleetResponse([
          {
            ...profileResponse(
              'pending-scale-down',
              [
                {
                  ...slotResponse('repo-scale-down-000001', null),
                  activity: 'idle',
                  target: 'scale-set-linux',
                },
              ],
              null,
            ),
            managerContractVersion: 8,
            configuredSlots: 30,
            autoscaling: autoscalingResponse({
              targetSlots: 1,
              idleRunners: 1,
              scaleDownAt: '2026-07-20T12:01:30+00:00',
            }),
          },
        ]),
      ),
    );

    render(<FleetDashboard tenantId="local" canAdminister={false} antiforgeryToken="" />);

    await screen.findByTestId('profile-capacity-autoscaled-pending-scale-down');
    expect(
      screen.getByTestId('profile-capacity-scale-down-delay-pending-scale-down'),
    ).toHaveTextContent('300 seconds');
    expect(
      screen.getByTestId('profile-capacity-scale-down-countdown-pending-scale-down'),
    ).toHaveTextContent('90 seconds remaining');
  });

  it('shows degraded autoscaling status and the latest error', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        fleetResponse([
          {
            ...profileResponse('degraded-profile', [], null),
            managerContractVersion: 8,
            configuredSlots: 30,
            desiredSlots: 0,
            activeSlots: 0,
            autoscaling: autoscalingResponse({
              status: 'degraded',
              lastError: 'GitHub queue observation failed.',
            }),
          },
        ]),
      ),
    );

    render(<FleetDashboard tenantId="local" canAdminister={false} antiforgeryToken="" />);

    await screen.findByTestId('profile-capacity-autoscaled-degraded-profile');
    expect(screen.getByTestId('profile-autoscaling-status-degraded-profile')).toHaveTextContent(
      'degraded',
    );
    expect(screen.getByTestId('profile-autoscaling-error-degraded-profile')).toHaveTextContent(
      'Last error: GitHub queue observation failed.',
    );
  });

  it('accepts omitted and null autoscaling fields as fixed-capacity profiles', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        fleetResponse([
          profileResponse(
            'legacy-fixed',
            [slotResponse('repo-legacy-fixed-000001', omittedProperty)],
            omittedProperty,
          ),
          {
            ...profileResponse(
              'nullable-fixed',
              [slotResponse('repo-nullable-fixed-000001', null)],
              null,
            ),
            managerContractVersion: 8,
            configuredSlots: null,
            autoscaling: null,
          },
          {
            ...profileResponse(
              'explicit-fixed',
              [
                {
                  ...slotResponse('repo-explicit-fixed-000001', null),
                  activity: null,
                  target: null,
                },
                slotResponse('repo-explicit-fixed-000002', null),
              ],
              null,
            ),
            managerContractVersion: 8,
            configuredSlots: 2,
            autoscaling: null,
          },
        ]),
      ),
    );

    render(<FleetDashboard tenantId="local" canAdminister={false} antiforgeryToken="" />);

    const legacy = await screen.findByTestId('profile-capacity-fixed-legacy-fixed');
    const nullable = screen.getByTestId('profile-capacity-fixed-nullable-fixed');
    const explicit = screen.getByTestId('profile-capacity-fixed-explicit-fixed');
    expect(within(legacy).getByText('Fixed capacity')).toBeInTheDocument();
    expect(within(nullable).getByText('Fixed capacity')).toBeInTheDocument();
    expect(within(explicit).getByText('Fixed capacity')).toBeInTheDocument();
    expect(screen.getByTestId('profile-capacity-configured-legacy-fixed')).toHaveTextContent('1');
    expect(screen.getByTestId('profile-capacity-configured-nullable-fixed')).toHaveTextContent('1');
    expect(screen.getByTestId('profile-capacity-configured-explicit-fixed')).toHaveTextContent('2');
    expect(
      screen.queryByTestId('profile-capacity-autoscaled-legacy-fixed'),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId('slot-target-repo-legacy-fixed-000001')).toHaveTextContent('—');
    expect(screen.getByTestId('slot-activity-repo-explicit-fixed-000001')).toHaveTextContent('—');
  });

  it('renders available point-in-time resources and the current worker aggregate', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        fleetResponse([
          profileResponse(
            'available-profile',
            [
              slotResponse('repo-example-000001', {
                cpuCores: 1.5,
                memoryWorkingSetBytes: 1_073_741_824,
                pids: 48,
              }),
              slotResponse('repo-example-000002', {
                cpuCores: 0.75,
                memoryWorkingSetBytes: 536_870_912,
                pids: 24,
              }),
            ],
            {
              sampledAt: '2026-07-19T18:30:00+00:00',
              status: 'available',
              host: {
                logicalProcessorCount: 8,
                memoryBytes: 34_359_738_368,
              },
              manager: {
                cpuCores: 0.5,
                memoryWorkingSetBytes: 201_326_592,
                pids: 11,
              },
            },
          ),
        ]),
      ),
    );

    render(<FleetDashboard tenantId="local" canAdminister={false} antiforgeryToken="" />);

    const summary = await screen.findByTestId('profile-resource-telemetry-available-profile');
    expect(within(summary).getByText('available')).toBeInTheDocument();
    expect(screen.getByTestId('profile-resource-sampled-available-profile')).not.toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId('profile-resource-host-available-profile')).toHaveTextContent(
      '8 logical processors · 32 GiB',
    );
    expect(screen.getByTestId('profile-resource-manager-available-profile')).toHaveTextContent(
      '0.5 cores · 192 MiB · 11 PIDs',
    );
    expect(screen.getByTestId('profile-resource-workers-available-profile')).toHaveTextContent(
      '2.25 cores · 1.5 GiB · 72 PIDs',
    );
    expect(within(summary).getByText('2 of 2 slots reporting')).toBeInTheDocument();
    expect(screen.getByTestId('slot-cpu-repo-example-000001')).toHaveTextContent('1.5 cores');
    expect(screen.getByTestId('slot-memory-repo-example-000001')).toHaveTextContent('1 GiB');
    expect(screen.getByTestId('slot-pids-repo-example-000001')).toHaveTextContent('48 PIDs');
  });

  it('renders partial and unavailable telemetry without inferring worker activity', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        fleetResponse([
          profileResponse('partial-profile', [slotResponse('repo-partial-000001', null)], {
            sampledAt: '2026-07-19T18:30:00+00:00',
            status: 'partial',
            host: {
              logicalProcessorCount: 4,
              memoryBytes: 8_589_934_592,
            },
            manager: null,
          }),
          profileResponse('unavailable-profile', [slotResponse('repo-unavailable-000001', null)], {
            sampledAt: '2026-07-19T18:30:00+00:00',
            status: 'unavailable',
            host: null,
            manager: null,
          }),
        ]),
      ),
    );

    render(<FleetDashboard tenantId="local" canAdminister={false} antiforgeryToken="" />);

    const partial = await screen.findByTestId('profile-resource-telemetry-partial-profile');
    expect(within(partial).getByText('partial')).toBeInTheDocument();
    expect(screen.getByTestId('profile-resource-host-partial-profile')).toHaveTextContent(
      '4 logical processors · 8 GiB',
    );
    expect(screen.getByTestId('profile-resource-manager-partial-profile')).toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId('profile-resource-workers-partial-profile')).toHaveTextContent(
      'Unavailable',
    );
    expect(within(partial).getByText('0 of 1 slots reporting')).toBeInTheDocument();

    const unavailable = screen.getByTestId('profile-resource-telemetry-unavailable-profile');
    expect(within(unavailable).getByText('unavailable')).toBeInTheDocument();
    expect(screen.getByTestId('profile-resource-host-unavailable-profile')).toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId('profile-resource-manager-unavailable-profile')).toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId('slot-cpu-repo-unavailable-000001')).toHaveTextContent('Unavailable');
    expect(within(unavailable).queryByText(/busy|idle/i)).not.toBeInTheDocument();
  });

  it('accepts omitted and null legacy resource telemetry as unavailable', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        fleetResponse([
          profileResponse(
            'legacy-omitted',
            [slotResponse('repo-legacy-omitted-000001', omittedProperty)],
            omittedProperty,
          ),
          profileResponse('legacy-null', [slotResponse('repo-legacy-null-000001', null)], null),
        ]),
      ),
    );

    render(<FleetDashboard tenantId="local" canAdminister={false} antiforgeryToken="" />);

    await screen.findByTestId('profile-resource-telemetry-legacy-omitted');
    for (const profileId of ['legacy-omitted', 'legacy-null']) {
      expect(screen.getByTestId(`profile-resource-sampled-${profileId}`)).toHaveTextContent(
        'Sampled Unavailable',
      );
      expect(screen.getByTestId(`profile-resource-host-${profileId}`)).toHaveTextContent(
        'Unavailable',
      );
      expect(screen.getByTestId(`profile-resource-manager-${profileId}`)).toHaveTextContent(
        'Unavailable',
      );
      expect(screen.getByTestId(`profile-resource-workers-${profileId}`)).toHaveTextContent(
        'Unavailable',
      );
    }
    expect(screen.getByTestId('slot-memory-repo-legacy-omitted-000001')).toHaveTextContent(
      'Unavailable',
    );
    expect(screen.getByTestId('slot-pids-repo-legacy-null-000001')).toHaveTextContent(
      'Unavailable',
    );
  });
});
