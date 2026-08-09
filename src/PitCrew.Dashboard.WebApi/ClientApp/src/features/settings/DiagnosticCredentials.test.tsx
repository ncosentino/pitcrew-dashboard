import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { DiagnosticCredentials } from './DiagnosticCredentials';

const credential = {
  credentialId: '11111111-1111-4111-8111-111111111111',
  label: 'Performance diagnostics',
  createdByGitHubUserId: '1',
  createdAt: '2026-08-04T00:00:00.0000000+00:00',
  expiresAt: '2026-08-05T00:00:00.0000000+00:00',
  revokedAt: null,
  revokedByGitHubUserId: null,
  rotatedFromCredentialId: null,
  lastUsedAt: null,
  useCount: 0,
  nodeIds: ['22222222-2222-4222-8222-222222222222'],
  profileIds: ['default'],
};

describe('DiagnosticCredentials', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('creates a scoped credential and shows the raw value once', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    fetchMock
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValueOnce(
        jsonResponse({
          credential,
          value: 'pcd_raw-secret',
        }),
      )
      .mockResolvedValueOnce(jsonResponse([credential]));
    const user = userEvent.setup();
    render(<DiagnosticCredentials tenantId="local" antiforgeryToken="token" />);

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    await user.type(
      screen.getByLabelText('Allowed node IDs'),
      '22222222-2222-4222-8222-222222222222',
    );
    await user.type(screen.getByLabelText('Allowed profile IDs'), 'default');
    await user.click(screen.getByRole('button', { name: 'Create diagnostic credential' }));

    expect(await screen.findByText('pcd_raw-secret')).toBeInTheDocument();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));
    const [url, init] = fetchMock.mock.calls[1] ?? [];
    expect(String(url)).toContain('/api/tenants/local/diagnostic-credentials');
    expect(init?.method).toBe('POST');
    expect(new Headers(init?.headers).get('X-PitCrew-Antiforgery')).toBe('token');
    const body = JSON.parse(String(init?.body));
    expect(body.label).toBe('Performance diagnostics');
    expect(body.nodeIds).toEqual(['22222222-2222-4222-8222-222222222222']);
    expect(body.profileIds).toEqual(['default']);
    expect(Date.parse(body.expiresAt)).toBeGreaterThan(Date.now());
    expect(screen.getByText(credential.credentialId)).toBeInTheDocument();
  });

  it('surfaces creation errors without displaying a secret', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(jsonResponse([]))
      .mockResolvedValueOnce(
        jsonResponse(
          {
            error: {
              code: 'invalid_diagnostic_credential',
              message: 'Every node restriction must belong to the tenant.',
            },
          },
          400,
        ),
      );
    const user = userEvent.setup();
    render(<DiagnosticCredentials tenantId="local" antiforgeryToken="token" />);

    await screen.findByText('Issued diagnostic credentials');
    await user.click(screen.getByRole('button', { name: 'Create diagnostic credential' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Every node restriction must belong to the tenant.',
    );
    expect(screen.queryByText('Copy this credential now')).not.toBeInTheDocument();
  });

  it('requires explicit confirmation before rotating a credential', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    fetchMock
      .mockResolvedValueOnce(jsonResponse([credential]))
      .mockResolvedValueOnce(
        jsonResponse({
          credential,
          value: 'pcd_rotated-secret',
        }),
      )
      .mockResolvedValueOnce(jsonResponse([credential]));
    const user = userEvent.setup();
    render(<DiagnosticCredentials tenantId="local" antiforgeryToken="token" />);

    const trigger = await screen.findByRole('button', { name: 'Rotate' });
    trigger.focus();
    await user.keyboard('{Enter}');

    const dialog = screen.getByRole('alertdialog', {
      name: 'Rotate "Performance diagnostics"?',
    });
    expect(within(dialog).getByText(credential.credentialId)).toBeInTheDocument();
    expect(
      within(dialog).getByText('The previous value becomes invalid immediately.'),
    ).toBeVisible();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    await user.click(trigger);
    await user.click(
      within(
        screen.getByRole('alertdialog', {
          name: 'Rotate "Performance diagnostics"?',
        }),
      ).getByRole('button', { name: 'Rotate credential' }),
    );

    expect(await screen.findByText('pcd_rotated-secret')).toBeInTheDocument();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));
    const [url, init] = fetchMock.mock.calls[1] ?? [];
    expect(String(url)).toContain(
      `/api/tenants/local/diagnostic-credentials/${credential.credentialId}/rotate`,
    );
    expect(init?.method).toBe('POST');
  });

  it('requires consequence confirmation before revoking a credential', async () => {
    const revoked = {
      ...credential,
      revokedAt: '2026-08-08T00:00:00.0000000+00:00',
      revokedByGitHubUserId: '1',
    };
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    fetchMock
      .mockResolvedValueOnce(jsonResponse([credential]))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(jsonResponse([revoked]));
    const user = userEvent.setup();
    render(<DiagnosticCredentials tenantId="local" antiforgeryToken="token" />);

    const trigger = await screen.findByRole('button', { name: 'Revoke' });
    await user.click(trigger);

    const dialog = screen.getByRole('alertdialog', {
      name: 'Revoke "Performance diagnostics"?',
    });
    expect(within(dialog).getByText('This credential is permanently revoked.')).toBeVisible();
    expect(within(dialog).getByText('It cannot be rotated or reinstated.')).toBeVisible();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    await user.click(within(dialog).getByRole('button', { name: 'Revoke credential' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));
    const [url, init] = fetchMock.mock.calls[1] ?? [];
    expect(String(url)).toContain(
      `/api/tenants/local/diagnostic-credentials/${credential.credentialId}/revoke`,
    );
    expect(init?.method).toBe('POST');
    expect(await screen.findByText(/Revoked/)).toBeInTheDocument();
  });
});

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
