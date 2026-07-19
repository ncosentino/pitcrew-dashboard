import { useCallback, useEffect, useRef, useState } from 'react';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { DisplayNameEditor } from '@/components/DisplayNameEditor';
import { ApiError } from '@/core/api/httpClient';
import { cn } from '@/lib/utils';

import {
  createEnrollmentCode,
  getFleet,
  renameNode,
  requestCredentialRotation,
  revokeNode,
  type EnrollmentCodeResponse,
  type FleetResponse,
  type ObservedSlot,
} from './fleetApi';

const refreshIntervalMilliseconds = 5_000;

/** Props for tenant-scoped fleet visibility and node administration. */
export interface FleetDashboardProps {
  readonly tenantId: string;
  readonly canAdminister: boolean;
  readonly antiforgeryToken: string;
}

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
    case 'rotation requested':
      return 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200';
    case 'backoff':
    case 'invalid':
    case 'conflict':
    case 'revoked':
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

/** Renders one tenant's live fleet plus authorized enrollment and node controls. */
export function FleetDashboard({ tenantId, canAdminister, antiforgeryToken }: FleetDashboardProps) {
  const [fleet, setFleet] = useState<FleetResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isMutating, setIsMutating] = useState(false);
  const [enrollmentLabel, setEnrollmentLabel] = useState('New server');
  const [enrollmentCode, setEnrollmentCode] = useState<EnrollmentCodeResponse | null>(null);
  const refreshSequence = useRef(0);

  const refresh = useCallback(
    async (signal: AbortSignal) => {
      const sequence = ++refreshSequence.current;
      try {
        const response = await getFleet(tenantId, signal);
        if (sequence !== refreshSequence.current) return;
        setFleet(response);
        setError(null);
      } catch (caught) {
        if (caught instanceof Error && caught.name === 'AbortError') return;
        if (sequence !== refreshSequence.current) return;
        setError(
          caught instanceof ApiError
            ? caught.message
            : caught instanceof Error
              ? caught.message
              : 'Fleet status could not be loaded.',
        );
      } finally {
        if (!signal.aborted && sequence === refreshSequence.current) {
          setIsLoading(false);
        }
      }
    },
    [tenantId],
  );

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

  const mutate = async (operation: () => Promise<void>) => {
    setIsMutating(true);
    try {
      await operation();
      await refresh(new AbortController().signal);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Fleet administration failed.');
    } finally {
      setIsMutating(false);
    }
  };

  const issueEnrollmentCode = async () => {
    setIsMutating(true);
    try {
      const response = await createEnrollmentCode(
        tenantId,
        enrollmentLabel.trim(),
        antiforgeryToken,
      );
      setEnrollmentCode(response);
      setError(null);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Enrollment code could not be created.');
    } finally {
      setIsMutating(false);
    }
  };

  const renameServer = async (nodeId: string, displayName: string) => {
    await renameNode(tenantId, nodeId, displayName, antiforgeryToken);
    refreshSequence.current++;
    setError(null);
    setFleet((current) =>
      current
        ? {
            ...current,
            nodes: current.nodes
              .map((node) => (node.nodeId === nodeId ? { ...node, displayName } : node))
              .sort((left, right) => left.displayName.localeCompare(right.displayName)),
          }
        : current,
    );
  };

  return (
    <>
      <section className="grid gap-2">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-2xl font-bold tracking-tight">Fleet status</h2>
            <p className="text-sm text-muted-foreground">
              Servers connect outbound and report credential-free manager observations.
            </p>
          </div>
          <div className="text-right text-sm text-muted-foreground">
            {fleet ? `Updated ${formatTime(fleet.generatedAt)}` : 'Waiting for status'}
          </div>
        </div>
      </section>

      {canAdminister ? (
        <Card>
          <CardHeader>
            <CardTitle>Enroll a connector</CardTitle>
            <CardDescription>
              Codes expire quickly and are consumed by exactly one enrollment or re-enrollment.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-3">
            <div className="flex flex-wrap gap-3">
              <input
                className="h-9 min-w-64 flex-1 rounded-md border bg-background px-3 text-sm"
                value={enrollmentLabel}
                onChange={(event) => setEnrollmentLabel(event.target.value)}
                maxLength={128}
              />
              <Button
                type="button"
                disabled={isMutating || enrollmentLabel.trim().length === 0}
                onClick={() => void issueEnrollmentCode()}
              >
                Create one-time code
              </Button>
            </div>
            {enrollmentCode ? (
              <div className="grid gap-2 rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
                <div className="text-sm font-semibold">Copy this code now</div>
                <code className="overflow-x-auto rounded bg-background p-3 text-xs">
                  {enrollmentCode.code}
                </code>
                <div className="text-xs">
                  Expires {formatTime(enrollmentCode.expiresAt)}. It is not stored in recoverable
                  form.
                </div>
              </div>
            ) : null}
          </CardContent>
        </Card>
      ) : null}

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
              Create a one-time code, configure it on a connector, and start the connector.
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
                <div className="flex flex-wrap items-center gap-2">
                  <StatusBadge
                    status={node.isRevoked ? 'revoked' : node.isOnline ? 'online' : 'offline'}
                  />
                  {node.credentialRotationRequested ? (
                    <StatusBadge status="rotation requested" />
                  ) : null}
                </div>
              </div>
              {canAdminister ? (
                <div className="grid gap-3 pt-3">
                  <DisplayNameEditor
                    value={node.displayName}
                    label="Server display name"
                    submitLabel="Rename server"
                    successMessage="Server name updated."
                    onSave={(displayName) => renameServer(node.nodeId, displayName)}
                  />
                  <div className="flex flex-wrap gap-2">
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      disabled={isMutating || node.isRevoked || node.credentialRotationRequested}
                      onClick={() =>
                        void mutate(() =>
                          requestCredentialRotation(tenantId, node.nodeId, antiforgeryToken),
                        )
                      }
                    >
                      Rotate credential
                    </Button>
                    <Button
                      type="button"
                      size="sm"
                      variant="destructive"
                      disabled={isMutating || node.isRevoked}
                      onClick={() => {
                        if (
                          globalThis.confirm(
                            `Revoke ${node.displayName}? The connector will stop synchronizing until it re-enrolls with a new one-time code.`,
                          )
                        ) {
                          void mutate(() => revokeNode(tenantId, node.nodeId, antiforgeryToken));
                        }
                      }}
                    >
                      Revoke
                    </Button>
                  </div>
                </div>
              ) : null}
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
                      <h3 className="font-semibold">{profile.profileId}</h3>
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
    </>
  );
}
