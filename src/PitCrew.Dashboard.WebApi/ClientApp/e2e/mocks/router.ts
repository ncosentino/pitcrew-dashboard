/**
 * Installs `page.route` handlers that serve sanitized fixture data for every
 * dashboard API endpoint the browser evidence specs exercise. This keeps
 * tests deterministic and bounded: no real backend, no shared mutable state
 * between tests, and no network egress beyond the local dev/preview server
 * that Playwright itself starts.
 */
import type { Page, Route } from '@playwright/test';

import type { DashboardSession } from '../../src/core/auth/sessionApi';
import type { FleetResponse } from '../../src/core/fleet/fleetApi';
import type { IncidentPage } from '../../src/features/fleet/incidentsApi';
import type {
  ImageBuildRequest,
  ImageCandidate,
  ImageRecipeRegistration,
} from '../../src/features/images/imagesApi';
import type {
  DiagnosticCredential,
  DiagnosticCredentialCreated,
  EnrollmentCodeResponse,
  TenantMember,
} from '../../src/features/settings/settingsApi';
import type { DashboardUser } from '../../src/core/auth/sessionApi';
import type { SupportIdentity, SupportSession } from '../../src/features/support/supportApi';

export type FleetOutcome = 'success' | 'network-error' | 'server-error';
export type MutationOutcome = 'success' | 'failure';
/**
 * `'success'` serves `options.session` (or a 401 when it is
 * `'unauthenticated'`, modeling LoginPage). `'server-error'` makes
 * `GET /api/session` fail with a 500, which `SessionProvider` treats as
 * anything-but-401 and surfaces as its `'error'` status (`SessionBoundary`'s
 * `role="alert"` retry surface) rather than `'unauthenticated'`.
 */
export type SessionOutcome = 'success' | 'server-error';

export interface MockApiOptions {
  /** `undefined` models a session request that fails with 401 (LoginPage). */
  readonly session: DashboardSession | 'unauthenticated';
  /** Controls the `GET /api/session` response. Defaults to `'success'`. */
  readonly sessionOutcome?: SessionOutcome;
  readonly fleet: FleetResponse;
  /** Controls the `GET .../fleet/v1/nodes` response. Defaults to `'success'`. */
  readonly fleetOutcome?: FleetOutcome;
  readonly incidents: IncidentPage;
  readonly tenantMembers?: ReadonlyArray<TenantMember>;
  readonly availableUsers?: ReadonlyArray<DashboardUser>;
  readonly diagnosticCredentials?: ReadonlyArray<DiagnosticCredential>;
  readonly enrollmentCode?: EnrollmentCodeResponse;
  readonly diagnosticCredentialCreated?: DiagnosticCredentialCreated;
  readonly supportIdentities?: ReadonlyArray<SupportIdentity>;
  readonly supportSessions?: ReadonlyArray<SupportSession>;
  readonly imageRecipeRegistrations?: ReadonlyArray<ImageRecipeRegistration>;
  readonly imageRecipeRegistrationsTruncated?: boolean;
  readonly imageBuildRequests?: ReadonlyArray<ImageBuildRequest>;
  readonly imageBuildRequestsTruncated?: boolean;
  readonly imageCandidates?: ReadonlyArray<ImageCandidate>;
  readonly imageCandidatesTruncated?: boolean;
  /** Controls every write endpoint (revoke/rotate/rename/acknowledge/etc). Defaults to `'success'`. */
  readonly mutationOutcome?: MutationOutcome;
}

const jsonHeaders = { 'content-type': 'application/json' } as const;

function errorBody(code: string, message: string): string {
  return JSON.stringify({ error: { code, message } });
}

function fulfillJson(route: Route, body: unknown): Promise<void> {
  return route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify(body) });
}

function mutationResponse(route: Route, outcome: MutationOutcome, body?: unknown): Promise<void> {
  if (outcome === 'failure') {
    return route.fulfill({
      status: 500,
      headers: jsonHeaders,
      body: errorBody('mutation_failed', 'The request could not be completed. Try again.'),
    });
  }
  if (body === undefined) return route.fulfill({ status: 204 });
  return route.fulfill({ status: 200, headers: jsonHeaders, body: JSON.stringify(body) });
}

