import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

import App from './App';

const session = {
  user: {
    githubUserId: '123',
    githubLogin: 'operator',
    displayName: 'Operator',
    avatarUrl: null,
  },
  isSystemAdministrator: false,
  tenants: [
    {
      tenantId: 'local',
      displayName: 'Local',
      role: 'owner',
    },
  ],
  antiforgeryToken: 'test-antiforgery-token',
};

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('App', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders GitHub sign-in when no dashboard session exists', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        {
          error: {
            code: 'unauthorized',
            message: 'Authentication required',
          },
        },
        401,
      ),
    );

    render(<App />);

    expect(await screen.findByText('Sign in to Pitcrew')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sign in with GitHub' })).toHaveAttribute(
      'href',
      '/auth/login?returnUrl=/',
    );
  });

  it('renders the authenticated empty fleet state', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(session);
      return jsonResponse({
        generatedAt: '2026-07-18T16:00:00+00:00',
        nodes: [],
      });
    });

    render(<App />);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Runner fleet' }),
    ).toBeInTheDocument();
    expect(await screen.findByText('No servers enrolled')).toBeInTheDocument();
    expect(screen.getByText('@operator')).toBeInTheDocument();
  });

  it('renders ASP.NET offset timestamps and fleet state', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/api/session')) return jsonResponse(session);
      return jsonResponse({
        generatedAt: '2026-07-18T16:00:00.1234567+00:00',
        nodes: [
          {
            nodeId: 'a6235ec4-2a15-4f91-a9e0-811152869a51',
            displayName: 'Build Server',
            connectorVersion: '2.0.0.0',
            enrolledAt: '2026-07-18T15:00:00.1234567+00:00',
            lastSeenAt: '2026-07-18T16:00:00.1234567+00:00',
            isOnline: true,
            isRevoked: false,
            credentialRotationRequested: false,
            profiles: [
              {
                schemaVersion: 1,
                managerContractVersion: 6,
                profileId: 'dashboard-demo',
                managerInstanceId: 'manager-instance',
                managerStatus: 'running',
                observedAt: '2026-07-18T15:59:59+00:00',
                scope: 'repo',
                generation: 1,
                desiredStateHash: 'a'.repeat(64),
                desiredStateStatus: 'accepted',
                desiredSlots: 2,
                activeSlots: 2,
                drainingSlots: 0,
                slots: [
                  {
                    key: 'repo-example-000001',
                    repository: 'https://github.com/example/project',
                    desired: true,
                    processRunning: true,
                    state: 'online',
                    failureCount: 0,
                    backoffSeconds: 0,
                    updatedAt: '2026-07-18T15:59:58+00:00',
                  },
                ],
              },
            ],
          },
        ],
      });
    });

    render(<App />);

    expect(await screen.findByText('Build Server')).toBeInTheDocument();
    expect(screen.getByText('dashboard-demo')).toBeInTheDocument();
    expect(screen.getByText('https://github.com/example/project')).toBeInTheDocument();
  });
});
