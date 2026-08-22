import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { SupportIdentityCard, SupportSessionCard } from './SupportPage';

describe('SupportIdentityCard', () => {
  it('confirms an active identity revocation and explains its boundaries', async () => {
    const onRevoke = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(
      <SupportIdentityCard
        identity={{
          nodeId: '11111111-1111-1111-1111-111111111111',
          displayName: 'Zephyr',
          status: 'Active',
          createdAt: '2026-08-01T00:00:00+00:00',
          revokedAt: null,
          lastPollAt: null,
          lastResultAt: null,
          capabilityVersion: 1,
        }}
        onRevoke={onRevoke}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Revoke' }));

    const dialog = screen.getByRole('alertdialog', {
      name: 'Revoke "Zephyr"?',
    });
    expect(
      within(dialog).getByText('The normal connector identity and runner pools are unchanged.'),
    ).toBeVisible();
    expect(
      within(dialog).getByText('Local support keys are not removed by this Dashboard action.'),
    ).toBeVisible();

    await user.click(within(dialog).getByRole('button', { name: 'Revoke support identity' }));

    expect(onRevoke).toHaveBeenCalledWith('11111111-1111-1111-1111-111111111111');
  });
});

describe('SupportSessionCard', () => {
  it('renders verified support output without interpreting diagnostic markdown as HTML', () => {
    render(
      <SupportSessionCard
        session={{
          sessionId: '22222222-2222-2222-2222-222222222222',
          nodeId: '11111111-1111-1111-1111-111111111111',
          diagnosticMode: 'ConnectorOffline',
          profileId: null,
          capability: 'pitcrew.diagnostics.snapshot.v1',
          requestDigest: 'b'.repeat(64),
          nodeSigningKeyFingerprint: 'a'.repeat(64),
          status: 'Completed',
          requestedAt: '2026-08-01T00:00:00+00:00',
          expiresAt: '2026-08-01T00:05:00+00:00',
          result: {
            report: { verified: ['connector'], unavailable: [], hypotheses: [] },
            markdown: '<script>alert(1)</script> verified evidence',
            attestation: {
              nodeSigningPublicKeySpki: 'spki',
              payloadBase64Url: 'payload',
              signatureBase64Url: 'signature',
              signatureAlgorithm: 'ES256-P1363',
            },
          },
        }}
      />,
    );

    expect(screen.getByText('ConnectorOffline')).toBeInTheDocument();
    expect(screen.getByText('<script>alert(1)</script> verified evidence')).toBeInTheDocument();
    expect(screen.getByText(/Attestation ES256-P1363/i)).toBeInTheDocument();
    expect(document.querySelector('script')).toBeNull();
  });

  it('lets an operator fetch a pending session through the result-ingesting endpoint', () => {
    const checkResult = vi.fn().mockResolvedValue(undefined);
    render(
      <SupportSessionCard
        onCheckResult={checkResult}
        session={{
          sessionId: '22222222-2222-2222-2222-222222222222',
          nodeId: '11111111-1111-1111-1111-111111111111',
          diagnosticMode: 'ConnectorOffline',
          profileId: null,
          capability: 'pitcrew.diagnostics.snapshot.v1',
          requestDigest: 'b'.repeat(64),
          nodeSigningKeyFingerprint: 'a'.repeat(64),
          status: 'Queued',
          requestedAt: '2026-08-01T00:00:00+00:00',
          expiresAt: '2026-08-01T00:05:00+00:00',
          result: null,
        }}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Check result' }));

    expect(checkResult).toHaveBeenCalledWith('22222222-2222-2222-2222-222222222222');
  });
});
