import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { SessionProvider } from '@/core/auth';

import SupportPage, {
  SupportIdentityCard,
  SupportIdentityInventory,
  SupportSessionCard,
} from './SupportPage';
import { type SupportSession } from './supportApi';

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

const completedResult: NonNullable<SupportSession['result']> = {
  report: { verified: ['connector'], unavailable: [], hypotheses: [] },
  markdown: 'Verified evidence',
  attestation: {
    nodeSigningPublicKeySpki: 'spki',
    payloadBase64Url: 'payload',
    signatureBase64Url: 'signature',
    signatureAlgorithm: 'ES256-P1363',
  },
};

function supportSession(
  status: SupportSession['status'],
  result: SupportSession['result'] = null,
  sessionId = '22222222-2222-4222-8222-222222222222',
): SupportSession {
  return {
    sessionId,
    nodeId: activeIdentity.nodeId,
    diagnosticMode: 'ConnectorOffline',
    profileId: null,
    capability: 'pitcrew.diagnostics.snapshot.v1',
    requestDigest: 'b'.repeat(64),
    nodeSigningKeyFingerprint: 'a'.repeat(64),
    status,
    requestedAt: '2026-08-01T00:00:00+00:00',
    expiresAt: '2026-08-01T00:05:00+00:00',
    dispatchedAt:
      status === 'Dispatched' || status === 'Completed' || status === 'Rejected'
        ? '2026-08-01T00:00:30+00:00'
        : null,
    rejectionDisposition: status === 'Rejected' ? 'broker-evidence-access-denied' : null,
    result,
  };
}

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

async function flushInitialSupportLoad() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });
}

function renderSupportPage(initialPath = '/tenants/local/support') {
  return render(
    <SessionProvider>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route path="/tenants/:tenantId/support/*" element={<SupportPage />} />
        </Routes>
      </MemoryRouter>
    </SessionProvider>,
  );
}

