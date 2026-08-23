import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider } from '@/core/auth';

import SupportPage, {
  SupportIdentityCard,
  SupportIdentityInventory,
  SupportSessionCard,
} from './SupportPage';

const activeIdentity = {
  nodeId: '11111111-1111-4111-8111-111111111111',
  displayName: 'Active node',
  status: 'Active' as const,
  createdAt: '2026-08-01T00:00:00+00:00',
  revokedAt: null,
  lastPollAt: null,
  lastResultAt: null,
  capabilityVersion: 1,
};

const revokedIdentity = {
  nodeId: '33333333-3333-4333-8333-333333333333',
  displayName: 'Revoked node',
  status: 'Revoked' as const,
  createdAt: '2026-08-01T00:00:00+00:00',
  revokedAt: '2026-08-02T00:00:00+00:00',
  lastPollAt: null,
  lastResultAt: null,
  capabilityVersion: 1,
};

const ownerSession = {
  user: {
    githubUserId: '123',
    githubLogin: 'operator',
    displayName: 'Operator',
    avatarUrl: null,
  },
  isSystemAdministrator: false,
  tenants: [{ tenantId: 'local', displayName: 'Local', role: 'owner' as const }],
  antiforgeryToken: 'test-antiforgery-token',
};

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('SupportIdentityCard', () => {
  it('confirms an active identity revocation and explains its boundaries', async () => {
    const onRevoke = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(
      <SupportIdentityCard
        identity={{
          ...activeIdentity,
          displayName: 'Zephyr',
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

    expect(onRevoke).toHaveBeenCalledWith('11111111-1111-4111-8111-111111111111');
  });

  it('renders revoked lifecycle truth without offline poll wording', () => {
    render(<SupportIdentityCard identity={revokedIdentity} />);

    expect(screen.getByText(/revoked/i, { selector: 'span' })).toBeVisible();
    expect(screen.getByText(/^Revoked .*2026/)).toBeVisible();
    expect(screen.queryByText(/Last poll:/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Revoke' })).not.toBeInTheDocument();
  });
});

describe('SupportIdentityInventory', () => {
  it('keeps active nodes primary and revoked identities in collapsed history', () => {
    render(
      <SupportIdentityInventory
        identities={[activeIdentity, revokedIdentity]}
        onRevoke={vi.fn().mockResolvedValue(undefined)}
      />,
    );

    expect(screen.getByRole('heading', { name: 'Active node' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Revoke' })).toBeVisible();
    const summary = screen.getByText('Revoked history (1)');
    const history = summary.closest('details');
    if (!history) throw new Error('Expected revoked history details.');
    expect(history).not.toHaveAttribute('open');
    expect(within(history).getByRole('heading', { name: 'Revoked node' })).toBeInTheDocument();
    expect(within(history).getByText(/revoked/i, { selector: 'span' })).toBeInTheDocument();
  });

  it('shows an active empty state while retaining revoked history', () => {
    render(<SupportIdentityInventory identities={[revokedIdentity]} />);

    expect(screen.getByText(/No active support nodes/)).toBeVisible();
    expect(screen.getByText('Revoked history (1)')).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Revoke' })).not.toBeInTheDocument();
  });
});

describe('SupportPage', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('offers diagnostic sessions only to active identities', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) {
        return jsonResponse([activeIdentity, revokedIdentity]);
      }
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    render(
      <SessionProvider>
        <MemoryRouter initialEntries={['/tenants/local/support']}>
          <Routes>
            <Route path="/tenants/:tenantId/support" element={<SupportPage />} />
          </Routes>
        </MemoryRouter>
      </SessionProvider>,
    );

    const nodeSelector = await screen.findByRole('combobox', { name: 'Support node' });
    expect(within(nodeSelector).getByRole('option', { name: 'Active node' })).toBeInTheDocument();
    expect(within(nodeSelector).queryByRole('option', { name: 'Revoked node' })).toBeNull();
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
