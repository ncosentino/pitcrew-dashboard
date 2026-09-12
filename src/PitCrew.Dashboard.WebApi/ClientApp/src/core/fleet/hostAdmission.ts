import {
  type HostAdmissionAccounting,
  type HostAdmissionState,
  type ManagerObservedState,
} from './fleetApi';

type WithholdingReason = NonNullable<HostAdmissionAccounting['withholdingReason']>;

export interface HostAdmissionSummary {
  readonly status: 'disabled' | 'available' | 'degraded' | 'unavailable';
  readonly label: string;
  readonly description: string;
}

export interface NodeHostAdmissionSummary {
  readonly status: HostAdmissionSummary['status'];
  readonly configuredProfiles: number;
  readonly borrowedUnits: number | null;
  readonly withheldUnits: number | null;
}

export interface HostAdmissionWithholdingSummary {
  readonly reason: WithholdingReason;
  readonly label: string;
  readonly description: string;
}

const withholdingSummaries: Record<
  WithholdingReason,
  Omit<HostAdmissionWithholdingSummary, 'reason'>
> = {
  'budget-exhausted': {
    label: 'Host budget exhausted',
    description: 'The remaining effective host budget cannot fit another worker for this profile.',
  },
  'protected-reservation': {
    label: 'Protected reservation',
    description:
      'Otherwise-free units are reserved and are not borrowable by this profile under the current policy.',
  },
  'fair-share-contention': {
    label: 'Fair-share contention',
    description:
      "Shared units currently preserve another contender's opportunity under the coordinator's fair-share policy.",
  },
  'adoption-pending': {
    label: 'Worker adoption pending',
    description: 'Existing-worker recovery is fencing new admission until adoption completes.',
  },
};

/** Explains a bounded coordinator-owned withholding reason without inferring a cause. */
export function describeHostAdmissionWithholding(
  reason: HostAdmissionAccounting['withholdingReason'],
): HostAdmissionWithholdingSummary | null {
  if (reason == null) return null;
  return { reason, ...withholdingSummaries[reason] };
}

/** Describes current host-admission evidence without treating missing values as zero. */
export function describeHostAdmission(
  state: HostAdmissionState | null | undefined,
): HostAdmissionSummary {
  if (state == null) {
    return {
      status: 'unavailable',
      label: 'Unavailable',
      description: 'This manager does not report host-admission evidence.',
    };
  }
  switch (state.status) {
    case 'disabled':
      return {
        status: 'disabled',
        label: 'Not configured',
        description: 'This profile uses independent admission and does not reserve host units.',
      };
    case 'unavailable':
      return {
        status: 'unavailable',
        label: 'Unavailable',
        description:
          'The manager could not read the coordinator, so host budget and accounting are unavailable rather than zero.',
      };
    case 'degraded':
      return {
        status: 'degraded',
        label: 'Degraded',
        description:
          'The coordinator responded, but policy identity, profile accounting, or demand freshness is incomplete.',
      };
    case 'available':
      return {
        status: 'available',
        label: 'Available',
        description: 'The coordinator and profile policy agree and demand accounting is current.',
      };
  }
}

/** Aggregates only complete profile accounting for a node-level scan summary. */
export function summarizeNodeHostAdmission(
  profiles: ReadonlyArray<ManagerObservedState>,
): NodeHostAdmissionSummary {
  if (profiles.length === 0) {
    return {
      status: 'unavailable',
      configuredProfiles: 0,
      borrowedUnits: null,
      withheldUnits: null,
    };
  }
  const states = profiles.map((profile) => profile.hostAdmission);
  if (states.some((state) => state == null)) {
    return {
      status: 'unavailable',
      configuredProfiles: states.filter((state) => state != null && state.status !== 'disabled')
        .length,
      borrowedUnits: null,
      withheldUnits: null,
    };
  }
  const configured = states.filter(
    (state): state is HostAdmissionState => state != null && state.status !== 'disabled',
  );
  if (configured.length === 0) {
    return {
      status: states.every((state) => state?.status === 'disabled') ? 'disabled' : 'unavailable',
      configuredProfiles: 0,
      borrowedUnits: null,
      withheldUnits: null,
    };
  }

  const status = states.some((state) => state == null || state.status === 'unavailable')
    ? 'unavailable'
    : configured.some((state) => state.status === 'degraded')
      ? 'degraded'
      : 'available';
  const accountingComplete =
    !configured.some((state) => state.status === 'unavailable') &&
    configured.every(
      (state) =>
        state.accounting != null &&
        state.accounting.pendingUnits != null &&
        state.accounting.withheldUnits != null &&
        state.accounting.borrowedUnits >= 0,
    );

  return {
    status,
    configuredProfiles: configured.length,
    borrowedUnits: accountingComplete
      ? configured.reduce((total, state) => total + (state.accounting?.borrowedUnits ?? 0), 0)
      : null,
    withheldUnits: accountingComplete
      ? configured.reduce((total, state) => total + (state.accounting?.withheldUnits ?? 0), 0)
      : null,
  };
}
