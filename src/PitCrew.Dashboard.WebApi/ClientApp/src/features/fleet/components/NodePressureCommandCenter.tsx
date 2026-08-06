import { useEffect, useMemo, useState } from 'react';

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import type { FleetNode, OperationalIncident } from '@/core/fleet';
import { formatBytes, formatSeconds, formatTime } from '@/core/formatting/formatters';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { getIncidents } from '../incidentsApi';

interface NodePressureCommandCenterProps {
  readonly tenantId: string;
  readonly node: FleetNode;
  readonly activeIncidents: ReadonlyArray<OperationalIncident>;
  readonly generatedAt: string;
}

interface WorkloadRow {
  readonly key: string;
  readonly profileId: string;
  readonly activity: string;
  readonly cpuCores: number | null;
  readonly memoryBytes: number | null;
  readonly pids: number | null;
  readonly startedAt: string | null;
  readonly label: string;
  readonly repository: string | null;
  readonly href: string | null;
}

const pressureIncidentKinds = new Set([
  'host-cpu-pressure',
  'host-memory-pressure',
  'host-io-pressure',
]);

function percent(value: number | null | undefined): string {
  if (value == null) return 'Unavailable';
  return `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value)}%`;
}

function load(value: number | null | undefined): string {
  if (value == null) return 'Unavailable';
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value);
}

function workloadRows(node: FleetNode): readonly WorkloadRow[] {
  return node.profiles
    .flatMap((profile) =>
      profile.slots.flatMap((slot): WorkloadRow[] => {
        if (
          !slot.processRunning ||
          (slot.activity !== 'busy' && slot.activity !== 'draining' && slot.currentJob == null)
        ) {
          return [];
        }
        const job = slot.currentJob;
        return [
          {
            key: `${profile.profileId}:${slot.key}`,
            profileId: profile.profileId,
            activity: slot.activity ?? 'unknown',
            cpuCores: slot.resources?.cpuCores ?? null,
            memoryBytes: slot.resources?.memoryWorkingSetBytes ?? null,
            pids: slot.resources?.pids ?? null,
            startedAt: job?.startedAt ?? slot.updatedAt ?? null,
            label: job?.displayName ?? 'Unattributed busy worker',
            repository: job?.repository ?? slot.repository,
            href: job
              ? `${job.repository}/actions/runs/${job.workflowRunId}/job/${job.jobId}`
              : null,
          },
        ];
      }),
    )
    .sort(
      (left, right) =>
        (right.cpuCores ?? -1) - (left.cpuCores ?? -1) ||
        (right.memoryBytes ?? -1) - (left.memoryBytes ?? -1) ||
        Date.parse(left.startedAt ?? '') - Date.parse(right.startedAt ?? ''),
    );
}

function latestPressure(node: FleetNode) {
  return node.profiles
    .flatMap((profile) => {
      const telemetry = profile.resourceTelemetry;
      return telemetry?.hostPressure
        ? [{ pressure: telemetry.hostPressure, sampledAt: telemetry.sampledAt }]
        : [];
    })
    .sort((left, right) => Date.parse(right.sampledAt) - Date.parse(left.sampledAt))[0];
}