/** Registers every mocked endpoint on `page`. Call once, before `page.goto`. */
export async function installMockApi(page: Page, options: MockApiOptions): Promise<void> {
  const mutationOutcome = options.mutationOutcome ?? 'success';
  const fleetOutcome = options.fleetOutcome ?? 'success';
  const sessionOutcome = options.sessionOutcome ?? 'success';

  await page.route('**/api/session', (route) => {
    if (sessionOutcome === 'server-error') {
      return route.fulfill({
        status: 500,
        headers: jsonHeaders,
        body: errorBody('session_unavailable', 'Dashboard session lookup failed unexpectedly.'),
      });
    }
    if (options.session === 'unauthenticated') {
      return route.fulfill({
        status: 401,
        headers: jsonHeaders,
        body: errorBody('unauthenticated', 'Sign-in required.'),
      });
    }
    return fulfillJson(route, options.session);
  });

  await page.route('**/auth/logout*', (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    return route.fulfill({ status: 204 });
  });

  await page.route(/\/api\/tenants\/[^/]+\/fleet\/v1\/nodes(\?.*)?$/, (route) => {
    if (fleetOutcome === 'network-error') return route.abort('failed');
    if (fleetOutcome === 'server-error') {
      return route.fulfill({
        status: 503,
        headers: jsonHeaders,
        body: errorBody('fleet_unavailable', 'Fleet snapshot temporarily unavailable.'),
      });
    }
    return fulfillJson(route, options.fleet);
  });

  await page.route(/\/api\/tenants\/[^/]+\/fleet\/v1\/incidents(\?.*)?$/, (route) =>
    fulfillJson(route, options.incidents),
  );
  await page.route(/\/api\/tenants\/[^/]+\/fleet\/v1\/incidents\/[^/]+\/acknowledge$/, (route) =>
    mutationResponse(route, mutationOutcome, { acknowledged: true }),
  );
  await page.route(/\/api\/tenants\/[^/]+\/fleet\/v1\/incidents\/[^/]+\/unacknowledge$/, (route) =>
    mutationResponse(route, mutationOutcome),
  );

  await page.route(/\/api\/tenants\/[^/]+\/fleet\/v1\/nodes\/[^/]+\/revoke$/, (route) =>
    mutationResponse(route, mutationOutcome),
  );
  await page.route(/\/api\/tenants\/[^/]+\/fleet\/v1\/nodes\/[^/]+$/, (route) => {
    if (route.request().method() !== 'PUT') return route.fallback();
    return mutationResponse(route, mutationOutcome);
  });
  await page.route(
    /\/api\/tenants\/[^/]+\/fleet\/v1\/nodes\/[^/]+\/credential-rotation$/,
    (route) => mutationResponse(route, mutationOutcome),
  );
  await page.route(
    /\/api\/tenants\/[^/]+\/fleet\/v1\/nodes\/[^/]+\/profiles\/[^/]+\/capacity-maximum$/,
    (route) => mutationResponse(route, mutationOutcome),
  );
  await page.route(
    /\/api\/tenants\/[^/]+\/fleet\/v1\/nodes\/[^/]+\/profiles\/[^/]+\/manager-recovery$/,
    (route) => mutationResponse(route, mutationOutcome),
  );

  await page.route(/\/api\/tenants\/[^/]+\/fleet\/v1\/enrollment-codes$/, (route) =>
    mutationResponse(route, mutationOutcome, options.enrollmentCode),
  );

  await page.route(/\/api\/tenants\/[^/]+\/members$/, (route) => {
    if (route.request().method() !== 'GET') return route.fallback();
    return fulfillJson(route, options.tenantMembers ?? []);
  });
  await page.route(/\/api\/tenants\/[^/]+\/members\/[^/]+$/, (route) =>
    mutationResponse(route, mutationOutcome),
  );
  await page.route(/\/api\/tenants\/[^/]+\/available-users$/, (route) =>
    fulfillJson(route, options.availableUsers ?? []),
  );

  await page.route(/\/api\/tenants\/[^/]+\/diagnostic-credentials$/, (route) => {
    if (route.request().method() === 'GET') {
      return fulfillJson(route, options.diagnosticCredentials ?? []);
    }
    return mutationResponse(route, mutationOutcome, options.diagnosticCredentialCreated);
  });
  await page.route(
    /\/api\/tenants\/[^/]+\/diagnostic-credentials\/[^/]+\/(revoke|rotate)$/,
    (route) => mutationResponse(route, mutationOutcome, options.diagnosticCredentialCreated),
  );

  await page.route(/\/api\/tenants\/[^/]+\/support\/v1\/identities$/, (route) =>
    fulfillJson(route, options.supportIdentities ?? []),
  );
  await page.route(/\/api\/tenants\/[^/]+\/support\/v1\/sessions\/[^/]+$/, (route) => {
    const sessionId = route.request().url().split('/').at(-1);
    const supportSession = options.supportSessions?.find(
      (candidate) => candidate.sessionId === sessionId,
    );
    if (!supportSession) {
      return route.fulfill({
        status: 404,
        headers: jsonHeaders,
        body: errorBody('not_found', 'Support session not found.'),
      });
    }
    return fulfillJson(route, supportSession);
  });
  await page.route(/\/api\/tenants\/[^/]+\/support\/v1\/sessions$/, (route) => {
    if (route.request().method() === 'GET') {
      return fulfillJson(route, options.supportSessions ?? []);
    }
    return mutationResponse(route, mutationOutcome, options.supportSessions?.[0]);
  });

  await page.route(
    /\/api\/tenants\/[^/]+\/images\/v1\/recipes\/registrations\/[^/]+\/disable$/,
    (route) => {
      const registration = options.imageRecipeRegistrations?.[0];
      return mutationResponse(
        route,
        mutationOutcome,
        registration ? { ...registration, disabledAt: '2026-08-28T12:30:00+00:00' } : undefined,
      );
    },
  );
  await page.route(
    /\/api\/tenants\/[^/]+\/images\/v1\/recipes\/registrations\/[^/?]+$/,
    (route) => {
      const registrationId = route.request().url().split('/').at(-1);
      const registration = options.imageRecipeRegistrations?.find(
        (candidate) => candidate.registrationId === registrationId,
      );
      return registration
        ? fulfillJson(route, registration)
        : route.fulfill({
            status: 404,
            headers: jsonHeaders,
            body: errorBody('not_found', 'Image recipe registration not found.'),
          });
    },
  );
  await page.route(/\/api\/tenants\/[^/]+\/images\/v1\/recipes\/registrations(\?.*)?$/, (route) => {
    if (route.request().method() === 'GET') {
      return fulfillJson(route, {
        registrations: options.imageRecipeRegistrations ?? [],
        truncated: options.imageRecipeRegistrationsTruncated ?? false,
      });
    }
    return mutationResponse(route, mutationOutcome, options.imageRecipeRegistrations?.[0]);
  });
  await page.route(/\/api\/tenants\/[^/]+\/images\/requests\/[^/?]+$/, (route) => {
    const requestId = route.request().url().split('/').at(-1);
    const request = options.imageBuildRequests?.find(
      (candidate) => candidate.requestId === requestId,
    );
    return request
      ? fulfillJson(route, request)
      : route.fulfill({
          status: 404,
          headers: jsonHeaders,
          body: errorBody('not_found', 'Image build request not found.'),
        });
  });
  await page.route(/\/api\/tenants\/[^/]+\/images\/requests(\?.*)?$/, (route) => {
    if (route.request().method() === 'GET') {
      return fulfillJson(route, {
        requests: options.imageBuildRequests ?? [],
        truncated: options.imageBuildRequestsTruncated ?? false,
      });
    }
    return mutationResponse(route, mutationOutcome, options.imageBuildRequests?.[0]);
  });
  await page.route(/\/api\/tenants\/[^/]+\/images\/candidates\/[^/?]+$/, (route) => {
    const candidateId = route.request().url().split('/').at(-1);
    const candidate = options.imageCandidates?.find((item) => item.candidateId === candidateId);
    return candidate
      ? fulfillJson(route, candidate)
      : route.fulfill({
          status: 404,
          headers: jsonHeaders,
          body: errorBody('not_found', 'Image candidate not found.'),
        });
  });
  await page.route(/\/api\/tenants\/[^/]+\/images\/candidates(\?.*)?$/, (route) =>
    fulfillJson(route, {
      candidates: options.imageCandidates ?? [],
      truncated: options.imageCandidatesTruncated ?? false,
    }),
  );

  await page.route('**/api/tenants', (route) => mutationResponse(route, mutationOutcome));
}
