/**
 * Sanitized fixture builders for the browser evidence harness.
 *
 * Every builder returns a value validated against the same zod schema the
 * production API clients parse responses with (see the `Schema.parse` calls
 * below). None of these identifiers, hostnames, or tokens are real: node and
 * profile IDs are fixed synthetic UUIDs, repositories point at
 * `github.com/example/*`, and credential-shaped strings are literal
 * placeholders rather than issued secrets.
 */
import {
  dashboardSessionSchema,
  dashboardUserSchema,
  tenantAccessSchema,
  type DashboardSession,
  type DashboardUser,
  type TenantAccess,
  type TenantRole,
} from '../../src/core/auth/sessionApi';
import {
  fleetNodeSchema,
  fleetResponseSchema,
  managerObservedStateSchema,
  operationalIncidentSchema,
  type FleetNode,
  type FleetResponse,
  type ManagerObservedState,
  type OperationalIncident,
} from '../../src/core/fleet/fleetApi';
import { incidentPageSchema, type IncidentPage } from '../../src/features/fleet/incidentsApi';
import {
  profileImageRolloutCommandSchema,
  profileImageRolloutControlSchema,
  type ProfileImageRolloutCommand,
  type ProfileImageRolloutControl,
} from '../../src/features/fleet/imageRolloutApi';
import {
  imageBuildRequestSchema,
  imageCandidateSchema,
  imageRecipeRegistrationSchema,
  type ImageBuildRequest,
  type ImageCandidate,
  type ImageRecipeRegistration,
} from '../../src/features/images/imagesApi';
import {
  diagnosticCredentialCreatedSchema,
  diagnosticCredentialSchema,
  dashboardUsersSchema,
  enrollmentCodeResponseSchema,
  tenantMembersSchema,
  type DiagnosticCredential,
  type DiagnosticCredentialCreated,
  type EnrollmentCodeResponse,
  type TenantMember,
} from '../../src/features/settings/settingsApi';

/** Fixed synthetic node/profile identifiers reused across scenarios. */
export const nodeIds = {
  alpha: 'a1235ec4-2a15-4f91-a9e0-811152869a51',
  bravo: 'b2235ec4-2a15-4f91-a9e0-811152869a52',
  charlie: 'c3235ec4-2a15-4f91-a9e0-811152869a53',
} as const;

export const tenantId = 'local';

export function buildUser(overrides: Partial<DashboardUser> = {}): DashboardUser {
  return dashboardUserSchema.parse({
    githubUserId: '1001',
    githubLogin: 'operator',
    displayName: 'Operator',
    avatarUrl: null,
    ...overrides,
  });
}

export function buildTenantAccess(
  role: TenantRole,
  overrides: Partial<TenantAccess> = {},
): TenantAccess {
  return tenantAccessSchema.parse({
    tenantId,
    displayName: 'Local fleet',
    role,
    ...overrides,
  });
}

/** Builds a validated session. `tenants: []` models the permission-limited/no-access case. */
export function buildSession(
  role: TenantRole | null,
  overrides: Partial<DashboardSession> = {},
): DashboardSession {
  return dashboardSessionSchema.parse({
    user: buildUser(),
    isSystemAdministrator: false,
    tenants: role === null ? [] : [buildTenantAccess(role)],
    antiforgeryToken: 'e2e-antiforgery-token',
    ...overrides,
  });
}

export function buildImageRecipeRegistration(
  overrides: Partial<ImageRecipeRegistration> = {},
): ImageRecipeRegistration {
  return imageRecipeRegistrationSchema.parse({
    registrationId: '70100000-0000-4000-8000-000000000001',
    version: 1,
    githubInstallationId: '101',
    githubRepositoryId: '202',
    githubWorkflowId: '303',
    repositoryOwner: 'example',
    repositoryName: 'runner-images',
    workflowPath: '.github/workflows/build-runner-image.yml',
    workflowBlobSha: 'a'.repeat(40),
    dispatchRef: 'refs/heads/main',
    recipeId: 'ubuntu-runner',
    candidateSchemaVersion: 1,
    allowedSourceRefs: ['refs/heads/main'],
    inputs: [
      {
        name: 'ubuntuVersion',
        type: 'string',
        required: true,
        maxLength: 16,
        allowedValues: ['24.04', '22.04'],
      },
    ],
    createdByGitHubUserId: '1001',
    createdAt: '2026-08-28T12:00:00+00:00',
    disabledByGitHubUserId: null,
    disabledAt: null,
    ...overrides,
  });
}