/** Renders node-wide pressure, demand, workload attribution, and exact GitHub triage links. */
export function NodePressureCommandCenter({
  tenantId,
  node,
  activeIncidents,
  generatedAt,
}: NodePressureCommandCenterProps) {
  const [recentIncident, setRecentIncident] = useState<OperationalIncident | null>(null);
  const [incidentError, setIncidentError] = useState<string | null>(null);
  const pressure = latestPressure(node);
  const workloads = useMemo(() => workloadRows(node), [node]);
  const demand = node.profiles.reduce(
    (total, profile) => ({
      running: total.running + (profile.autoscaling?.runningJobs ?? 0),
      assigned: total.assigned + (profile.autoscaling?.assignedJobs ?? 0),
      queued: total.queued + (profile.autoscaling?.availableJobs ?? 0),
      busy: total.busy + profile.slots.filter((slot) => slot.activity === 'busy').length,
    }),
    { running: 0, assigned: 0, queued: 0, busy: 0 },
  );
  const pressureIncidents = activeIncidents.filter(
    (incident) => incident.nodeId === node.nodeId && pressureIncidentKinds.has(incident.kind),
  );

  useEffect(() => {
    const controller = new AbortController();
    const loadRecent = async () => {
      try {
        const page = await getIncidents(tenantId, 'resolved', controller.signal);
        if (controller.signal.aborted) return;
        setRecentIncident(
          page.incidents.find(
            (incident) =>
              incident.nodeId === node.nodeId && pressureIncidentKinds.has(incident.kind),
          ) ?? null,
        );
        setIncidentError(null);
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === 'AbortError') return;
        setIncidentError(
          caught instanceof Error ? caught.message : 'Recent pressure incidents are unavailable.',
        );
      }
    };
    void loadRecent();
    const timer = globalThis.setInterval(() => void loadRecent(), 30_000);
    return () => {
      controller.abort();
      globalThis.clearInterval(timer);
    };
  }, [node.nodeId, tenantId]);

  return (
    <section className="grid gap-4" aria-labelledby="node-pressure-heading">
      <div>
        <h3 className="text-xl font-semibold" id="node-pressure-heading">
          Host pressure and active workloads
        </h3>
        <p className="text-sm text-muted-foreground">
          Docker-host or VM pressure with exact GitHub links when manager job context is available.
        </p>
      </div>

      {pressureIncidents.map((incident) => (
        <div
          className="rounded-lg border border-red-300 bg-red-50 px-4 py-3 text-red-950 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
          key={incident.incidentId}
          role="alert"
        >
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status={incident.severity} />
            <span className="font-semibold">{incident.title}</span>
          </div>
          <p className="mt-1 text-sm">{incident.summary}</p>
        </div>
      ))}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Docker-host pressure</CardTitle>
            <CardDescription>
              {pressure
                ? `Sampled ${formatTime(pressure.sampledAt)}.`
                : 'No pressure sample reported.'}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
              <div>
                <dt className="text-xs text-muted-foreground uppercase">CPU</dt>
                <dd className="mt-1 font-semibold tabular-nums">
                  {percent(pressure?.pressure.cpuUtilizationPercent)}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">Load 1m</dt>
                <dd className="mt-1 font-semibold tabular-nums">
                  {load(pressure?.pressure.load1)}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">Available memory</dt>
                <dd className="mt-1 font-semibold tabular-nums">
                  {pressure?.pressure.memoryAvailableBytes == null
                    ? 'Unavailable'
                    : formatBytes(pressure.pressure.memoryAvailableBytes)}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase">I/O PSI</dt>
                <dd className="mt-1 font-semibold tabular-nums">
                  {percent(pressure?.pressure.ioPressureSomeAvg10)}
                </dd>
              </div>
            </dl>
            <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <StatusBadge status={pressure?.pressure.status ?? 'unavailable'} />
              <span>
                Memory PSI {percent(pressure?.pressure.memoryPressureSomeAvg10)} · CPU PSI{' '}
                {percent(pressure?.pressure.cpuPressureSomeAvg10)} · swap{' '}
                {pressure?.pressure.swapUsedBytes == null
                  ? 'unavailable'
                  : formatBytes(pressure.pressure.swapUsedBytes)}
              </span>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>GitHub demand</CardTitle>
            <CardDescription>
              Current scale-set and local worker evidence across profiles.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
              {[
                ['Running', demand.running],
                ['Busy workers', demand.busy],
                ['Assigned', demand.assigned],
                ['Available / queued', demand.queued],
              ].map(([label, value]) => (
                <div key={label}>
                  <dt className="text-xs text-muted-foreground uppercase">{label}</dt>
                  <dd className="mt-1 text-xl font-semibold tabular-nums">{value}</dd>
                </div>
              ))}
            </dl>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Active workers and jobs</CardTitle>
          <CardDescription>
            Highest current worker CPU first. Cancellation remains in GitHub; Dashboard stores no
            Actions write credential.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {workloads.length === 0 ? (
            <p className="text-sm text-muted-foreground">No busy or draining worker is reported.</p>
          ) : (
            <div
              aria-label="Active workers and jobs"
              className="max-h-80 overflow-auto rounded border"
              role="region"
              tabIndex={0}
            >
              <table className="w-full text-left text-sm">
                <thead className="text-xs text-muted-foreground uppercase">
                  <tr>
                    <th className="px-3 py-2" scope="col">
                      Workload
                    </th>
                    <th className="px-3 py-2" scope="col">
                      Profile
                    </th>
                    <th className="px-3 py-2" scope="col">
                      Elapsed
                    </th>
                    <th className="px-3 py-2" scope="col">
                      CPU / memory / PIDs
                    </th>
                    <th className="px-3 py-2" scope="col">
                      Action
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {workloads.map((workload) => (
                    <tr className="border-t align-top" key={workload.key}>
                      <th className="px-3 py-2 font-medium" scope="row">
                        {workload.label}
                        <div className="text-xs font-normal text-muted-foreground">
                          {workload.repository ?? 'Repository unavailable'} · {workload.activity}
                        </div>
                      </th>
                      <td className="px-3 py-2">{workload.profileId}</td>
                      <td className="px-3 py-2 tabular-nums">
                        {workload.startedAt
                          ? formatSeconds(
                              Math.max(
                                0,
                                (Date.parse(generatedAt) - Date.parse(workload.startedAt)) / 1000,
                              ),
                            )
                          : 'Unavailable'}
                      </td>
                      <td className="px-3 py-2 tabular-nums">
                        {workload.cpuCores?.toFixed(2) ?? '—'} /{' '}
                        {workload.memoryBytes == null ? '—' : formatBytes(workload.memoryBytes)} /{' '}
                        {workload.pids ?? '—'}
                      </td>
                      <td className="px-3 py-2">
                        {workload.href ? (
                          <a
                            className="font-medium text-primary underline-offset-4 hover:underline"
                            href={workload.href}
                            rel="noreferrer"
                            target="_blank"
                          >
                            Open in GitHub
                          </a>
                        ) : (
                          <span className="text-xs text-muted-foreground">
                            Job link unavailable
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {pressureIncidents.length === 0 && recentIncident ? (
        <div className="rounded-lg border bg-muted/20 px-4 py-3 text-sm">
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status="resolved" />
            <span className="font-semibold">Recent pressure episode: {recentIncident.title}</span>
          </div>
          <p className="mt-1 text-muted-foreground">
            {formatTime(recentIncident.triggeredAt)} –{' '}
            {formatTime(recentIncident.resolvedAt ?? recentIncident.lastObservedAt)} ·{' '}
            {recentIncident.summary}
          </p>
          {recentIncident.evidence ? (
            <p className="mt-1 font-mono text-xs text-muted-foreground">
              Retained evidence: {recentIncident.evidence}
            </p>
          ) : null}
        </div>
      ) : null}
      {incidentError ? (
        <p className="text-sm text-amber-800 dark:text-amber-200" role="status">
          {incidentError}
        </p>
      ) : null}
    </section>
  );
}
