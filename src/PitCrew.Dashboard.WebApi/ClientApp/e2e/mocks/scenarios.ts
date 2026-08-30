/**
 * Named application states used across the browser evidence suite. Each
 * scenario is a fully-formed, schema-validated fixture set (see
 * `mocks/fixtures.ts`) representing one of the states the UX browser matrix
 * exercises.
 */
import {
  buildDiagnosticCredentialCreated,
  buildDiagnosticCredentials,
  buildEnrollmentCode,
  buildFleetNode,
  buildFleetResponse,
  buildIncident,
  buildIncidentPage,
  buildImageBuildRequest,
  buildImageCampaign,
  buildImageCampaignSummary,
  buildImageCandidate,
  buildImageRecipeRegistration,
  buildProfileImageRolloutControl,
  buildProfile,
  buildSession,
  buildTenantMembers,
  buildAvailableUsers,
  nodeIds,
  tenantId,
} from './fixtures';
import type { MockApiOptions } from './router';

export { tenantId, nodeIds };

function buildScenarioSupport(
  role: Parameters<typeof buildSession>[0] = 'owner',
): Omit<MockApiOptions, 'fleet' | 'incidents'> {
  return {
    session: buildSession(role),
    tenantMembers: buildTenantMembers(),
    availableUsers: buildAvailableUsers(),
    diagnosticCredentials: buildDiagnosticCredentials(),
    enrollmentCode: buildEnrollmentCode(),
    diagnosticCredentialCreated: buildDiagnosticCredentialCreated(),
    imageRecipeRegistrations: [buildImageRecipeRegistration()],
    imageBuildRequests: [buildImageBuildRequest()],
    imageCandidates: [buildImageCandidate()],
    imageCampaigns: [buildImageCampaignSummary()],
    imageCampaignDetails: [buildImageCampaign()],
    profileImageRolloutControl: buildProfileImageRolloutControl(),
  };
}

/** Healthy fleet: all nodes online, no active incidents, no findings. */
export function healthyScenario(): MockApiOptions {
  const alpha = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: 'Alpha',
    isOnline: true,
    profiles: [buildProfile('build'), buildProfile('deploy')],
  });
  const bravo = buildFleetNode({
    nodeId: nodeIds.bravo,
    displayName: 'Bravo',
    isOnline: true,
    profiles: [buildProfile('build')],
  });
  return {
    ...buildScenarioSupport('owner'),
    fleet: buildFleetResponse([alpha, bravo], []),
    incidents: buildIncidentPage([]),
  };
}

/** One node has a triggered, unacknowledged capacity-deficit incident. */
export function activeIncidentScenario(): MockApiOptions {
  const incident = buildIncident();
  const alpha = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: 'Alpha',
    isOnline: true,
    profiles: [buildProfile('build')],
  });
  return {
    ...buildScenarioSupport('owner'),
    fleet: buildFleetResponse([alpha], [incident]),
    incidents: buildIncidentPage([incident]),
  };
}

/** One node is offline with stale hardware telemetry and a degraded connector. */
export function offlineStaleScenario(): MockApiOptions {
  const alpha = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: 'Alpha',
    isOnline: true,
    profiles: [buildProfile('build')],
  });
  const bravo = buildFleetNode({
    nodeId: nodeIds.bravo,
    displayName: 'Bravo',
    isOnline: false,
    hardwareStatus: 'stale',
    connectorFailure: true,
    profiles: [buildProfile('build')],
  });
  return {
    ...buildScenarioSupport('owner'),
    fleet: buildFleetResponse([alpha, bravo], []),
    incidents: buildIncidentPage([]),
  };
}

/** Connector degraded but still online so node detail keeps current evidence visible. */
export function degradedNodeScenario(): MockApiOptions {
  const node = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: 'Alpha',
    isOnline: true,
    connectorFailure: true,
    profiles: [buildProfile('build'), buildProfile('deploy')],
  });
  return {
    ...buildScenarioSupport('owner'),
    fleet: buildFleetResponse([node], []),
    incidents: buildIncidentPage([]),
  };
}

