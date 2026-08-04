import { render, screen, waitFor } from '@testing-library/react';
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
});

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