describe('SupportIdentityCard', () => {
  it('renders projected poll and result evidence for an active identity', () => {
    render(
      <SupportIdentityCard
        identity={{
          ...activeIdentity,
          lastPollAt: '2026-08-01T00:01:00+00:00',
          lastResultAt: '2026-08-01T00:02:00+00:00',
        }}
      />,
    );

    expect(screen.getByText(/Last poll/)).toBeVisible();
    expect(screen.getByText(/Last result/)).toBeVisible();
    expect(screen.queryByText('Unavailable')).not.toBeInTheDocument();
  });

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
    expect(screen.queryByText(/Last poll/)).not.toBeInTheDocument();
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
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('leads with readiness and task-oriented workspace navigation', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    renderSupportPage();

    expect(await screen.findByRole('region', { name: 'Support readiness' })).toBeVisible();
    expect(screen.getByRole('navigation', { name: 'Support tasks' })).toBeVisible();
    expect(screen.getByRole('link', { name: /Run diagnostic/ })).toHaveAttribute(
      'href',
      '/tenants/local/support/run',
    );
    expect(screen.getByRole('link', { name: /Sessions/ })).toHaveAttribute(
      'href',
      '/tenants/local/support/sessions',
    );
    expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument();
  });

  it('uses human diagnostic labels while preserving the wire value', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    renderSupportPage('/tenants/local/support/run');

    const mode = await screen.findByRole('combobox', { name: 'Problem to investigate' });
    expect(within(mode).getByRole('option', { name: 'Connector offline' })).toHaveValue(
      'ConnectorOffline',
    );
    expect(screen.getByText(/normal connector status is unavailable/i)).toBeVisible();
  });

  it('keeps enrollment collapsed until the operator requests it', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    const user = userEvent.setup();

    renderSupportPage('/tenants/local/support/nodes');

    await screen.findByRole('heading', { name: 'Support nodes' });
    expect(screen.queryByRole('group', { name: 'Create node enrollment' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Enroll support node' }));
    expect(screen.getByRole('group', { name: 'Create node enrollment' })).toBeVisible();
  });

  it('distinguishes unavailable support state from an empty enrollment state', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) {
        return jsonResponse(
          { error: { code: 'unavailable', message: 'Support unavailable' } },
          503,
        );
      }
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    renderSupportPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('Support unavailable');
    const readiness = screen.getByRole('region', { name: 'Support readiness' });
    expect(within(readiness).getByText('Status unavailable')).toBeVisible();
    expect(within(readiness).getAllByText('Unavailable')).toHaveLength(4);
    expect(within(readiness).queryByText('Enrollment required')).not.toBeInTheDocument();
  });

  it('reports a created session separately when the following refresh fails', async () => {
    let identityLoads = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) {
        identityLoads++;
        return identityLoads === 1
          ? jsonResponse([activeIdentity])
          : jsonResponse({ error: { code: 'unavailable', message: 'Refresh unavailable' } }, 503);
      }
      if (url.endsWith('/support/v1/sessions') && init?.method === 'POST') {
        return jsonResponse(supportSession('Queued'), 202);
      }
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    renderSupportPage('/tenants/local/support/run');

    await screen.findByRole('option', { name: 'Active node' });
    fireEvent.click(screen.getByRole('button', { name: 'Request read-only diagnostics' }));
    expect(
      await screen.findByText(
        'The diagnostic session was created, but support status could not refresh: Refresh unavailable',
      ),
    ).toBeVisible();
    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'Connector offline' })).toBeVisible();
    });
  });

  it('focuses and exposes a copy action for a newly created enrollment code', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([]);
      if (url.endsWith('/support/v1/enrollment-authorizations') && init?.method === 'POST') {
        return jsonResponse({
          displayName: 'Support node',
          enrollmentCode: 'enrollment-code-placeholder',
          enrollmentExpiresAt: '2026-08-01T00:15:00+00:00',
        });
      }
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    const user = userEvent.setup();

    renderSupportPage('/tenants/local/support/nodes');

    await user.click(await screen.findByRole('button', { name: 'Enroll support node' }));
    await user.click(screen.getByRole('button', { name: 'Create one-time code' }));
    const announcement = await screen.findByText('Copy this one-time code now');
    expect(announcement.closest('[tabindex="-1"]')).toHaveFocus();
    expect(screen.getByRole('button', { name: 'Copy one-time enrollment code' })).toBeVisible();
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
    renderSupportPage('/tenants/local/support/run');

    const nodeSelector = await screen.findByRole('combobox', { name: 'Support node' });
    await waitFor(() => {
      expect(within(nodeSelector).getByRole('option', { name: 'Active node' })).toBeInTheDocument();
    });
    expect(within(nodeSelector).queryByRole('option', { name: 'Revoked node' })).toBeNull();
  });

  it('automatically refreshes an active session without hiding unchanged state', async () => {
    vi.useFakeTimers();
    const queued = supportSession('Queued');
    let detailRequests = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith(`/support/v1/sessions/${queued.sessionId}`)) {
        detailRequests++;
        return jsonResponse(queued);
      }
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([queued]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderSupportPage('/tenants/local/support/sessions');
    await flushInitialSupportLoad();

    expect(screen.getByRole('status')).toHaveTextContent(
      'Waiting for a terminal result. This session updates automatically.',
    );
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(detailRequests).toBe(1);
    expect(screen.getAllByText('Queued', { selector: 'span' })).toHaveLength(3);
    expect(screen.queryByRole('button', { name: 'Check result' })).not.toBeInTheDocument();
  });

  it('automatically renders a completed lifecycle and stops polling it', async () => {
    vi.useFakeTimers();
    const queued = supportSession('Queued');
    const completed = supportSession('Completed', completedResult);
    let detailRequests = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith(`/support/v1/sessions/${queued.sessionId}`)) {
        detailRequests++;
        return jsonResponse(completed);
      }
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([queued]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderSupportPage('/tenants/local/support/sessions');
    await flushInitialSupportLoad();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(screen.getAllByText('Completed', { selector: 'span' })).toHaveLength(3);
    expect(screen.getByText('Verified evidence')).toBeVisible();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
    expect(detailRequests).toBe(1);
  });

  it('retries automatic refresh after a transient API failure', async () => {
    vi.useFakeTimers();
    const queued = supportSession('Queued');
    const completed = supportSession('Completed', completedResult);
    let detailRequests = 0;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith(`/support/v1/sessions/${queued.sessionId}`)) {
        detailRequests++;
        return detailRequests === 1
          ? jsonResponse({ error: { code: 'temporary', message: 'Temporary outage' } }, 503)
          : jsonResponse(completed);
      }
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([queued]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderSupportPage('/tenants/local/support/sessions');
    await flushInitialSupportLoad();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Automatic session refresh failed: Temporary outage',
    );
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(screen.getAllByText('Completed', { selector: 'span' })).toHaveLength(3);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('refreshes multiple active sessions in one bounded interval', async () => {
    vi.useFakeTimers();
    const first = supportSession('Queued', null, '22222222-2222-4222-8222-222222222222');
    const second = supportSession('Dispatched', null, '44444444-4444-4444-8444-444444444444');
    const detailRequests: string[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith(`/support/v1/sessions/${first.sessionId}`)) {
        detailRequests.push(first.sessionId);
        return jsonResponse({ ...first, status: 'Expired' });
      }
      if (url.endsWith(`/support/v1/sessions/${second.sessionId}`)) {
        detailRequests.push(second.sessionId);
        return jsonResponse({
          ...second,
          status: 'Rejected',
          rejectionDisposition: 'broker-timeout',
        });
      }
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([first, second]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderSupportPage('/tenants/local/support/sessions');
    await flushInitialSupportLoad();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(detailRequests).toEqual([first.sessionId, second.sessionId]);
    expect(screen.getAllByText('Expired', { selector: 'span' })).toHaveLength(3);
    expect(screen.getAllByText('Rejected', { selector: 'span' })).toHaveLength(2);
  });

  it('aborts an active session refresh when the page unmounts', async () => {
    vi.useFakeTimers();
    const queued = supportSession('Queued');
    const detailSignals: AbortSignal[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return Promise.resolve(jsonResponse(ownerSession));
      if (url.endsWith('/support/v1/identities')) {
        return Promise.resolve(jsonResponse([activeIdentity]));
      }
      if (url.endsWith(`/support/v1/sessions/${queued.sessionId}`)) {
        detailSignals.push(init?.signal as AbortSignal);
        return new Promise<Response>(() => undefined);
      }
      if (url.endsWith('/support/v1/sessions')) return Promise.resolve(jsonResponse([queued]));
      return Promise.resolve(
        jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404),
      );
    });
    const { unmount } = renderSupportPage('/tenants/local/support/sessions');
    await flushInitialSupportLoad();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });

    unmount();
    expect(detailSignals).toHaveLength(1);
    expect(detailSignals[0].aborted).toBe(true);
  });

  it('aborts a superseded automatic refresh before starting another', async () => {
    vi.useFakeTimers();
    const queued = supportSession('Queued');
    const detailSignals: AbortSignal[] = [];
    vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return Promise.resolve(jsonResponse(ownerSession));
      if (url.endsWith('/support/v1/identities')) {
        return Promise.resolve(jsonResponse([activeIdentity]));
      }
      if (url.endsWith(`/support/v1/sessions/${queued.sessionId}`)) {
        detailSignals.push(init?.signal as AbortSignal);
        return new Promise<Response>(() => undefined);
      }
      if (url.endsWith('/support/v1/sessions')) return Promise.resolve(jsonResponse([queued]));
      return Promise.resolve(
        jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404),
      );
    });
    renderSupportPage('/tenants/local/support/sessions');
    await flushInitialSupportLoad();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });

    expect(detailSignals).toHaveLength(2);
    expect(detailSignals[0].aborted).toBe(true);
    expect(detailSignals[1].aborted).toBe(false);
  });

  it('requests the server-supported 15-minute session window', async () => {
    let requestBody: Record<string, unknown> | null = null;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith('/support/v1/sessions') && init?.method === 'POST') {
        requestBody = JSON.parse(String(init.body)) as Record<string, unknown>;
        return jsonResponse(supportSession('Queued'), 202);
      }
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });
    renderSupportPage('/tenants/local/support/run');

    await screen.findByRole('combobox', { name: 'Support node' });
    fireEvent.click(await screen.findByRole('button', { name: 'Request read-only diagnostics' }));
    await waitFor(() => {
      expect(requestBody?.expiresInSeconds).toBe(900);
    });
  });

  it('orders active sessions before history and restores a deep-linked detail', async () => {
    const completed = supportSession(
      'Completed',
      completedResult,
      '55555555-5555-4555-8555-555555555555',
    );
    const queued = supportSession('Queued');
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([completed, queued]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    renderSupportPage(`/tenants/local/support/sessions/${completed.sessionId}`);

    const sessionList = (await screen.findAllByRole('list', { name: 'Support sessions' }))[0];
    const rows = within(sessionList).getAllByRole('listitem');
    expect(within(rows[0]).getByText('Queued', { selector: 'span' })).toBeVisible();
    expect(within(rows[1]).getByText('Completed', { selector: 'span' })).toBeVisible();
    expect(within(sessionList).getByRole('link', { name: 'Selected' })).toHaveAttribute(
      'href',
      `/tenants/local/support/sessions/${completed.sessionId}`,
    );
    expect(screen.getByRole('region', { name: 'Connector offline' })).toHaveTextContent(
      'Verified evidence',
    );
  });

  it('does not reorder a session row when automatic refresh makes it terminal', async () => {
    vi.useFakeTimers();
    const queued = supportSession('Queued');
    const rejected = supportSession('Rejected', null, '66666666-6666-4666-8666-666666666666');
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith(`/support/v1/sessions/${queued.sessionId}`)) {
        return jsonResponse({ ...queued, status: 'Completed', result: completedResult });
      }
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([rejected, queued]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    renderSupportPage('/tenants/local/support/sessions');
    await flushInitialSupportLoad();

    const sessionList = screen.getAllByRole('list', { name: 'Support sessions' })[0];
    expect(
      within(within(sessionList).getAllByRole('listitem')[0]).getByText('Queued'),
    ).toBeVisible();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    expect(
      within(within(sessionList).getAllByRole('listitem')[0]).getByText('Completed'),
    ).toBeVisible();
  });

  it('shows an explicit unknown-session state instead of selecting another session', async () => {
    const completed = supportSession('Completed', completedResult);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith('/support/v1/sessions')) return jsonResponse([completed]);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    renderSupportPage('/tenants/local/support/sessions/99999999-9999-4999-8999-999999999999');

    expect(await screen.findByRole('heading', { name: 'Session not found' })).toBeVisible();
    expect(screen.queryByText('Verified evidence')).not.toBeInTheDocument();
  });

  it('discloses the automatic refresh ceiling when more than 16 sessions are active', async () => {
    const activeSessions = Array.from({ length: 17 }, (_, index) =>
      supportSession(
        'Queued',
        null,
        `30000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
      ),
    );
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = input instanceof Request ? input.url : String(input);
      if (url.endsWith('/api/session')) return jsonResponse(ownerSession);
      if (url.endsWith('/support/v1/identities')) return jsonResponse([activeIdentity]);
      if (url.endsWith('/support/v1/sessions')) return jsonResponse(activeSessions);
      return jsonResponse({ error: { code: 'not_found', message: 'Not found' } }, 404);
    });

    renderSupportPage('/tenants/local/support/sessions');

    expect(
      await screen.findByText(/Only the 16 highest-priority active sessions update automatically/),
    ).toBeVisible();
  });
});

describe('SupportSessionCard', () => {
  it('renders verified support output without interpreting diagnostic markdown as HTML', () => {
    render(
      <SupportSessionCard
        session={supportSession('Completed', {
          ...completedResult,
          markdown: '<script>alert(1)</script> verified evidence',
        })}
      />,
    );

    expect(screen.getByText('Connector offline')).toBeInTheDocument();
    expect(screen.getByText('<script>alert(1)</script> verified evidence')).toBeInTheDocument();
    expect(screen.getByText('ES256-P1363')).toBeInTheDocument();
    expect(document.querySelector('script')).toBeNull();
  });

  it.each(['Queued', 'Dispatched', 'Completed', 'Rejected', 'Cancelled', 'Expired'] as const)(
    'renders the explicit %s lifecycle',
    (status) => {
      render(
        <SupportSessionCard
          session={supportSession(status, status === 'Completed' ? completedResult : null)}
        />,
      );

      expect(screen.getByText(status, { selector: 'span' })).toBeVisible();
    },
  );

  it.each(['Queued', 'Dispatched', 'Completed', 'Rejected', 'Cancelled', 'Expired'] as const)(
    'does not require a manual result check for %s sessions',
    (status) => {
      render(
        <SupportSessionCard
          session={supportSession(status, status === 'Completed' ? completedResult : null)}
        />,
      );

      expect(screen.queryByRole('button', { name: 'Check result' })).not.toBeInTheDocument();
    },
  );

  it('renders bounded dispatch and rejection evidence', () => {
    render(<SupportSessionCard session={supportSession('Rejected')} />);

    expect(screen.getByText(/First dispatched/)).toBeVisible();
    expect(screen.getByText('broker-evidence-access-denied', { selector: 'code' })).toBeVisible();
    expect(screen.getByText('The broker cannot read the approved evidence set.')).toBeVisible();
  });

  it('announces automatic updates for active sessions', () => {
    render(<SupportSessionCard session={supportSession('Dispatched')} />);

    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite');
    expect(screen.getByRole('status')).toHaveTextContent(
      'Waiting for a terminal result. This session updates automatically.',
    );
  });
});