export function buildImageBuildRequest(
  overrides: Partial<ImageBuildRequest> = {},
): ImageBuildRequest {
  return imageBuildRequestSchema.parse({
    requestId: '70200000-0000-4000-8000-000000000002',
    registrationId: '70100000-0000-4000-8000-000000000001',
    registrationVersion: 1,
    recipeId: 'ubuntu-runner',
    sourceRepository: 'example/runner-images',
    sourceRef: 'refs/heads/main',
    sourceCommit: 'b'.repeat(40),
    status: 'ready',
    githubRunId: '98765',
    githubRunApiUrl: 'https://api.github.com/repos/example/runner-images/actions/runs/98765',
    githubRunHtmlUrl: 'https://github.com/example/runner-images/actions/runs/98765',
    terminalCategory: null,
    terminalDetail: null,
    requestedAt: '2026-08-28T12:05:00+00:00',
    updatedAt: '2026-08-28T12:15:00+00:00',
    ...overrides,
  });
}

export function buildImageCandidate(overrides: Partial<ImageCandidate> = {}): ImageCandidate {
  return imageCandidateSchema.parse({
    candidateId: '70300000-0000-4000-8000-000000000003',
    requestId: '70200000-0000-4000-8000-000000000002',
    registrationId: '70100000-0000-4000-8000-000000000001',
    registrationVersion: 1,
    outcome: 'ready',
    recipeId: 'ubuntu-runner',
    sourceRepository: 'example/runner-images',
    sourceCommit: 'b'.repeat(40),
    githubRunId: '98765',
    githubRunApiUrl: 'https://api.github.com/repos/example/runner-images/actions/runs/98765',
    githubRunUrl: 'https://github.com/example/runner-images/actions/runs/98765',
    artifactId: '4567',
    artifactName: 'pitcrew-image-candidate',
    artifactDigest: `sha256:${'c'.repeat(64)}`,
    reportHash: 'd'.repeat(64),
    imageReference: 'ghcr.io/example/runner:candidate',
    digest: `sha256:${'e'.repeat(64)}`,
    immutableReference: `ghcr.io/example/runner@sha256:${'e'.repeat(64)}`,
    platform: 'linux/amd64',
    outputMode: 'registry',
    failureCategory: null,
    failureDetail: null,
    createdAt: '2026-08-28T12:14:00+00:00',
    storedAt: '2026-08-28T12:15:00+00:00',
    qualifications: [
      { name: 'image-build', status: 'passed' },
      { name: 'buildkit-digest', status: 'passed' },
      { name: 'registry-digest', status: 'passed' },
      { name: 'oci-manifest', status: 'passed' },
      { name: 'builder-cleanup', status: 'passed' },
    ],
    ...overrides,
  });
}

export function buildProfileImageRolloutControl(
  overrides: Partial<ProfileImageRolloutControl> = {},
): ProfileImageRolloutControl {
  return profileImageRolloutControlSchema.parse({
    nodeId: nodeIds.alpha,
    profileId: 'build',
    architecture: 'linux/amd64',
    currentImageReference: 'ghcr.io/example/runner:current',
    currentImageDigest: `sha256:${'1'.repeat(64)}`,
    currentLocalImageId: `sha256:${'2'.repeat(64)}`,
    currentWorkerRevision: '3'.repeat(64),
    staticFingerprint: '4'.repeat(64),
    preservedConfigurationFingerprint: '5'.repeat(64),
    routingFingerprint: '6'.repeat(64),
    desiredGeneration: 4,
    desiredStateHash: 'a'.repeat(64),
    allowedRecipeIds: ['ubuntu-runner'],
    rolloutAllowed: true,
    localSchemaSupported: true,
    localFailureCategory: null,
    operationActive: false,
    observedStateAgeSeconds: 5,
    observedStateMaximumAgeSeconds: 120,
    observedStateFresh: true,
    managerConvergenceStatus: 'current',
    currentWorkers: 2,
    staleWorkers: 0,
    latestCommand: null,
    recentCommands: [],
    ...overrides,
  });
}

