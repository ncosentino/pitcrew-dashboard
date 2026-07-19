import { afterEach, describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import App from './App';

describe('App', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders the empty fleet state', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          generatedAt: '2026-07-18T16:00:00+00:00',
          nodes: [],
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        },
      ),
    );

    render(<App />);

    const heading = screen.getByRole('heading', { level: 1 });
    expect(heading).toBeInTheDocument();
    expect(heading).toHaveTextContent('Runner fleet');
    expect(await screen.findByText('No servers enrolled')).toBeInTheDocument();
  });

  it('renders ASP.NET offset timestamps and fleet state', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          generatedAt: '2026-07-18T16:00:00.1234567+00:00',
          nodes: [
            {
              nodeId: 'a6235ec4-2a15-4f91-a9e0-811152869a51',
              displayName: 'Build Server',
              connectorVersion: '1.0.0.0',
              enrolledAt: '2026-07-18T15:00:00.1234567+00:00',
              lastSeenAt: '2026-07-18T16:00:00.1234567+00:00',
              isOnline: true,
              profiles: [
                {
                  schemaVersion: 1,
                  managerContractVersion: 5,
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
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        },
      ),
    );

    render(<App />);

    expect(await screen.findByText('Build Server')).toBeInTheDocument();
    expect(screen.getByText('dashboard-demo')).toBeInTheDocument();
    expect(screen.getByText('https://github.com/example/project')).toBeInTheDocument();
  });
});
