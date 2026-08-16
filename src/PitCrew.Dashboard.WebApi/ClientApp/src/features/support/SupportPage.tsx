import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';
import { formatTime } from '@/core/formatting/formatters';
import { FormField } from '@/core/ui/FormField';
import { StatusBadge } from '@/core/ui/StatusBadge';

import {
  createSupportEnrollment,
  createSupportSession,
  getSupportIdentities,
  getSupportSessions,
  type CreatedSupportEnrollment,
  type SupportIdentity,
  type SupportSession,
} from './supportApi';

const diagnosticModes = [
  'ConnectorOffline',
  'CapacityMismatch',
  'JobNotAssigned',
  'HostPressure',
  'Full',
] as const;

/** Tenant support-plane diagnostics workflow. */
export default function SupportPage() {
  const { tenantId = '' } = useParams();
  const { session } = useSession();
  const [identities, setIdentities] = useState<readonly SupportIdentity[]>([]);
  const [sessions, setSessions] = useState<readonly SupportSession[]>([]);
  const [nodeId, setNodeId] = useState('');
  const [mode, setMode] = useState<(typeof diagnosticModes)[number]>('ConnectorOffline');
  const [profileId, setProfileId] = useState('');
  const [enrollment, setEnrollment] = useState<CreatedSupportEnrollment | null>(null);
  const [displayName, setDisplayName] = useState('Support node');
  const [signingKey, setSigningKey] = useState('');
  const [encryptionKey, setEncryptionKey] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      const [nextIdentities, nextSessions] = await Promise.all([
        getSupportIdentities(tenantId, signal),
        getSupportSessions(tenantId, signal),
      ]);
      setIdentities(nextIdentities);
      setSessions(nextSessions);
      if (!nodeId && nextIdentities[0]) setNodeId(nextIdentities[0].nodeId);
    },
    [nodeId, tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      void load(controller.signal).catch((caught: unknown) => {
        if (caught instanceof Error && caught.name === 'AbortError') return;
        setError(caught instanceof Error ? caught.message : 'Support status could not be loaded.');
      });
    }, 0);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [load]);

  const requestSession = async () => {
    if (!session) return;
    setBusy(true);
    try {
      await createSupportSession(
        tenantId,
        nodeId,
        mode,
        profileId.trim().length === 0 ? null : profileId.trim(),
        session.antiforgeryToken,
      );
      await load();
      setError(null);
    } catch (caught) {
      setError(
        caught instanceof Error
          ? caught.message
          : 'Support diagnostic session could not be created.',
      );
    } finally {
      setBusy(false);
    }
  };

  const enroll = async () => {
    if (!session) return;
    setBusy(true);
    try {
      setEnrollment(
        await createSupportEnrollment(
          tenantId,
          displayName.trim(),
          signingKey.trim(),
          encryptionKey.trim(),
          session.antiforgeryToken,
        ),
      );
      await load();
      setError(null);
    } catch (caught) {
      setError(
        caught instanceof Error ? caught.message : 'Support identity could not be enrolled.',
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="grid gap-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Support diagnostics</h1>
        <p className="text-sm text-muted-foreground">
          Request bounded read-only diagnostics through the independent support agent. This status
          is separate from normal connector and runner health.
        </p>
      </div>
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
      <Card>
        <CardHeader>
          <CardTitle as="h2">Support identities</CardTitle>
          <CardDescription>
            Active support nodes poll the opaque relay without sharing connector credentials.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3">
          <div className="grid gap-2 md:grid-cols-2">
            {identities.map((identity) => (
              <article key={identity.nodeId} className="min-w-0 rounded-lg border p-3">
                <div className="flex items-center justify-between gap-3">
                  <h3 className="font-medium">{identity.displayName}</h3>
                  <StatusBadge status={identity.status === 'Active' ? 'healthy' : 'critical'} />
                </div>
                <div className="mt-2 break-all font-mono text-xs text-muted-foreground">
                  {identity.nodeId}
                </div>
                <dl className="mt-2 grid gap-1 text-sm">
                  <div>
                    Last poll:{' '}
                    {identity.lastPollAt ? formatTime(identity.lastPollAt) : 'Unavailable'}
                  </div>
                  <div>Capability: v{identity.capabilityVersion}</div>
                </dl>
              </article>
            ))}
          </div>
          <fieldset className="grid gap-3 rounded-lg border p-3">
            <legend className="px-2 text-sm font-medium">Enroll generated support keys</legend>
            <FormField label="Display name">
              <input
                className="h-9 rounded-md border bg-background px-3 text-sm"
                value={displayName}
                onChange={(event) => setDisplayName(event.target.value)}
              />
            </FormField>
            <FormField label="Node signing public key SPKI">
              <textarea
                className="min-h-20 rounded-md border bg-background p-3 font-mono text-xs"
                value={signingKey}
                onChange={(event) => setSigningKey(event.target.value)}
              />
            </FormField>
            <FormField label="Node encryption public key SPKI">
              <textarea
                className="min-h-20 rounded-md border bg-background p-3 font-mono text-xs"
                value={encryptionKey}
                onChange={(event) => setEncryptionKey(event.target.value)}
              />
            </FormField>
            <Button
              type="button"
              disabled={busy || !signingKey || !encryptionKey}
              onClick={() => void enroll()}
            >
              Create support enrollment
            </Button>
          </fieldset>
          {enrollment ? (
            <div
              role="status"
              className="grid gap-2 rounded-lg border border-status-caution-foreground/30 bg-status-caution p-4 text-status-caution-foreground"
            >
              <strong>Copy support enrollment material now</strong>
              <code className="overflow-x-auto rounded bg-background p-3 text-xs text-foreground">
                {enrollment.enrollmentCode}
              </code>
              <code className="overflow-x-auto rounded bg-background p-3 text-xs text-foreground">
                {enrollment.transportCredential}
              </code>
              <span className="text-xs">Relay: {enrollment.relayUrl}</span>
            </div>
          ) : null}
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle as="h2">Request diagnostic session</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-3">
          <FormField label="Support node">
            <select
              className="h-9 rounded-md border bg-background px-3 text-sm"
              value={nodeId}
              onChange={(event) => setNodeId(event.target.value)}
            >
              {identities.map((identity) => (
                <option key={identity.nodeId} value={identity.nodeId}>
                  {identity.displayName}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label="Diagnostic mode">
            <select
              className="h-9 rounded-md border bg-background px-3 text-sm"
              value={mode}
              onChange={(event) => setMode(event.target.value as typeof mode)}
            >
              {diagnosticModes.map((candidate) => (
                <option key={candidate}>{candidate}</option>
              ))}
            </select>
          </FormField>
          <FormField label="Profile ID" hint="Optional locally configured profile.">
            <input
              className="h-9 rounded-md border bg-background px-3 text-sm"
              value={profileId}
              onChange={(event) => setProfileId(event.target.value)}
            />
          </FormField>
          <Button type="button" disabled={busy || !nodeId} onClick={() => void requestSession()}>
            Request read-only diagnostics
          </Button>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle as="h2">Recent sessions</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-3">
          {sessions.map((supportSession) => (
            <SupportSessionCard key={supportSession.sessionId} session={supportSession} />
          ))}
        </CardContent>
      </Card>
    </section>
  );
}

export function SupportSessionCard({ session }: { readonly session: SupportSession }) {
  return (
    <article className="grid gap-2 rounded-lg border p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-medium">{session.diagnosticMode}</h3>
        <StatusBadge
          status={
            session.status === 'Completed'
              ? 'healthy'
              : session.status === 'Rejected'
                ? 'critical'
                : 'warning'
          }
        />
      </div>
      <div className="break-all font-mono text-xs text-muted-foreground">{session.sessionId}</div>
      <div className="text-sm text-muted-foreground">
        Requested {formatTime(session.requestedAt)} · expires {formatTime(session.expiresAt)}
      </div>
      {session.result ? (
        <pre className="overflow-x-auto rounded-md bg-muted p-3 text-xs">
          {JSON.stringify(session.result.report, null, 2)}
        </pre>
      ) : (
        <p className="text-sm text-muted-foreground">
          Verified report unavailable until completion.
        </p>
      )}
      {session.result ? (
        <pre className="whitespace-pre-wrap rounded-md bg-muted p-3 text-sm">
          {session.result.markdown}
        </pre>
      ) : null}
      {session.result ? (
        <p className="break-all text-xs text-muted-foreground">
          Attestation {session.result.attestation.signatureAlgorithm}:{' '}
          {session.result.attestation.signatureBase64Url}
        </p>
      ) : null}
    </article>
  );
}