export function buildProfileImageRolloutCommand(
  overrides: Partial<ProfileImageRolloutCommand> = {},
): ProfileImageRolloutCommand {
  return profileImageRolloutCommandSchema.parse({
    commandId: '70400000-0000-4000-8000-000000000004',
    candidateId: '70300000-0000-4000-8000-000000000003',
    recipeId: 'ubuntu-runner',
    targetDigest: `sha256:${'e'.repeat(64)}`,
    targetPlatform: 'linux/amd64',
    previousImageReference: null,
    previousImageDigest: null,
    previousWorkerRevision: null,
    status: 'started',
    failureCategory: null,
    requestedByGitHubUserId: '1001',
    requestedAt: '2026-08-28T12:20:00+00:00',
    expiresAt: '2026-08-28T12:50:00+00:00',
    deliveredAt: '2026-08-28T12:20:05+00:00',
    claimedAt: '2026-08-28T12:20:06+00:00',
    startedAt: '2026-08-28T12:20:07+00:00',
    completedAt: null,
    targetWorkerRevision: null,
    managerConvergenceStatus: null,
    currentWorkers: null,
    staleWorkers: null,
    lastError: null,
    resultMessage: null,
    previousCandidateId: null,
    previousRecipeId: null,
    ...overrides,
  });
}

function baseProfile(profileId: string): ManagerObservedState {
  return managerObservedStateSchema.parse({
    schemaVersion: 1,
    managerContractVersion: 13,
    profileId,
    managerInstanceId: `manager-${profileId}`,
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
        key: `${profileId}-000001`,
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
        key: `${profileId}-000002`,
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
  });
}

export function buildProfile(
  profileId: string,
  overrides: Partial<ManagerObservedState> = {},
): ManagerObservedState {
  return managerObservedStateSchema.parse({ ...baseProfile(profileId), ...overrides });
}

interface FleetNodeOptions {
  readonly nodeId: string;
  readonly displayName: string;
  readonly isOnline: boolean;
  readonly isRevoked?: boolean;
  readonly hardwareStatus?: 'current' | 'stale' | 'unavailable';
  readonly profiles?: ReadonlyArray<ManagerObservedState>;
  readonly connectorFailure?: boolean;
}

export function buildFleetNode(options: FleetNodeOptions): FleetNode {
  const hardwareStatus = options.hardwareStatus ?? 'current';
  const node = {
    nodeId: options.nodeId,
    displayName: options.displayName,
    connectorVersion: '3.0.0',
    enrolledAt: '2026-07-17T15:00:00+00:00',
    lastSeenAt: options.isOnline ? '2026-07-19T18:30:05+00:00' : '2026-07-17T16:00:00+00:00',
    isOnline: options.isOnline,
    isRevoked: options.isRevoked ?? false,
    credentialRotationRequested: false,
    profiles: options.profiles ?? [],
    capacityControls: [],
    recoveryControls: [],
    hardware:
      hardwareStatus === 'unavailable'
        ? null
        : {
            status: hardwareStatus,
            collectedAt:
              hardwareStatus === 'stale'
                ? '2026-07-18T18:00:00+00:00'
                : '2026-07-19T18:00:00+00:00',
            attemptedAt: '2026-07-19T18:30:00+00:00',
            inventoryHash: 'a'.repeat(64),
            processorModel: 'Example Processor 9000',
            architecture: 'amd64',
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
      nodeId: options.nodeId,
      receivedAt: '2026-07-19T17:31:00+00:00',
      snapshot: {
        state: options.connectorFailure ? 'degraded' : 'healthy',
        processStartedAt: '2026-07-18T12:00:00+00:00',
        updatedAt: '2026-07-19T17:31:00+00:00',
        lastAttemptAt: '2026-07-19T17:31:00+00:00',
        lastSuccessAt: options.connectorFailure ? null : '2026-07-19T17:31:00+00:00',
        activeOutageId: options.connectorFailure ? 'd6235ec4-2a15-4f91-a9e0-811152869a54' : null,
        activeOutageStartedAt: options.connectorFailure ? '2026-07-19T17:20:00+00:00' : null,
        lastFailureAt: '2026-07-19T17:30:00+00:00',
        lastFailureCategory: 'synchronization-network',
        lastFailureProfileId: null,
        lastFailureDetail: 'Connector synchronization could not reach Dashboard.',
        consecutiveFailures: options.connectorFailure ? 3 : 0,
        nextRetryAt: options.connectorFailure ? '2026-07-19T17:35:00+00:00' : null,
        lastRecoveredOutageId: 'd6235ec4-2a15-4f91-a9e0-811152869a54',
        lastRecoveredOutageStartedAt: '2026-07-19T17:20:00+00:00',
        lastRecoveredAt: '2026-07-19T17:31:00+00:00',
        lastRecoveredFailureCategory: 'synchronization-network',
      },
    },
  };
  return fleetNodeSchema.parse(node);
}

