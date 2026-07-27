import {
  type CapacityCommandState,
  type FleetNode,
  type ManagerObservedState,
  type RecoveryCommandState,
  type RecoveryControlState,
} from '@/core/fleet';

/** Expected connector evidence one recovery request is fenced against. */
export interface RecoveryFences {
  readonly expectedManagerInstanceId: string;
  readonly expectedGeneration: number;
  readonly expectedDesiredStateHash: string | null;
}

/** Bounded reason a profile cannot currently be recovered. */
export type RecoveryUnavailableCode =
  | 'not-authorized'
  | 'connector-read-only'
  | 'connector-offline'
  | 'node-revoked'
  | 'locally-disallowed'
  | 'legacy-contract'
  | 'manager-not-running'
  | 'manager-unresolved'
  | 'observation-stale'
  | 'operation-active'
  | 'recovery-active'
  | 'capacity-active'
  | 'fence-unavailable';

/** Current recovery availability with a specific operator explanation. */
export interface RecoveryAvailability {
  readonly canQueue: boolean;
  readonly code: RecoveryUnavailableCode | null;
  readonly explanation: string | null;
  readonly fences: RecoveryFences | null;
}

/** Terminal recovery statuses that never become success-shaped afterwards. */
const terminalStatuses = new Set(['succeeded', 'rejected', 'failed', 'expired', 'indeterminate']);

/** Reports whether a recovery command still occupies the profile. */
export function isRecoveryCommandActive(command: RecoveryCommandState | null): boolean {
  return command !== null && !terminalStatuses.has(command.status);
}

/** Reports whether a capacity command still occupies the profile. */
export function isCapacityCommandActive(command: CapacityCommandState | null): boolean {
  return command !== null && (command.status === 'pending' || command.status === 'delivered');
}

function ageSeconds(from: string, to: string): number {
  return Math.max(0, Math.round((new Date(to).getTime() - new Date(from).getTime()) / 1_000));
}

interface RecoveryAvailabilityInput {
  readonly node: FleetNode;
  readonly profile: ManagerObservedState;
  readonly control: RecoveryControlState | null;
  readonly capacityCommand: CapacityCommandState | null;
  readonly canAdminister: boolean;
  readonly generatedAt: string;
}

function unavailable(
  code: RecoveryUnavailableCode,
  explanation: string,
  fences: RecoveryFences | null,
): RecoveryAvailability {
  return { canQueue: false, code, explanation, fences };
}

/**
 * Resolves whether recovery may be queued and, when it may not, why.
 *
 * The evaluation mirrors the dashboard queueing checks so an enabled control
 * never depends on the server rejecting a request the operator could not
 * anticipate.
 */
export function describeRecoveryAvailability(
  input: RecoveryAvailabilityInput,
): RecoveryAvailability {
  const { node, profile, control, capacityCommand, canAdminister, generatedAt } = input;
  const fences =
    control?.expectedManagerInstanceId == null
      ? null
      : {
          expectedManagerInstanceId: control.expectedManagerInstanceId,
          expectedGeneration: control.desiredGeneration,
          expectedDesiredStateHash: control.desiredStateHash,
        };

  if (!canAdminister) {
    return unavailable(
      'not-authorized',
      'Only tenant administrators can queue manager recovery. Viewers keep read-only access.',
      fences,
    );
  }
  if (control === null) {
    return unavailable(
      'connector-read-only',
      'This connector does not advertise host-operator manager recovery for this profile. Read-only container connectors never expose recovery.',
      fences,
    );
  }
  if (node.isRevoked) {
    return unavailable(
      'node-revoked',
      'This node credential is revoked, so the connector can no longer claim operations.',
      fences,
    );
  }
  if (!node.isOnline) {
    return unavailable(
      'connector-offline',
      'The connector is offline. Recovery requires a connector that is currently reporting.',
      fences,
    );
  }
  if (!control.recoveryAllowed) {
    return unavailable(
      'locally-disallowed',
      'Local connector policy does not allow manager recovery for this profile. Only the host operator can change that allowlist.',
      fences,
    );
  }
  if (!control.managerContractSupported || control.managerContractVersion < 9) {
    return unavailable(
      'legacy-contract',
      `The manager implements contract ${control.managerContractVersion}; recovery requires manager contract 9 or newer.`,
      fences,
    );
  }
  if (profile.managerStatus !== 'running') {
    return unavailable(
      'manager-not-running',
      `The observed manager is ${profile.managerStatus}. Recovery restarts one running manager and never starts a stopped or missing one.`,
      fences,
    );
  }
  if (!control.singleManagerResolved) {
    return unavailable(
      'manager-unresolved',
      'Exactly one running manager could not be resolved locally. Ambiguous or duplicated managers are never remotely recoverable.',
      fences,
    );
  }
  const capabilityAgeSeconds =
    node.lastSeenAt === null ? Number.POSITIVE_INFINITY : ageSeconds(node.lastSeenAt, generatedAt);
  if (
    control.observedStateAgeSeconds > control.observedStateMaximumAgeSeconds ||
    capabilityAgeSeconds > control.observedStateMaximumAgeSeconds
  ) {
    return unavailable(
      'observation-stale',
      `The manager evidence behind this control is stale. Recovery requires connector evidence newer than ${control.observedStateMaximumAgeSeconds} seconds; the connector observed this manager ${control.observedStateAgeSeconds} seconds before its last report.`,
      fences,
    );
  }
  if (control.operationActive) {
    return unavailable(
      'operation-active',
      'The connector reports another local operation for this profile. One profile runs at most one operation at a time.',
      fences,
    );
  }
  if (isRecoveryCommandActive(control.latestCommand)) {
    return unavailable(
      'recovery-active',
      'A recovery command for this profile is already in progress. Wait for its recorded outcome before queueing another.',
      fences,
    );
  }
  if (isCapacityCommandActive(capacityCommand)) {
    return unavailable(
      'capacity-active',
      'A capacity command is active for this profile. Capacity and recovery never overlap.',
      fences,
    );
  }
  if (fences === null) {
    return unavailable(
      'fence-unavailable',
      'The connector has not advertised a manager instance to fence this request against.',
      fences,
    );
  }
  return { canQueue: true, code: null, explanation: null, fences };
}

/**
 * Produces the confirmation identity of the currently displayed evidence.
 *
 * A confirmation is only valid while this signature is unchanged, so a refresh
 * or capability change invalidates an in-progress confirmation.
 */
export function recoveryConfirmationSignature(
  availability: RecoveryAvailability,
  control: RecoveryControlState | null,
): string {
  return [
    availability.canQueue ? 'available' : (availability.code ?? 'unavailable'),
    availability.fences?.expectedManagerInstanceId ?? 'none',
    availability.fences?.expectedGeneration ?? 'none',
    availability.fences?.expectedDesiredStateHash ?? 'none',
    control?.latestCommand?.commandId ?? 'none',
  ].join('|');
}
