import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import type { FleetNode } from '@/core/fleet';
import { formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

interface ConnectorHealthSummaryProps {
  readonly node: FleetNode;
}

/** Renders retained connector-owned outage evidence without implying live host truth. */
export function ConnectorHealthSummary({ node }: ConnectorHealthSummaryProps) {
  const health = node.connectorHealth;
  const snapshot = health?.snapshot;

  return (
    <Card data-testid="connector-health-summary">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <CardTitle>Connector outage evidence</CardTitle>
            <CardDescription>
              Bounded evidence replayed after the normal outbound synchronization channel recovered.
            </CardDescription>
          </div>
          <StatusBadge
            status={
              snapshot == null ? 'unavailable' : node.isOnline ? snapshot.state : 'last known'
            }
          />
        </div>
      </CardHeader>
      <CardContent className="grid gap-3 text-sm">
        {snapshot == null ? (
          <p className="text-muted-foreground">
            {node.isOnline
              ? 'Unavailable: this connector has not replayed bounded health evidence.'
              : 'Reason unavailable: the connector is unreachable and has never replayed bounded health evidence.'}
          </p>
        ) : (
          <>
            {!node.isOnline ? (
              <p className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
                Last-known connector evidence received {formatTime(health?.receivedAt ?? null)}.
                This is not a current host measurement.
              </p>
            ) : null}
            <dl className="grid gap-3 sm:grid-cols-3">
              <EvidenceField label="Last attempt" value={formatTime(snapshot.lastAttemptAt)} />
              <EvidenceField label="Last success" value={formatTime(snapshot.lastSuccessAt)} />
              <EvidenceField
                label="Consecutive failures"
                value={String(snapshot.consecutiveFailures)}
              />
              <EvidenceField
                label="Failure category"
                value={snapshot.lastFailureCategory ?? 'Unavailable'}
              />
              <EvidenceField
                label="Affected profile"
                value={snapshot.lastFailureProfileId ?? 'Node-wide or unavailable'}
              />
              <EvidenceField label="Next retry" value={formatTime(snapshot.nextRetryAt)} />
            </dl>
            {snapshot.lastFailureDetail ? (
              <p className="text-muted-foreground">{snapshot.lastFailureDetail}</p>
            ) : null}
            {snapshot.activeOutageId ? (
              <p className="font-medium text-amber-900 dark:text-amber-100">
                Active outage since {formatTime(snapshot.activeOutageStartedAt)}.
              </p>
            ) : snapshot.lastRecoveredOutageId ? (
              <p className="font-medium text-emerald-800 dark:text-emerald-200">
                Recovered outage {formatTime(snapshot.lastRecoveredOutageStartedAt)} to{' '}
                {formatTime(snapshot.lastRecoveredAt)} (
                {snapshot.lastRecoveredFailureCategory ?? 'category unavailable'}).
              </p>
            ) : (
              <p className="text-muted-foreground">
                No retained connector outage interval has been replayed.
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}

function EvidenceField({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 font-medium">{value}</dd>
    </div>
  );
}
