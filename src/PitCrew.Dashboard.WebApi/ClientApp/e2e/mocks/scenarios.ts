/**
 * Named application states used across the browser evidence suite. Each
 * scenario is a fully-formed, schema-validated fixture set (see
 * `mocks/fixtures.ts`) representing one of the states issue #84 requires
 * coverage for: healthy, active incident, offline/stale, unavailable, empty,
 * permission-limited, and failed-mutation.
 */
import {
  buildDiagnosticCredentialCreated,
  buildDiagnosticCredentials,
  buildEnrollmentCode,
  buildFleetNode,
  buildFleetResponse,
  buildIncident,
  buildIncidentPage,
  buildProfile,
  buildSession,
  buildTenantMembers,
  buildAvailableUsers,
  nodeIds,
  tenantId,
} from './fixtures';
import type { MockApiOptions } from './router';

export { tenantId, nodeIds };

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
    session: buildSession('owner'),
    fleet: buildFleetResponse([alpha, bravo], []),
    incidents: buildIncidentPage([]),
    tenantMembers: buildTenantMembers(),
    availableUsers: buildAvailableUsers(),
    diagnosticCredentials: buildDiagnosticCredentials(),
    enrollmentCode: buildEnrollmentCode(),
    diagnosticCredentialCreated: buildDiagnosticCredentialCreated(),
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
  const base = healthyScenario();
  return {
    ...base,
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
  const base = healthyScenario();
  return {
    ...base,
    fleet: buildFleetResponse([alpha, bravo], []),
  };
}

/** The fleet snapshot endpoint is unreachable (network failure). */
export function unavailableScenario(): MockApiOptions {
  const base = healthyScenario();
  return {
    ...base,
    fleetOutcome: 'network-error',
  };
}

/** Tenant has no enrolled nodes and no incidents. */
export function emptyScenario(): MockApiOptions {
  return {
    session: buildSession('owner'),
    fleet: buildFleetResponse([], []),
    incidents: buildIncidentPage([]),
    tenantMembers: buildTenantMembers(),
    availableUsers: buildAvailableUsers(),
    diagnosticCredentials: [],
  };
}

/** Session only has the `viewer` role: administration/settings routes must reject access. */
export function permissionLimitedScenario(): MockApiOptions {
  const base = healthyScenario();
  return {
    ...base,
    session: buildSession('viewer'),
  };
}

/** Every write endpoint fails, to exercise `role="alert"` error rendering. */
export function failedMutationScenario(): MockApiOptions {
  const base = healthyScenario();
  return {
    ...base,
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
