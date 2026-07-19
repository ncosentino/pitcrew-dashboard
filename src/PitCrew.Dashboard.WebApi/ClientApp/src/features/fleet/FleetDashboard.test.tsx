import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { FleetDashboard } from './FleetDashboard';

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('FleetDashboard', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renames a revoked server and updates the fleet card immediately', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (_input, init) => {
      if (init?.method === 'PUT') {
        return new Response(null, { status: 204 });
      }
      return jsonResponse({
        generatedAt: '2026-07-18T16:00:00+00:00',
        nodes: [
          {
            nodeId: 'a6235ec4-2a15-4f91-a9e0-811152869a51',
            displayName: 'Original server',
            connectorVersion: '2.0.0',
            enrolledAt: '2026-07-18T15:00:00+00:00',
            lastSeenAt: '2026-07-18T15:30:00+00:00',
            isOnline: false,
            isRevoked: true,
            credentialRotationRequested: false,
            profiles: [],
          },
        ],
      });
    });
    const user = userEvent.setup();

    render(<FleetDashboard tenantId="local" canAdminister antiforgeryToken="token" />);

    const input = await screen.findByLabelText('Server display name');
    await user.clear(input);
    await user.type(input, '  Renamed server  ');
    await user.click(screen.getByRole('button', { name: 'Rename server' }));

    await waitFor(() => expect(screen.getByText('Renamed server')).toBeInTheDocument());
    const renameCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(renameCall).toBeDefined();
    const [url, init] = renameCall ?? [];
    expect(String(url)).toMatch(
      /\/api\/tenants\/local\/fleet\/v1\/nodes\/a6235ec4-2a15-4f91-a9e0-811152869a51$/,
    );
    expect(new Headers(init?.headers).get('X-PitCrew-Antiforgery')).toBe('token');
    expect(JSON.parse(String(init?.body))).toEqual({
      displayName: 'Renamed server',
    });
    expect(screen.getByRole('status')).toHaveTextContent('Server name updated.');
  });
});
