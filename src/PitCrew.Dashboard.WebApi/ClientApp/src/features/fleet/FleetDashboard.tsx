import { useCallback, useEffect, useState } from 'react';

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { ApiError } from '@/core/api/httpClient';
import { cn } from '@/lib/utils';

import { getFleet, type FleetResponse, type ObservedSlot } from './fleetApi';

const refreshIntervalMilliseconds = 5_000;

function formatTime(value: string | null): string {
  if (value === null) return 'Never';
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(new Date(value));
}

function statusClasses(status: string): string {
  switch (status) {
    case 'online':
    case 'running':
    case 'accepted':
      return 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200';
    case 'draining':
    case 'restarting':
      return 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200';
    case 'backoff':
    case 'invalid':
    case 'conflict':
      return 'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200';
    default:
      return 'bg-muted text-muted-foreground';
  }
}

function StatusBadge({ status }: { readonly status: string }) {
  return (
    <span
      className={cn(
        'inline-flex rounded-full px-2 py-1 text-xs font-semibold capitalize',
        statusClasses(status),
      )}
    >
      {status}
    </span>
  );
}

function SlotRow({ slot }: { readonly slot: ObservedSlot }) {
  return (
    <tr className="border-t">
      <td className="px-3 py-2 font-mono text-xs">{slot.key}</td>
      <td className="px-3 py-2">{slot.repository ?? 'Shared scope'}</td>
      <td className="px-3 py-2">
        <StatusBadge status={slot.state} />
      </td>
      <td className="px-3 py-2 text-right tabular-nums">{slot.failureCount}</td>
    </tr>
  );
}

export function FleetDashboard() {
  const [fleet, setFleet] = useState<FleetResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const refresh = useCallback(async (signal: AbortSignal) => {
    try {
      const response = await getFleet(signal);
      setFleet(response);
      setError(null);
    } catch (caught) {
      if (caught instanceof Error && caught.name === 'AbortError') return;
      setError(
        caught instanceof ApiError
          ? caught.message
          : caught instanceof Error
            ? caught.message
            : 'Fleet status could not be loaded.',
      );
    } finally {
      if (!signal.aborted) setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    let controller = new AbortController();
    const load = () => {
      controller.abort();
      controller = new AbortController();
      void refresh(controller.signal);
    };
    const initialTimer = window.setTimeout(load, 0);
    const refreshTimer = window.setInterval(load, refreshIntervalMilliseconds);

    return () => {
      controller.abort();
      window.clearTimeout(initialTimer);
      window.clearInterval(refreshTimer);
    };
  }, [refresh]);

  return (
    <main className="mx-auto flex min-h-screen max-w-7xl flex-col gap-6 px-4 py-8 sm:px-8">
      <header className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-sm font-semibold tracking-[0.2em] text-muted-foreground uppercase">
              Pitcrew
            </p>
            <h1 className="text-3xl font-bold tracking-tight">Runner fleet</h1>
          </div>
          <div className="text-right text-sm text-muted-foreground">
            <div>Read-only dashboard</div>
            <div>{fleet ? `Updated ${formatTime(fleet.generatedAt)}` : 'Waiting for status'}</div>
          </div>
        </div>
        <p className="max-w-3xl text-muted-foreground">
          Servers connect outbound and report credential-free manager observations. This dashboard
          never receives a Docker socket or GitHub runner-registration token.
        </p>
      </header>

      {error ? (
        <div className="rounded-lg border border-red-300 bg-red-50 p-4 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100">
          {error}
        </div>
      ) : null}

      {isLoading ? <p className="text-muted-foreground">Loading fleet status…</p> : null}

      {!isLoading && fleet?.nodes.length === 0 ? (
        <Card>
          <CardHeader>
            <CardTitle>No servers enrolled</CardTitle>
            <CardDescription>
              Start a connector with this dashboard URL and a valid enrollment token.
            </CardDescription>
          </CardHeader>
        </Card>
      ) : null}

      <section className="grid gap-6">
        {fleet?.nodes.map((node) => (
          <Card key={node.nodeId}>
            <CardHeader>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <CardTitle className="text-xl">{node.displayName}</CardTitle>
                  <CardDescription>
                    Connector {node.connectorVersion || 'unknown'} · Last seen{' '}
                    {formatTime(node.lastSeenAt)}
                  </CardDescription>
                </div>
                <StatusBadge status={node.isOnline ? 'online' : 'offline'} />
              </div>
            </CardHeader>
            <CardContent className="grid gap-4">
              {node.profiles.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  The connector has not reported any profile observations.
                </p>
              ) : null}
              {node.profiles.map((profile) => (
                <section key={profile.profileId} className="overflow-hidden rounded-lg border">
                  <div className="flex flex-wrap items-center justify-between gap-3 bg-muted/50 px-4 py-3">
                    <div>
                      <h2 className="font-semibold">{profile.profileId}</h2>
                      <p className="text-sm text-muted-foreground">
                        {profile.scope} scope · generation {profile.generation} · manager contract{' '}
                        {profile.managerContractVersion}
                      </p>
                    </div>
                    <div className="flex items-center gap-2">
                      <StatusBadge status={profile.managerStatus} />
                      <StatusBadge status={profile.desiredStateStatus} />
                    </div>
                  </div>
                  <dl className="grid grid-cols-3 gap-px border-y bg-border text-center">
                    <div className="bg-background px-3 py-3">
                      <dt className="text-xs text-muted-foreground uppercase">Desired</dt>
                      <dd className="text-2xl font-semibold tabular-nums">
                        {profile.desiredSlots}
                      </dd>
                    </div>
                    <div className="bg-background px-3 py-3">
                      <dt className="text-xs text-muted-foreground uppercase">Active</dt>
                      <dd className="text-2xl font-semibold tabular-nums">{profile.activeSlots}</dd>
                    </div>
                    <div className="bg-background px-3 py-3">
                      <dt className="text-xs text-muted-foreground uppercase">Draining</dt>
                      <dd className="text-2xl font-semibold tabular-nums">
                        {profile.drainingSlots}
                      </dd>
                    </div>
                  </dl>
                  <div className="overflow-x-auto">
                    <table className="w-full min-w-2xl text-left text-sm">
                      <thead className="bg-muted/30 text-xs text-muted-foreground uppercase">
                        <tr>
                          <th className="px-3 py-2 font-medium">Slot</th>
                          <th className="px-3 py-2 font-medium">Target</th>
                          <th className="px-3 py-2 font-medium">State</th>
                          <th className="px-3 py-2 text-right font-medium">Failures</th>
                        </tr>
                      </thead>
                      <tbody>
                        {profile.slots.map((slot) => (
                          <SlotRow key={slot.key} slot={slot} />
                        ))}
                      </tbody>
                    </table>
                  </div>
                </section>
              ))}
            </CardContent>
          </Card>
        ))}
      </section>
    </main>
  );
}
