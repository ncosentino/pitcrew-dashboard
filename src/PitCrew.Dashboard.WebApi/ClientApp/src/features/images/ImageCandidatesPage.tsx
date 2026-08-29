import { useEffect, useMemo, useRef } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { formatTime } from '@/core/formatting/formatters';
import { EmptyState } from '@/core/ui/EmptyState';
import { LoadingState } from '@/core/ui/LoadingState';
import { OperationalList, OperationalRow } from '@/core/ui/OperationalList';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';

import { ImageBuildRequestForm } from './ImageBuildRequestForm';
import { ImageCandidateDetail } from './ImageCandidateDetail';
import { useImageWorkspace } from './imageWorkspaceContext';
import type { ImageBuildRequest } from './imagesApi';

function requestRank(request: ImageBuildRequest): number {
  switch (request.status) {
    case 'blocked':
      return 0;
    case 'failed':
      return 1;
    case 'qualifying':
      return 2;
    case 'building':
      return 3;
    case 'dispatching':
      return 4;
    case 'requested':
      return 5;
    case 'ready':
      return 10;
  }
}

/** Presents attention-ordered image build requests and one focused candidate record. */
export default function ImageCandidatesPage() {
  const { tenantId, antiforgeryToken, canAdminister, data, error, isLoading, refresh } =
    useImageWorkspace();
  const [searchParams, setSearchParams] = useSearchParams();
  const detailHeading = useRef<HTMLHeadingElement>(null);
  const pendingFocus = useRef<string | null>(null);
  const requests = useMemo(
    () =>
      [...(data?.requests ?? [])].sort(
        (left, right) =>
          requestRank(left) - requestRank(right) ||
          Date.parse(right.updatedAt) - Date.parse(left.updatedAt),
      ),
    [data?.requests],
  );
  const requestedId = searchParams.get('request');
  const requestedRequest =
    requestedId == null ? undefined : requests.find((request) => request.requestId === requestedId);
  const selectedRequest = requestedId == null ? (requests[0] ?? null) : (requestedRequest ?? null);
  const selectedCandidate =
    selectedRequest == null
      ? null
      : (data?.candidates.find((candidate) => candidate.requestId === selectedRequest.requestId) ??
        null);
  const selectedRegistration =
    selectedRequest == null
      ? null
      : (data?.registrations.find(
          (registration) =>
            registration.registrationId === selectedRequest.registrationId &&
            registration.version === selectedRequest.registrationVersion,
        ) ?? null);
  const missingSelection = data !== null && requestedId !== null && requestedRequest === undefined;

  useEffect(() => {
    if (requestedId !== null || requests[0] === undefined) return;
    setSearchParams(
      (current) => {
        if (current.get('request')) return current;
        const next = new URLSearchParams(current);
        next.set('request', requests[0].requestId);
        return next;
      },
      { replace: true },
    );
  }, [requestedId, requests, setSearchParams]);

  useEffect(() => {
    if (selectedRequest?.requestId !== pendingFocus.current) return;
    pendingFocus.current = null;
    detailHeading.current?.focus();
  }, [selectedRequest]);

  const selectRequest = (requestId: string) => {
    pendingFocus.current = requestId;
  };

  const onCreated = (requestId: string) => {
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current);
        next.set('request', requestId);
        return next;
      },
      { replace: false },
    );
    pendingFocus.current = requestId;
    refresh();
  };

  if (isLoading) return <LoadingState label="Loading image candidates…" />;
  if (!data) {
    return error ? null : (
      <EmptyState
        title="Image evidence unavailable"
        description="The candidate workspace cannot load without an authoritative API response."
      />
    );
  }

  return (
    <section aria-labelledby="image-candidates-heading" className="grid min-w-0 gap-4">
      <div>
        <h2 id="image-candidates-heading" className="text-lg font-semibold">
          Candidate activity
        </h2>
        <p className="mt-1 max-w-[72ch] text-sm text-muted-foreground">
          Trusted workflow requests ordered by blocked or failed evidence, active work, then ready
          history.
        </p>
      </div>

      {data.requestsTruncated || data.candidatesTruncated ? (
        <StateBanner tone="caution">
          This bounded view shows the newest 100 records. Older request or candidate evidence is not
          included.
        </StateBanner>
      ) : null}
      {missingSelection ? (
        <StateBanner tone="caution" role="status">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>
              The requested build record is not present in this bounded response. Another record has
              not been substituted.
            </span>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={() =>
                setSearchParams(
                  (current) => {
                    const next = new URLSearchParams(current);
                    next.delete('request');
                    return next;
                  },
                  { replace: true },
                )
              }
            >
              Clear selection
            </Button>
          </div>
        </StateBanner>
      ) : null}

      {requests.length === 0 ? (
        <EmptyState
          title="No image build requests"
          description={
            canAdminister
              ? 'Request a candidate build after an administrator registers an enabled trusted recipe.'
              : 'No administrator has requested a candidate build for this tenant.'
          }
        />
      ) : (
        <div className="grid min-w-0 gap-4 xl:grid-cols-[minmax(19rem,0.9fr)_minmax(0,1.1fr)] xl:items-start">
          <OperationalList label="Image build requests">
            {requests.map((request) => {
              const candidate = data.candidates.find(
                (item) => item.requestId === request.requestId,
              );
              const selected = request.requestId === selectedRequest?.requestId;
              const next = new URLSearchParams(searchParams);
              next.set('request', request.requestId);
              return (
                <OperationalRow
                  key={request.requestId}
                  selected={selected}
                  status={
                    <div className="flex flex-wrap gap-1.5">
                      <StatusBadge status={request.status} />
                      {candidate ? <StatusBadge status={candidate.outcome} /> : null}
                    </div>
                  }
                  title={`${request.recipeId} · ${request.sourceCommit.slice(0, 12)}`}
                  description={`${request.sourceRepository} · ${request.sourceRef}`}
                  metadata={
                    <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground">
                      <span>Updated {formatTime(request.updatedAt)}</span>
                      <span>
                        {request.githubRunId
                          ? `GitHub run ${request.githubRunId}`
                          : 'Run identity unavailable'}
                      </span>
                      {request.terminalCategory ? (
                        <span className="font-medium text-foreground">
                          {request.terminalCategory}
                        </span>
                      ) : null}
                    </div>
                  }
                  actions={
                    <Button asChild size="sm" variant={selected ? 'secondary' : 'outline'}>
                      <Link
                        aria-current={selected ? 'page' : undefined}
                        to={`?${next.toString()}`}
                        onClick={() => selectRequest(request.requestId)}
                      >
                        {selected ? 'Selected' : 'Inspect'}
                      </Link>
                    </Button>
                  }
                />
              );
            })}
          </OperationalList>

          {selectedRequest ? (
            <div aria-label="Selected image build evidence" className="min-w-0" role="region">
              <ImageCandidateDetail
                request={selectedRequest}
                candidate={selectedCandidate}
                registration={selectedRegistration}
                focusTitleRef={detailHeading}
              />
            </div>
          ) : null}
        </div>
      )}

      {canAdminister ? (
        <ImageBuildRequestForm
          key={data.registrations
            .map(
              (registration) =>
                `${registration.registrationId}:${registration.version}:${registration.disabledAt ?? 'enabled'}`,
            )
            .join('|')}
          tenantId={tenantId}
          antiforgeryToken={antiforgeryToken}
          registrations={data.registrations}
          onCreated={onCreated}
        />
      ) : (
        <p className="text-sm text-muted-foreground">
          Viewer access is read-only. Tenant administrators request candidate builds.
        </p>
      )}
    </section>
  );
}
