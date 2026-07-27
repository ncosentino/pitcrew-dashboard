import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { type FleetNode, type ManagerObservedState, type RecoveryControlState } from '@/core/fleet';

import { ProfileManagerRecovery } from './ProfileManagerRecovery';

const generatedAt = '2026-07-19T18:30:05+00:00';

const node: FleetNode = {
  nodeId: 'a6235ec4-2a15-4f91-a9e0-811152869a51',
  displayName: 'Resource server',
  connectorVersion: '2.0.0',
  enrolledAt: '2026-07-18T15:00:00+00:00',
  lastSeenAt: '2026-07-19T18:30:05+00:00',
  isOnline: true,
  isRevoked: false,
  credentialRotationRequested: false,
  profiles: [],
  capacityControls: [],
  recoveryControls: [],
};

const profile: ManagerObservedState = {
  schemaVersion: 1,
  managerContractVersion: 10,
  profileId: 'default',
  managerInstanceId: 'manager-default',
  managerStatus: 'running',
  observedAt: '2026-07-19T18:30:00+00:00',
  scope: 'repo',
  generation: 4,
  desiredStateHash: 'a'.repeat(64),
  desiredStateStatus: 'accepted',
  desiredSlots: 1,
  activeSlots: 1,
  eligibleSlots: 1,
  drainingSlots: 0,
  slots: [],
  resourcePolicy: null,
};

const control: RecoveryControlState = {
  profileId: 'default',
  managerContractVersion: 10,
  managerContractSupported: true,
  expectedManagerInstanceId: 'manager-default',
  desiredGeneration: 4,
  desiredStateHash: 'a'.repeat(64),
  observedStateAgeSeconds: 5,
  observedStateMaximumAgeSeconds: 120,
  recoveryAllowed: true,
  singleManagerResolved: true,
  operationActive: false,
  latestCommand: null,
  recentCommands: [],
};

function renderRecovery(
  currentControl: RecoveryControlState,
  onRecover: (fences: unknown) => Promise<void>,
) {
  return render(
    <ProfileManagerRecovery
      tenantId="local"
      node={node}
      profile={profile}
      control={currentControl}
      capacityCommand={null}
      canAdminister
      generatedAt={generatedAt}
      isMutating={false}
      onRecover={onRecover}
    />,
  );
}

describe('ProfileManagerRecovery', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('invalidates an open confirmation when the expected fences change', async () => {
    const recover = vi.fn<(fences: unknown) => Promise<void>>(async () => undefined);
    const user = userEvent.setup();
    const { rerender } = renderRecovery(control, recover);

    await user.click(screen.getByTestId('profile-recovery-action-default'));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('checkbox'));

    rerender(
      <ProfileManagerRecovery
        tenantId="local"
        node={node}
        profile={profile}
        control={{ ...control, expectedManagerInstanceId: 'manager-default-2' }}
        capacityCommand={null}
        canAdminister
        generatedAt={generatedAt}
        isMutating={false}
        onRecover={recover}
      />,
    );

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(
      'evidence changed while the confirmation was open',
    );
    expect(recover).not.toHaveBeenCalled();
  });

  it('requires a fresh acknowledgement after a cancelled confirmation', async () => {
    const recover = vi.fn<(fences: unknown) => Promise<void>>(async () => undefined);
    const user = userEvent.setup();
    renderRecovery(control, recover);

    await user.click(screen.getByTestId('profile-recovery-action-default'));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('checkbox'));
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await user.click(screen.getByTestId('profile-recovery-action-default'));
    const reopened = await screen.findByRole('alertdialog');
    expect(within(reopened).getByRole('checkbox')).not.toBeChecked();
    expect(within(reopened).getByRole('button', { name: 'Queue manager recovery' })).toBeDisabled();
    expect(recover).not.toHaveBeenCalled();
  });

  it('queues recovery with the connector-advertised fences', async () => {
    const recover = vi.fn<(fences: unknown) => Promise<void>>(async () => undefined);
    const user = userEvent.setup();
    renderRecovery(control, recover);

    await user.click(screen.getByTestId('profile-recovery-action-default'));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('checkbox'));
    await user.click(within(dialog).getByRole('button', { name: 'Queue manager recovery' }));

    expect(recover).toHaveBeenCalledTimes(1);
    expect(recover).toHaveBeenCalledWith({
      expectedManagerInstanceId: 'manager-default',
      expectedGeneration: 4,
      expectedDesiredStateHash: 'a'.repeat(64),
    });
  });

  it('returns focus to the recovery action after the confirmation closes', async () => {
    const recover = vi.fn<(fences: unknown) => Promise<void>>(async () => undefined);
    const user = userEvent.setup();
    renderRecovery(control, recover);

    const action = screen.getByTestId('profile-recovery-action-default');
    await user.click(action);
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(action).toHaveFocus();
  });
});
