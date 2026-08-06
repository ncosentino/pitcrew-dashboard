import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { FleetNode, OperationalIncident } from '@/core/fleet';

import { NodePressureCommandCenter } from './NodePressureCommandCenter';

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe('NodePressureCommandCenter', () => {
  it('attributes concurrent pressure to exact GitHub jobs and keeps cancellation at the source', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          generatedAt: '2026-08-06T04:29:00+00:00',
          incidents: [],
          truncated: false,
        }),
        { headers: { 'Content-Type': 'application/json' } },
      ),
    );
    const node = createNode();
    const incident = createIncident(node.nodeId);

    render(
      <NodePressureCommandCenter
        activeIncidents={[incident]}
        generatedAt="2026-08-06T04:20:00+00:00"
        node={node}
        tenantId="local"
      />,
    );

    expect(screen.getByText('97.5%')).toBeInTheDocument();
    expect(screen.getByText('Android debug build')).toBeInTheDocument();
    const link = screen.getByRole('link', { name: 'Open in GitHub' });
    expect(link).toHaveAttribute(
      'href',
      'https://github.com/ncosentino/genesis/actions/runs/31068390178/job/92513140749',
    );
    expect(screen.getByText('Zephyr has sustained Docker-host CPU pressure')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /cancel/iu })).not.toBeInTheDocument();
  });
});

function createNode(): FleetNode {
  return {
    nodeId: '11111111-1111-1111-1111-111111111111',
    displayName: 'Zephyr',
    connectorVersion: '0.8.0',
    enrolledAt: '2026-08-01T00:00:00+00:00',
    lastSeenAt: '2026-08-06T04:20:00+00:00',
    isOnline: true,
    isRevoked: false,
    credentialRotationRequested: false,
    capacityControls: [],
    recoveryControls: [],
    profiles: [
      {
        schemaVersion: 1,
        managerContractVersion: 16,
        profileId: 'genesis-ci',
        managerInstanceId: 'manager-1',
        managerStatus: 'running',
        observedAt: '2026-08-06T04:20:00+00:00',
        scope: 'repo',
        generation: 1,
        desiredStateHash: null,
        desiredStateStatus: 'accepted',
        desiredSlots: 1,
        activeSlots: 1,
        eligibleSlots: 1,
        drainingSlots: 0,
        configuredSlots: 8,
        resourcePolicy: null,
        operationJournal: null,
        subsystemHealth: null,
        capacityEvidence: null,
        update: null,
        host: null,
        resourceTelemetry: {
          sampledAt: '2026-08-06T04:20:00+00:00',
          status: 'available',
          host: {
            logicalProcessorCount: 16,
            memoryBytes: 34_359_738_368,
          },
          manager: {
            cpuCores: 0.1,
            memoryWorkingSetBytes: 33_554_432,
            pids: 7,
            networkRxBytes: null,
            networkTxBytes: null,
            blockReadBytes: null,
            blockWriteBytes: null,
          },
          hostPressure: {
            status: 'available',
            source: 'docker-host',
            cpuUtilizationPercent: 97.5,
            load1: 18,
            load5: 12,
            load15: 8,
            memoryTotalBytes: 34_359_738_368,
            memoryAvailableBytes: 2_147_483_648,
            swapUsedBytes: 1_073_741_824,
            cpuPressureSomeAvg10: 35,
            cpuPressureFullAvg10: 5,
            memoryPressureSomeAvg10: 25,
            memoryPressureFullAvg10: 3,
            ioPressureSomeAvg10: 42,
            ioPressureFullAvg10: 18,
          },
        },
        autoscaling: {
          mode: 'scale-set',
          status: 'running',
          minimumIdleSlots: 1,
          maximumSlots: 8,
          targetSlots: 1,
          assignedJobs: 2,
          runningJobs: 2,
          availableJobs: 0,
          idleRunners: 0,
          busyRunners: 1,
          scaleDownDelaySeconds: 120,
          scaleSetCount: 1,
          scaleDownAt: null,
          lastError: null,
          maximumActiveWorkers: 8,
          targets: [],
        },
        slots: [
          {
            key: 'slot-1',
            repository: 'https://github.com/ncosentino/genesis',
            desired: true,
            processRunning: true,
            state: 'online',
            failureCount: 0,
            backoffSeconds: 0,
            updatedAt: '2026-08-06T03:42:03+00:00',
            activity: 'busy',
            target: 'repo:genesis',
            registrationStatus: 'connected',
            imageId: null,
            lastExit: null,
            runnerNameHash: 'a'.repeat(64),
            resources: {
              cpuCores: 14.5,
              memoryWorkingSetBytes: 12_884_901_888,
              pids: 512,
              networkRxBytes: null,
              networkTxBytes: null,
              blockReadBytes: null,
              blockWriteBytes: null,
            },
            currentJob: {
              repository: 'https://github.com/ncosentino/genesis',
              workflowRunId: 31_068_390_178,
              jobId: '92513140749',
              displayName: 'Android debug build',
              eventName: 'push',
              queuedAt: '2026-08-06T03:40:00+00:00',
              scaleSetAssignedAt: '2026-08-06T03:41:00+00:00',
              runnerAssignedAt: '2026-08-06T03:41:30+00:00',
              startedAt: '2026-08-06T03:42:03+00:00',
              finishedAt: null,
              result: null,
            },
          },
        ],
      },
    ],
    hardware: null,
  };
}

function createIncident(nodeId: string): OperationalIncident {
  return {
    incidentId: '22222222-2222-2222-2222-222222222222',
    nodeId,
    profileId: null,
    kind: 'host-cpu-pressure',
    severity: 'warning',
    status: 'triggered',
    title: 'Zephyr has sustained Docker-host CPU pressure',
    summary: 'The newest four host samples exceed a CPU threshold.',
    reason: 'sustained-host-cpu-pressure',
    evidence: 'peakCpuPercent=97.5',
    link: `/tenants/local/nodes/${nodeId}`,
    firstObservedAt: '2026-08-06T04:19:00+00:00',
    triggeredAt: '2026-08-06T04:20:00+00:00',
    lastObservedAt: '2026-08-06T04:20:00+00:00',
    acknowledgedAt: null,
    acknowledgedByGitHubUserId: null,
    resolvedAt: null,
  };
}
