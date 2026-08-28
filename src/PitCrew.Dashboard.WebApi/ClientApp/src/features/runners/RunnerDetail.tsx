import { useId, type ReactNode, type Ref } from 'react';
import { Link } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { formatBytes, formatCpuCores, formatPids, formatTime } from '@/core/formatting/formatters';
import { CopyableId } from '@/core/ui/CopyableId';
import { DetailPanel } from '@/core/ui/DetailPanel';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import { WorkerExitEvidence, WorkerImageIdentity } from '@/core/ui/WorkerEvidenceCells';

import type { FleetSlot } from './runnerRows';

interface RunnerDetailProps {
  readonly row: FleetSlot;
  readonly tenantId: string;
  readonly isVisible: boolean;
  readonly focusTitleRef?: Ref<HTMLHeadingElement>;
}

/** Presents one selected runner slot and its explicit GitHub job correlation. */
export function RunnerDetail({ row, tenantId, isVisible, focusTitleRef }: RunnerDetailProps) {
  const sectionId = useId();
  const job = row.slot.currentJob;
  const profilePath = `/tenants/${encodeURIComponent(tenantId)}/nodes/${encodeURIComponent(row.nodeId)}/profiles/${encodeURIComponent(row.profileId)}`;
  const jobHref = job
    ? `${job.repository}/actions/runs/${job.workflowRunId}/job/${job.jobId}`
    : null;
  const title = job
    ? (job.displayName ?? `GitHub job ${job.jobId}`)
    : row.slot.activity === 'busy'
      ? 'Busy runner without job identity'
      : `${row.nodeName} · ${row.slot.key}`;

  return (
    <DetailPanel
      title={title}
      description={`${row.nodeName} · Profile ${row.profileId} · Slot ${row.slot.key}`}
      focusTitleRef={focusTitleRef}
      status={
        <>
          {!row.nodeOnline ? <StatusBadge status="offline" /> : null}
          <StatusBadge status={row.slot.activity ?? 'activity unavailable'} />
          <StatusBadge status={row.slot.state} />
          <StatusBadge status={row.slot.registrationStatus ?? 'registration unavailable'} />
        </>
      }
      actions={
        <>
          <Button asChild size="sm" variant="outline">
            <Link to={profilePath}>Open profile</Link>
          </Button>
          {jobHref ? (
            <Button asChild size="sm" variant="outline">
              <a href={jobHref} rel="noreferrer" target="_blank">
                Open job in GitHub
              </a>
            </Button>
          ) : null}
        </>
      }
    >
      <div className="grid min-w-0 gap-5">
        {!isVisible ? (
          <StateBanner tone="caution" role="status">
            This deep-linked runner is outside the current inventory filters. Its dispatch sheet
            remains selected so the investigation keeps context.
          </StateBanner>
        ) : null}

        <section aria-labelledby={`${sectionId}-job`}>
          <h3 id={`${sectionId}-job`} className="text-sm font-semibold">
            Current GitHub job
          </h3>
          {job ? (
            <dl className="mt-3 grid gap-3 sm:grid-cols-2">
              <RunnerFact
                label="Repository"
                value={
                  <a
                    className="text-link underline-offset-4 hover:underline"
                    href={job.repository}
                    rel="noreferrer"
                    target="_blank"
                  >
                    {job.repository}
                  </a>
                }
              />
              <RunnerFact label="Event" value={job.eventName ?? 'Unavailable'} />
              <RunnerFact label="Started" value={formatTime(job.startedAt)} />
              <RunnerFact
                label="Result"
                value={job.finishedAt ? (job.result ?? 'Unavailable') : 'In progress'}
              />
              <RunnerFact
                label="Workflow run ID"
                value={<CopyableId value={String(job.workflowRunId)} label="workflow run ID" />}
              />
              <RunnerFact label="Job ID" value={<CopyableId value={job.jobId} label="job ID" />} />
              <RunnerFact
                label="Scale set assigned"
                value={job.scaleSetAssignedAt ? formatTime(job.scaleSetAssignedAt) : 'Unavailable'}
              />
              <RunnerFact
                label="Runner assigned"
                value={job.runnerAssignedAt ? formatTime(job.runnerAssignedAt) : 'Unavailable'}
              />
            </dl>
          ) : row.slot.currentJob === undefined ? (
            <div
              className="mt-3 border-t pt-3 text-sm text-status-caution-foreground"
              role="status"
            >
              Current job identity is unavailable for this manager contract. Activity and resource
              use are not treated as workload identity.
            </div>
          ) : row.slot.activity === 'busy' || row.slot.activity === 'draining' ? (
            <div
              className="mt-3 border-t pt-3 text-sm text-status-caution-foreground"
              role="status"
            >
              The runner reports {row.slot.activity} activity but no current GitHub job. Do not
              infer job identity from process or resource activity.
            </div>
          ) : (
            <div className="mt-3 border-t pt-3 text-sm text-muted-foreground">
              No current GitHub job is assigned to this runner.
            </div>
          )}
        </section>

        <section aria-labelledby={`${sectionId}-lifecycle`}>
          <h3 id={`${sectionId}-lifecycle`} className="text-sm font-semibold">
            Runner lifecycle
          </h3>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <RunnerFact label="Target" value={row.slot.target ?? 'Unavailable'} />
            <RunnerFact label="Desired" value={row.slot.desired ? 'Yes' : 'No'} />
            <RunnerFact label="Process" value={row.slot.processRunning ? 'Running' : 'Stopped'} />
            <RunnerFact label="Updated" value={formatTime(row.slot.updatedAt)} />
            <RunnerFact label="Failures" value={String(row.slot.failureCount)} />
            <RunnerFact label="Backoff" value={`${row.slot.backoffSeconds} seconds`} />
            <RunnerFact
              label="Runner identity hash"
              value={
                row.slot.runnerNameHash ? (
                  <CopyableId value={row.slot.runnerNameHash} label="runner identity hash" />
                ) : (
                  'Unavailable'
                )
              }
            />
            <RunnerFact
              label="Last exit"
              value={<WorkerExitEvidence lastExit={row.slot.lastExit} />}
            />
          </dl>
        </section>

        <section aria-labelledby={`${sectionId}-resources`}>
          <h3 id={`${sectionId}-resources`} className="text-sm font-semibold">
            Reported runner evidence
          </h3>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <RunnerFact
              label="CPU"
              value={
                row.slot.resources ? formatCpuCores(row.slot.resources.cpuCores) : 'Unavailable'
              }
            />
            <RunnerFact
              label="Memory"
              value={
                row.slot.resources
                  ? formatBytes(row.slot.resources.memoryWorkingSetBytes)
                  : 'Unavailable'
              }
            />
            <RunnerFact
              label="Processes"
              value={row.slot.resources ? formatPids(row.slot.resources.pids) : 'Unavailable'}
            />
            <RunnerFact
              label="Worker image"
              value={<WorkerImageIdentity imageId={row.slot.imageId} />}
            />
          </dl>
        </section>

        <section aria-labelledby={`${sectionId}-related`} className="border-t pt-4">
          <h3 id={`${sectionId}-related`} className="text-sm font-semibold">
            Related evidence
          </h3>
          <p className="mt-1 text-sm text-muted-foreground">
            Profile-owned pages retain worker inventory, capacity, diagnostics, and history without
            duplicating those records here.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <Button asChild size="sm" variant="outline">
              <Link to={`${profilePath}/workers`}>Open profile workers</Link>
            </Button>
            <Button asChild size="sm" variant="outline">
              <Link to={`${profilePath}/history`}>Open retained history</Link>
            </Button>
          </div>
        </section>
      </div>
    </DetailPanel>
  );
}

interface RunnerFactProps {
  readonly label: string;
  readonly value: ReactNode;
}

function RunnerFact({ label, value }: RunnerFactProps) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-1 [overflow-wrap:anywhere] text-sm font-medium text-foreground">{value}</dd>
    </div>
  );
}