/** Node pressure is active and pause-safe capacity actions are available. */
export function pressureScenario(): MockApiOptions {
  const baseline = buildProfile('build');
  const pressureProfile = buildProfile('build', {
    managerContractVersion: 16,
    slots: baseline.slots.map((slot) => ({ ...slot, currentJob: null })),
    resourceTelemetry: {
      sampledAt: '2026-07-19T18:30:00+00:00',
      status: 'available',
      host: {
        logicalProcessorCount: 20,
        memoryBytes: 34359738368,
      },
      manager: {
        cpuCores: 0.75,
        memoryWorkingSetBytes: 4096,
        pids: 18,
      },
      hostPressure: {
        status: 'available',
        source: 'docker-host',
        cpuUtilizationPercent: 97,
        load1: 16.2,
        load5: 12.8,
        load15: 8.5,
        memoryTotalBytes: 34359738368,
        memoryAvailableBytes: 1073741824,
        swapUsedBytes: 536870912,
        cpuPressureSomeAvg10: 34.7,
        cpuPressureFullAvg10: 18.1,
        memoryPressureSomeAvg10: 41.3,
        memoryPressureFullAvg10: 22.9,
        ioPressureSomeAvg10: 12.5,
        ioPressureFullAvg10: 6.2,
      },
    },
  });
  const incident = buildIncident({
    incidentId: 'f6235ec4-2a15-4f91-a9e0-811152869a58',
    kind: 'host-memory-pressure',
    profileId: null,
    title: 'Host memory pressure',
    summary: 'Available Docker-host memory is below the safe operating floor on Alpha.',
    reason: 'memory-pressure',
    evidence: 'availableMemoryBytes=1073741824 memoryPressureSomeAvg10=41.3',
    link: `/tenants/${tenantId}/nodes/${nodeIds.alpha}`,
  });
  const node = {
    ...buildFleetNode({
      nodeId: nodeIds.alpha,
      displayName: 'Alpha',
      isOnline: true,
      profiles: [pressureProfile],
    }),
    capacityControls: [
      {
        profileId: 'build',
        generation: pressureProfile.generation,
        currentMaximum: 3,
        maximumAllowed: 6,
        supportsZeroMaximum: true,
        latestCommand: null,
        pauseCommandId: null,
        resumeMaximum: null,
      },
    ],
  };
  return {
    ...buildScenarioSupport('owner'),
    fleet: buildFleetResponse([node], [incident]),
    incidents: buildIncidentPage([incident]),
  };
}

/** One worker is actively running a GitHub job with current activity evidence. */
export function activeJobScenario(): MockApiOptions {
  const baseline = buildProfile('build');
  const activeProfile = buildProfile('build', {
    managerContractVersion: 15,
    slots: [
      {
        ...baseline.slots[0],
        activity: 'busy',
        runnerNameHash: 'a'.repeat(64),
        currentJob: {
          repository: 'https://github.com/example/project',
          workflowRunId: 12345,
          jobId: '67890',
          displayName: 'Compile and verify dashboard assets',
          eventName: 'push',
          queuedAt: '2026-07-19T18:20:00+00:00',
          scaleSetAssignedAt: '2026-07-19T18:21:00+00:00',
          runnerAssignedAt: '2026-07-19T18:22:00+00:00',
          startedAt: '2026-07-19T18:23:00+00:00',
          finishedAt: null,
          result: null,
        },
      },
      {
        ...baseline.slots[1],
        activity: 'idle',
        currentJob: null,
      },
    ],
  });
  const node = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: 'Alpha',
    isOnline: true,
    profiles: [activeProfile],
  });
  return {
    ...buildScenarioSupport('owner'),
    fleet: buildFleetResponse([node], []),
    incidents: buildIncidentPage([]),
  };
}

/** Profile rollout is mid-flight with current and stale workers both present. */
export function rollingImageScenario(): MockApiOptions {
  const rollingProfile = buildProfile('build', {
    update: {
      status: 'rolling',
      targetImage: 'ghcr.io/example/pitcrew-worker:2026.07.19',
      targetImageId: `sha256:${'c'.repeat(64)}`,
      targetRevision: 'd'.repeat(64),
      currentWorkers: 1,
      staleWorkers: 1,
      lastError: null,
    },
  });
  const node = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: 'Alpha',
    isOnline: true,
    profiles: [rollingProfile],
  });
  return {
    ...buildScenarioSupport('owner'),
    fleet: buildFleetResponse([node], []),
    incidents: buildIncidentPage([]),
  };
}

