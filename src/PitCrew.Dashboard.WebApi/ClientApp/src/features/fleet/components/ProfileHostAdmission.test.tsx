import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import {
  type HostAdmissionAccounting,
  type HostAdmissionState,
  type ManagerObservedState,
} from '@/core/fleet';

import { ProfileHostAdmission } from './ProfileHostAdmission';

const availableAccounting: HostAdmissionAccounting = {
  unitCost: 2,
  reservedUnits: 4,
  borrowable: false,
  profilePolicyFingerprint: 'profile-policy',
  activeUnits: 5,
  provisionalUnits: 0,
  heldUnits: 5,
  borrowedUnits: 1,
  pendingUnits: 4,
  withheldUnits: 4,
  allocatableUnits: 4,
  allocatableWorkers: 2,
  theoreticalMaximumUnits: 10,
  theoreticalMaximumWorkers: 5,
  withholdingReason: null,
};

const availableAdmission: HostAdmissionState = {
  status: 'available',
  namespace: 'primary',
  epoch: 3,
  decisionSequence: 42,
  capacityUnits: 12,
  safetyMarginUnits: 2,
  effectiveTotalUnits: 10,
  availableUnits: 4,
  hostPolicyFingerprint: 'host-policy',
  accounting: availableAccounting,
  lastDecision: {
    sequence: 42,
    command: 'acquire',
    granted: false,
    failureCategory: 'budget-exceeded',
    decidedAtUnixNano: 1_754_719_500_000_000_000,
  },
};

function profile(
  hostAdmission?: HostAdmissionState | null,
  managerContractVersion = hostAdmission === undefined ? 17 : 19,
): ManagerObservedState {
  return {
    schemaVersion: 1,
    managerContractVersion,
    profileId: 'default',
    managerInstanceId: 'manager-default',
    managerStatus: 'running',
    observedAt: '2026-08-09T06:30:00+00:00',
    scope: 'repo',
    generation: 1,
    desiredStateHash: null,
    desiredStateStatus: 'accepted',
    desiredSlots: 0,
    activeSlots: 0,
    eligibleSlots: 0,
    drainingSlots: 0,
    slots: [],
    resourceTelemetry: null,
    configuredSlots: 0,
    autoscaling: null,
    resourcePolicy: null,
    operationJournal: null,
    subsystemHealth: null,
    capacityEvidence: null,
    update: null,
    hostAdmission,
  };
}

describe('ProfileHostAdmission', () => {
  it('separates profile-usable capacity from host availability and policy ceilings', () => {
    render(<ProfileHostAdmission profile={profile(availableAdmission)} />);

    const panel = screen.getByTestId('profile-host-admission-default');
    expect(within(panel).getByText('Protected reservation')).toBeInTheDocument();
    expect(screen.getByTestId('profile-host-admission-available-default')).toHaveTextContent(
      '4 units',
    );
    expect(
      screen.getByTestId('profile-host-admission-allocatable-workers-default'),
    ).toHaveTextContent('2 workers');
    expect(
      screen.getByTestId('profile-host-admission-allocatable-units-default'),
    ).toHaveTextContent('4 units');
    expect(
      screen.getByTestId('profile-host-admission-theoretical-workers-default'),
    ).toHaveTextContent('5 workers');
    expect(
      screen.getByTestId('profile-host-admission-theoretical-units-default'),
    ).toHaveTextContent('10 units');
    expect(screen.getByTestId('profile-host-admission-borrowed-default')).toHaveTextContent('1');
    expect(screen.getByTestId('profile-host-admission-withheld-default')).toHaveTextContent('4');
    expect(screen.getByTestId('profile-host-admission-decision-default')).toHaveTextContent(
      'budget-exceeded',
    );
  });

  it.each([
    ['budget-exhausted', 'Host budget exhausted', 'cannot fit another worker'],
    ['protected-reservation', 'Protected reservation', 'not borrowable by this profile'],
    ['fair-share-contention', 'Fair-share contention', "another contender's opportunity"],
    ['adoption-pending', 'Worker adoption pending', 'fencing new admission'],
  ] as const)('explains %s while host availability remains positive', (reason, label, detail) => {
    render(
      <ProfileHostAdmission
        profile={profile({
          ...availableAdmission,
          accounting: {
            ...availableAccounting,
            allocatableUnits: 0,
            allocatableWorkers: 0,
            withholdingReason: reason,
          },
        })}
      />,
    );

    expect(screen.getByTestId('profile-host-admission-available-default')).toHaveTextContent(
      '4 units',
    );
    expect(
      screen.getByTestId('profile-host-admission-allocatable-workers-default'),
    ).toHaveTextContent('0 workers');
    const banner = screen.getByTestId('profile-host-admission-withholding-default');
    expect(banner).toHaveTextContent(label);
    expect(banner).toHaveTextContent(detail);
    expect(banner).toHaveTextContent('Host-wide availability can remain positive');
  });

  it('shows the configured active-worker cap without presenting it as available capacity', () => {
    render(
      <ProfileHostAdmission
        profile={{
          ...profile(availableAdmission),
          autoscaling: {
            mode: 'scale-set',
            status: 'running',
            minimumIdleSlots: 0,
            maximumSlots: 8,
            targetSlots: 2,
            assignedJobs: 1,
            runningJobs: 1,
            availableJobs: 0,
            idleRunners: 1,
            busyRunners: 1,
            scaleDownDelaySeconds: 30,
            scaleSetCount: 1,
            scaleDownAt: null,
            lastError: null,
            maximumActiveWorkers: 6,
            targets: null,
          },
        }}
      />,
    );

    expect(
      screen.getByTestId('profile-host-admission-configured-maximum-default'),
    ).toHaveTextContent('6 workers');
    expect(
      screen.getByText(/not guaranteed demand or physical host capacity/i),
    ).toBeInTheDocument();
  });

  it('renders contract 18 profile-scoped capacity as unavailable rather than zero', () => {
    render(
      <ProfileHostAdmission
        profile={profile(
          {
            ...availableAdmission,
            accounting: {
              ...availableAccounting,
              allocatableUnits: null,
              allocatableWorkers: null,
              theoreticalMaximumUnits: null,
              theoreticalMaximumWorkers: null,
              withholdingReason: null,
            },
          },
          18,
        )}
      />,
    );

    expect(
      screen.getByTestId('profile-host-admission-allocatable-workers-default'),
    ).toHaveTextContent('Unavailable');
    expect(
      screen.getByText(/Contract 18 and degraded compatibility evidence/i),
    ).toBeInTheDocument();
  });

  it('renders legacy absence as unavailable rather than zero', () => {
    render(<ProfileHostAdmission profile={profile()} />);

    const panel = screen.getByTestId('profile-host-admission-default');
    expect(panel).toHaveTextContent('unavailable');
    expect(panel).toHaveTextContent('does not report host-admission evidence');
    expect(within(panel).queryByText(/^0$/)).not.toBeInTheDocument();
  });
});
