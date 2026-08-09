import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { type HostAdmissionState, type ManagerObservedState } from '@/core/fleet';

import { ProfileHostAdmission } from './ProfileHostAdmission';

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
  accounting: {
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
  },
  lastDecision: {
    sequence: 42,
    command: 'acquire',
    granted: false,
    failureCategory: 'budget-exceeded',
    decidedAtUnixNano: 1_754_719_500_000_000_000,
  },
};

function profile(hostAdmission?: HostAdmissionState | null): ManagerObservedState {
  return {
    schemaVersion: 1,
    managerContractVersion: hostAdmission === undefined ? 17 : 18,
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
  it('renders available budget, protected reservation, borrowing, and withheld demand', () => {
    render(<ProfileHostAdmission profile={profile(availableAdmission)} />);

    const panel = screen.getByTestId('profile-host-admission-default');
    expect(within(panel).getByText('Protected reservation')).toBeInTheDocument();
    expect(screen.getByTestId('profile-host-admission-budget-default')).toHaveTextContent('10');
    expect(screen.getByTestId('profile-host-admission-available-default')).toHaveTextContent('4');
    expect(screen.getByTestId('profile-host-admission-borrowed-default')).toHaveTextContent('1');
    expect(screen.getByTestId('profile-host-admission-withheld-default')).toHaveTextContent('4');
    expect(screen.getByTestId('profile-host-admission-decision-default')).toHaveTextContent(
      'budget-exceeded',
    );
  });

  it('renders legacy absence as unavailable rather than zero', () => {
    render(<ProfileHostAdmission profile={profile()} />);

    const panel = screen.getByTestId('profile-host-admission-default');
    expect(panel).toHaveTextContent('unavailable');
    expect(panel).toHaveTextContent('does not report host-admission evidence');
    expect(within(panel).queryByText(/^0$/)).not.toBeInTheDocument();
  });
});