/** Recovery command is active and fenced manager-recovery evidence is visible. */
export function recoveryScenario(): MockApiOptions {
  const profile = buildProfile('build');
  const recoveryCommand = {
    commandId: '06235ec4-2a15-4f91-a9e0-811152869a59',
    status: 'started' as const,
    failureCategory: null,
    requestedByGitHubUserId: '1001',
    requestedAt: '2026-07-19T18:24:00+00:00',
    expiresAt: '2026-07-19T18:39:00+00:00',
    deliveredAt: '2026-07-19T18:24:10+00:00',
    claimedAt: '2026-07-19T18:24:15+00:00',
    startedAt: '2026-07-19T18:24:20+00:00',
    completedAt: null,
    beforeManagerInstanceId: profile.managerInstanceId,
    afterManagerInstanceId: null,
    resultMessage: 'Manager restart claimed and currently executing on the connector.',
  };
  const node = {
    ...buildFleetNode({
      nodeId: nodeIds.alpha,
      displayName: 'Alpha',
      isOnline: true,
      profiles: [profile],
    }),
    recoveryControls: [
      {
        profileId: profile.profileId,
        managerContractVersion: profile.managerContractVersion,
        managerContractSupported: true,
        expectedManagerInstanceId: profile.managerInstanceId,
        desiredGeneration: profile.generation,
        desiredStateHash: profile.desiredStateHash,
        observedStateAgeSeconds: 12,
        observedStateMaximumAgeSeconds: 90,
        recoveryAllowed: true,
        singleManagerResolved: true,
        operationActive: true,
        latestCommand: recoveryCommand,
        recentCommands: [recoveryCommand],
      },
    ],
  };
  return {
    ...buildScenarioSupport('owner'),
    fleet: buildFleetResponse([node], []),
    incidents: buildIncidentPage([]),
  };
}

/** Session only has the `viewer` role so admin actions stay hidden. */
export function readOnlyScenario(): MockApiOptions {
  const alpha = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: 'Alpha',
    isOnline: true,
    profiles: [buildProfile('build'), buildProfile('deploy')],
  });
  return {
    ...buildScenarioSupport('viewer'),
    fleet: buildFleetResponse([alpha], []),
    incidents: buildIncidentPage([]),
  };
}

/** The fleet snapshot endpoint is unreachable (network failure). */
export function unavailableScenario(): MockApiOptions {
  return {
    ...healthyScenario(),
    fleetOutcome: 'network-error',
  };
}

/** Tenant has no enrolled nodes and no incidents. */
export function emptyScenario(): MockApiOptions {
  return {
    ...buildScenarioSupport('owner'),
    fleet: buildFleetResponse([], []),
    incidents: buildIncidentPage([]),
  };
}

/** Session only has the `viewer` role: administration/settings routes must reject access. */
export function permissionLimitedScenario(): MockApiOptions {
  return readOnlyScenario();
}

/** Every write endpoint fails, to exercise `role="alert"` error rendering. */
export function failedMutationScenario(): MockApiOptions {
  return {
    ...healthyScenario(),
    mutationOutcome: 'failure',
  };
}

/** No dashboard session cookie is present; `LoginPage` should render. */
export function unauthenticatedScenario(): MockApiOptions {
  return {
    session: 'unauthenticated',
    fleet: buildFleetResponse([], []),
    incidents: buildIncidentPage([]),
  };
}

/**
 * `GET /api/session` fails with a 500 (not a 401), so `SessionProvider`
 * lands in its `'error'` status instead of `'unauthenticated'`, and
 * `SessionBoundary` renders the `role="alert"` retry surface rather than
 * `LoginPage`.
 */
export function sessionErrorScenario(): MockApiOptions {
  return {
    session: 'unauthenticated',
    sessionOutcome: 'server-error',
    fleet: buildFleetResponse([], []),
    incidents: buildIncidentPage([]),
  };
}
