import { useEffect, useMemo, useState } from 'react';
import { Navigate, Outlet, useParams } from 'react-router-dom';

import { hasMinimumTenantRole, useSession } from '@/core/auth';
import { ReadinessSummary } from '@/core/ui/ReadinessSummary';
import { StateBanner } from '@/core/ui/StateBanner';
import { StatusBadge } from '@/core/ui/StatusBadge';
import type { TaskNavigationItem } from '@/core/ui/TaskNavigation';
import { TaskWorkspace } from '@/core/ui/TaskWorkspace';

import {
  getImageBuildRequests,
  getImageCandidates,
  getImageRecipeRegistrations,
} from './imagesApi';
import type { ImageWorkspaceContext, ImageWorkspaceData } from './imageWorkspaceContext';

/** Resolves the parent Runner images destination to candidate activity. */
export function ImageWorkspaceLandingPage() {
  const { tenantId = '' } = useParams();
  return <Navigate replace to={`/tenants/${encodeURIComponent(tenantId)}/images/candidates`} />;
}

/** Owns one bounded polling projection for candidate, request, and recipe tasks. */
export default function ImageWorkspace() {
  const { tenantId = '' } = useParams();
  const { session } = useSession();
  const tenant = session?.tenants.find((candidate) => candidate.tenantId === tenantId);
  const [data, setData] = useState<ImageWorkspaceData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const imagesPath = `/tenants/${encodeURIComponent(tenantId)}/images`;
  const canAdminister = tenant != null && hasMinimumTenantRole(tenant.role, 'administrator');

  useEffect(() => {
    const controller = new AbortController();
    let requestVersion = 0;
    const load = async () => {
      const version = requestVersion + 1;
      requestVersion = version;
      try {
        const [registrationList, requestList, candidateList] = await Promise.all([
          getImageRecipeRegistrations(tenantId, controller.signal),
          getImageBuildRequests(tenantId, controller.signal),
          getImageCandidates(tenantId, controller.signal),
        ]);
        if (controller.signal.aborted || requestVersion !== version) return;
        setData({
          registrations: registrationList.registrations,
          registrationsTruncated: registrationList.truncated,
          requests: requestList.requests,
          requestsTruncated: requestList.truncated,
          candidates: candidateList.candidates,
          candidatesTruncated: candidateList.truncated,
        });
        setError(null);
      } catch (caught) {
        if (controller.signal.aborted || requestVersion !== version) return;
        setError(
          caught instanceof Error ? caught.message : 'Runner image evidence could not be loaded.',
        );
      }
    };
    void load();
    const timer = globalThis.setInterval(() => void load(), 8_000);
    return () => {
      globalThis.clearInterval(timer);
      controller.abort();
    };
  }, [refreshVersion, tenantId]);

  const tasks = useMemo<ReadonlyArray<TaskNavigationItem>>(
    () => [
      {
        label: 'Candidates',
        description: 'Build requests, qualification, and immutable image evidence.',
        path: `${imagesPath}/candidates`,
      },
      {
        label: 'Recipes',
        description: 'Frozen trusted workflow registrations and declared inputs.',
        path: `${imagesPath}/recipes`,
      },
    ],
    [imagesPath],
  );

  if (!session || !tenant) return null;

  const activeRequests =
    data?.requests.filter((request) =>
      ['requested', 'dispatching', 'building', 'qualifying'].includes(request.status),
    ).length ?? 0;
  const attentionRequests =
    data?.requests.filter((request) => request.status === 'blocked' || request.status === 'failed')
      .length ?? 0;
  const enabledRegistrations =
    data?.registrations.filter((registration) => registration.disabledAt === null).length ?? 0;
  const readyCandidates =
    data?.candidates.filter((candidate) => candidate.outcome === 'ready').length ?? 0;
  const unavailable = data === null && error !== null;

  return (
    <section className="grid min-w-0 gap-5">
      <ReadinessSummary
        title="Image readiness"
        description="Trusted workflow authority, durable build progress, and immutable qualification evidence for this tenant."
        status={
          <StatusBadge
            status={
              data === null
                ? error
                  ? 'Status unavailable'
                  : 'Loading'
                : error
                  ? 'Stale evidence'
                  : attentionRequests > 0
                    ? 'Needs attention'
                    : activeRequests > 0
                      ? 'Builds active'
                      : readyCandidates > 0
                        ? 'Candidates ready'
                        : enabledRegistrations > 0
                          ? 'Ready to request'
                          : 'Recipe registration required'
            }
            tone={
              unavailable
                ? 'critical'
                : error || attentionRequests > 0
                  ? 'caution'
                  : readyCandidates > 0
                    ? 'positive'
                    : 'neutral'
            }
          />
        }
        items={[
          {
            label: 'Enabled recipes',
            value: data ? enabledRegistrations : error ? 'Unavailable' : 'Loading…',
            detail: 'Frozen workflow registrations',
          },
          {
            label: 'Active builds',
            value: data ? activeRequests : error ? 'Unavailable' : 'Loading…',
            detail: 'Requested through qualifying',
          },
          {
            label: 'Ready candidates',
            value: data ? readyCandidates : error ? 'Unavailable' : 'Loading…',
            detail: 'Immutable qualified image evidence',
          },
          {
            label: 'Needs attention',
            value: data ? attentionRequests : error ? 'Unavailable' : 'Loading…',
            detail: 'Blocked or failed requests',
          },
        ]}
      />

      {error ? (
        <StateBanner tone={data ? 'caution' : 'critical'}>
          {data
            ? `Showing retained image evidence because refresh failed: ${error}`
            : `Runner image evidence is unavailable: ${error}`}
        </StateBanner>
      ) : null}

      <TaskWorkspace navigationLabel="Runner image tasks" navigationItems={tasks}>
        <Outlet
          context={
            {
              tenantId,
              antiforgeryToken: session.antiforgeryToken,
              canAdminister,
              data,
              error,
              isLoading: data === null && error === null,
              refresh: () => setRefreshVersion((current) => current + 1),
            } satisfies ImageWorkspaceContext
          }
        />
      </TaskWorkspace>
    </section>
  );
}