export function buildIncident(overrides: Partial<OperationalIncident> = {}): OperationalIncident {
  return operationalIncidentSchema.parse({
    incidentId: 'e6235ec4-2a15-4f91-a9e0-811152869a55',
    nodeId: nodeIds.alpha,
    profileId: 'build',
    kind: 'capacity-deficit',
    severity: 'critical',
    status: 'triggered',
    title: 'Sustained capacity deficit',
    summary: 'Profile "build" on Alpha has run below target capacity for over 10 minutes.',
    reason: 'eligibility-deficit',
    evidence: 'eligibleWorkers=1 targetSlots=3',
    link: `/tenants/${tenantId}/nodes/${nodeIds.alpha}/profiles/build/capacity`,
    firstObservedAt: '2026-07-19T18:00:00+00:00',
    triggeredAt: '2026-07-19T18:10:00+00:00',
    lastObservedAt: '2026-07-19T18:30:00+00:00',
    acknowledgedAt: null,
    acknowledgedByGitHubUserId: null,
    resolvedAt: null,
    ...overrides,
  });
}

export function buildFleetResponse(
  nodes: ReadonlyArray<FleetNode>,
  activeIncidents: ReadonlyArray<OperationalIncident> = [],
): FleetResponse {
  return fleetResponseSchema.parse({
    generatedAt: '2026-07-19T18:30:05+00:00',
    nodes,
    activeIncidents,
  });
}

export function buildIncidentPage(
  incidents: ReadonlyArray<OperationalIncident>,
  truncated = false,
): IncidentPage {
  return incidentPageSchema.parse({
    generatedAt: '2026-07-19T18:30:05+00:00',
    incidents,
    truncated,
  });
}

export function buildTenantMembers(): ReadonlyArray<TenantMember> {
  return tenantMembersSchema.parse([
    {
      user: buildUser(),
      role: 'owner',
      createdAt: '2026-06-01T09:00:00+00:00',
    },
    {
      user: buildUser({
        githubUserId: '1002',
        githubLogin: 'second-operator',
        displayName: 'Second Operator',
      }),
      role: 'viewer',
      createdAt: '2026-06-15T09:00:00+00:00',
    },
  ]);
}

export function buildAvailableUsers(): ReadonlyArray<DashboardUser> {
  return dashboardUsersSchema.parse([
    buildUser({
      githubUserId: '1003',
      githubLogin: 'third-operator',
      displayName: 'Third Operator',
    }),
  ]);
}

export function buildDiagnosticCredentials(): ReadonlyArray<DiagnosticCredential> {
  return [
    diagnosticCredentialSchema.parse({
      credentialId: 'f6235ec4-2a15-4f91-a9e0-811152869a56',
      label: 'Read-only fleet audit',
      createdByGitHubUserId: '1001',
      createdAt: '2026-07-01T09:00:00+00:00',
      expiresAt: '2027-08-01T09:00:00+00:00',
      revokedAt: null,
      revokedByGitHubUserId: null,
      rotatedFromCredentialId: null,
      lastUsedAt: '2026-07-19T08:00:00+00:00',
      useCount: 4,
      nodeIds: [nodeIds.alpha],
      profileIds: ['build'],
    }),
  ];
}

export function buildEnrollmentCode(): EnrollmentCodeResponse {
  return enrollmentCodeResponseSchema.parse({
    enrollmentCodeId: '17235ec4-2a15-4f91-a9e0-811152869a57',
    code: 'placeholder-enrollment-code',
    expiresAt: '2026-07-19T19:00:00+00:00',
  });
}

export function buildDiagnosticCredentialCreated(): DiagnosticCredentialCreated {
  return diagnosticCredentialCreatedSchema.parse({
    credential: buildDiagnosticCredentials()[0],
    value: 'placeholder-diagnostic-credential-value',
  });
}
