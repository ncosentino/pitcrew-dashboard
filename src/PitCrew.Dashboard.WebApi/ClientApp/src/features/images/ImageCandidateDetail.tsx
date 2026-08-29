import type { ReactNode, Ref } from 'react';

import { Button } from '@/components/ui/button';
import { formatTime } from '@/core/formatting/formatters';
import { CopyableId } from '@/core/ui/CopyableId';
import { DetailPanel } from '@/core/ui/DetailPanel';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import type { ImageBuildRequest, ImageCandidate, ImageRecipeRegistration } from './imagesApi';

interface ImageCandidateDetailProps {
  readonly request: ImageBuildRequest;
  readonly candidate: ImageCandidate | null;
  readonly registration: ImageRecipeRegistration | null;
  readonly focusTitleRef?: Ref<HTMLHeadingElement>;
}

function shortCommit(commit: string): string {
  return commit.slice(0, 12);
}

function qualificationLabel(name: ImageCandidate['qualifications'][number]['name']): string {
  return name.replaceAll('-', ' ');
}

function formatLifecycle(status: ImageBuildRequest['status']): string {
  const label = status.replaceAll('-', ' ');
  return label.charAt(0).toLocaleUpperCase() + label.slice(1);
}

/** Presents one build request and its immutable candidate evidence without raw workflow logs. */
export function ImageCandidateDetail({
  request,
  candidate,
  registration,
  focusTitleRef,
}: ImageCandidateDetailProps) {
  const runUrl = candidate?.githubRunUrl ?? request.githubRunHtmlUrl;

  return (
    <DetailPanel
      title={`${request.recipeId} · ${shortCommit(request.sourceCommit)}`}
      description={`${request.sourceRepository} · ${request.sourceRef}`}
      focusTitleRef={focusTitleRef}
      status={
        <>
          <StatusBadge status={request.status} />
          {candidate ? (
            <StatusBadge
              status={candidate.outcome}
              tone={candidate.outcome === 'ready' ? 'positive' : 'critical'}
            />
          ) : null}
        </>
      }
      actions={
        runUrl ? (
          <Button asChild size="sm" variant="outline">
            <a href={runUrl} rel="noreferrer" target="_blank">
              Open exact GitHub run
            </a>
          </Button>
        ) : null
      }
    >
      <div className="grid min-w-0 gap-5">
        {request.status === 'ready' && candidate === null ? (
          <StateBanner tone="critical">
            The request reports ready, but immutable candidate evidence is unavailable. Reload
            before using this request for rollout.
          </StateBanner>
        ) : null}

        <section aria-labelledby={`request-authority-${request.requestId}`}>
          <h3 id={`request-authority-${request.requestId}`} className="text-sm font-semibold">
            Build authority
          </h3>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2">
            <Fact
              label="Request ID"
              value={<CopyableId value={request.requestId} label="image build request ID" />}
            />
            <Fact
              label="Registration"
              value={
                <CopyableId value={request.registrationId} label="image recipe registration ID" />
              }
            />
            <Fact label="Registration version" value={request.registrationVersion} />
            <Fact label="Recipe" value={request.recipeId} />
            <Fact label="Source repository" value={request.sourceRepository} />
            <Fact label="Source ref" value={request.sourceRef} />
            <Fact
              label="Source commit"
              value={<CopyableId value={request.sourceCommit} label="source commit" />}
            />
            <Fact label="Requested" value={formatTime(request.requestedAt)} />
          </dl>
        </section>

        <section aria-labelledby={`request-execution-${request.requestId}`}>
          <h3 id={`request-execution-${request.requestId}`} className="text-sm font-semibold">
            Workflow execution
          </h3>
          <dl className="mt-3 grid gap-3 sm:grid-cols-2">
            <Fact label="Lifecycle" value={formatLifecycle(request.status)} />
            <Fact label="Updated" value={formatTime(request.updatedAt)} />
            <Fact
              label="GitHub run ID"
              value={
                request.githubRunId ? (
                  <CopyableId value={request.githubRunId} label="GitHub workflow run ID" />
                ) : (
                  'Unavailable'
                )
              }
            />
            <Fact label="Terminal category" value={request.terminalCategory ?? 'Not terminal'} />
          </dl>
          {request.terminalDetail ? (
            <StateBanner
              className="mt-3"
              tone={request.status === 'failed' ? 'critical' : 'caution'}
            >
              {request.terminalDetail}
            </StateBanner>
          ) : null}
        </section>

        {candidate ? (
          <>
            <section aria-labelledby={`candidate-evidence-${candidate.candidateId}`}>
              <h3
                id={`candidate-evidence-${candidate.candidateId}`}
                className="text-sm font-semibold"
              >
                Immutable candidate evidence
              </h3>
              <dl className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                <Fact
                  label="Candidate ID"
                  value={<CopyableId value={candidate.candidateId} label="image candidate ID" />}
                />
                <Fact label="Outcome" value={candidate.outcome} />
                <Fact label="Platform" value={candidate.platform} />
                <Fact label="Output mode" value={candidate.outputMode} />
                <Fact label="Image reference" value={candidate.imageReference} />
                <Fact
                  label="Immutable digest"
                  value={
                    candidate.digest ? (
                      <CopyableId value={candidate.digest} label="image digest" />
                    ) : (
                      'Unavailable'
                    )
                  }
                />
                <Fact
                  label="Immutable reference"
                  value={
                    candidate.immutableReference ? (
                      <CopyableId
                        value={candidate.immutableReference}
                        label="immutable image reference"
                      />
                    ) : candidate.outputMode === 'oci' ? (
                      'Not applicable for OCI output'
                    ) : (
                      'Unavailable'
                    )
                  }
                />
                <Fact label="Artifact ID" value={candidate.artifactId} />
                <Fact label="Stored" value={formatTime(candidate.storedAt)} />
              </dl>
              {candidate.failureDetail ? (
                <StateBanner className="mt-3" tone="critical">
                  {candidate.failureCategory}: {candidate.failureDetail}
                </StateBanner>
              ) : null}
            </section>

            <section
              aria-labelledby={`candidate-qualifications-${candidate.candidateId}`}
              className="border-t pt-4"
            >
              <h3
                id={`candidate-qualifications-${candidate.candidateId}`}
                className="text-sm font-semibold"
              >
                Qualification evidence
              </h3>
              <ul className="mt-3 divide-y overflow-hidden rounded-lg border">
                {candidate.qualifications.map((qualification) => (
                  <li
                    className="flex min-w-0 flex-wrap items-center justify-between gap-2 px-3 py-2"
                    key={qualification.name}
                  >
                    <span className="text-sm font-medium capitalize">
                      {qualificationLabel(qualification.name)}
                    </span>
                    <StatusBadge
                      status={qualification.status}
                      tone={
                        qualification.status === 'passed'
                          ? 'positive'
                          : qualification.status === 'failed'
                            ? 'critical'
                            : 'caution'
                      }
                    />
                  </li>
                ))}
              </ul>
            </section>
          </>
        ) : (
          <section
            aria-labelledby={`candidate-pending-${request.requestId}`}
            className="border-t pt-4"
          >
            <h3 id={`candidate-pending-${request.requestId}`} className="text-sm font-semibold">
              Candidate evidence
            </h3>
            <p className="mt-2 text-sm text-muted-foreground">
              {request.status === 'blocked' || request.status === 'failed'
                ? 'No candidate was created from this terminal request.'
                : 'Candidate evidence will appear only after the exact workflow run and report complete validation.'}
            </p>
          </section>
        )}

        <section aria-labelledby={`recipe-evidence-${request.requestId}`} className="border-t pt-4">
          <h3 id={`recipe-evidence-${request.requestId}`} className="text-sm font-semibold">
            Registered recipe evidence
          </h3>
          {registration ? (
            <dl className="mt-3 grid gap-3 sm:grid-cols-2">
              <Fact
                label="Repository"
                value={`${registration.repositoryOwner}/${registration.repositoryName}`}
              />
              <Fact label="Workflow" value={registration.workflowPath} />
              <Fact
                label="Workflow blob"
                value={
                  <CopyableId value={registration.workflowBlobSha} label="workflow blob identity" />
                }
              />
              <Fact label="Dispatch ref" value={registration.dispatchRef} />
            </dl>
          ) : (
            <p className="mt-2 text-sm text-muted-foreground">
              The exact recipe registration version is not present in this bounded response.
            </p>
          )}
        </section>
      </div>
    </DetailPanel>
  );
}

function Fact({ label, value }: { readonly label: string; readonly value: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-1 min-w-0 [overflow-wrap:anywhere] text-sm font-medium">{value}</dd>
    </div>
  );
}
