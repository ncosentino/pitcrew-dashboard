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
      expiresAt: '2026-08-01T09:00:00+00:00',
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
