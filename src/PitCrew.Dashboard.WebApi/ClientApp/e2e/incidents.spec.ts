/**
 * Browser evidence for issue #234: exception-led fleet and incident workspaces.
 * Covers critical/warning mix, connector evidence present/absent, acknowledged,
 * resolved, and truncated history cases.
 */
import { test, expect, type Page, type TestInfo } from '@playwright/test';

import { viewports } from '../playwright.config';
import {
  buildFleetNode,
  buildFleetResponse,
  buildIncident,
  buildIncidentPage,
  buildProfile,
  buildSession,
  buildTenantMembers,
  buildAvailableUsers,
  buildDiagnosticCredentials,
  buildEnrollmentCode,
  buildDiagnosticCredentialCreated,
  nodeIds,
  tenantId,
} from './mocks/fixtures';
import type { MockApiOptions } from './mocks/router';
import { setUpPage } from './support/session';
import { runAxeCheck } from './support/axe';
import { measureDocumentOverflow } from './support/overflow';

const incidentsPath = `/tenants/${tenantId}/incidents`;
const fleetPath = `/tenants/${tenantId}/fleet`;

async function expectNoOverflowAndAccessible(
  page: Page,
  testInfo: TestInfo,
  name: string,
): Promise<void> {
  await page.waitForLoadState('networkidle');

  const overflow = await measureDocumentOverflow(page);
  expect(overflow.overflowPx, `document overflow on ${name}`).toBe(0);

  const axeResult = await runAxeCheck(page, testInfo, `incident-${name}`);
  expect(
    axeResult.unexpected,
    `unexpected axe violations on ${name}: ${axeResult.unexpected.map((v) => v.id).join(', ')}`,
  ).toHaveLength(0);

  await testInfo.attach(`screenshot-${name}`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
}

function baseScenario(): MockApiOptions {
  const alpha = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: 'Alpha',
    isOnline: true,
    profiles: [buildProfile('build')],
  });
  return {
    session: buildSession('owner'),
    fleet: buildFleetResponse([alpha], []),
    incidents: buildIncidentPage([]),
    tenantMembers: buildTenantMembers(),
    availableUsers: buildAvailableUsers(),
    diagnosticCredentials: buildDiagnosticCredentials(),
    enrollmentCode: buildEnrollmentCode(),
    diagnosticCredentialCreated: buildDiagnosticCredentialCreated(),
  };
}

const workspaceThemes = ['light', 'dark'] as const;
const workspaceViewports = [
  { name: 'desktop', size: viewports.desktop },
  { name: 'mobile', size: viewports.mobile },
] as const;

for (const theme of workspaceThemes) {
  for (const viewport of workspaceViewports) {
    test(`incident workspace keeps readiness, queue, and one case file visible in ${theme} ${viewport.name}`, async ({
      page,
    }, testInfo) => {
      const criticalIncident = buildIncident({
        incidentId: 'f1111111-1111-4111-8111-111111111111',
        title: 'Critical capacity deficit',
      });
      const warningIncident = buildIncident({
        incidentId: 'f2222222-2222-4222-8222-222222222222',
        severity: 'warning',
        title: 'Runner startup warning',
        reason: 'startup-delay',
      });
      const alpha = buildFleetNode({
        nodeId: nodeIds.alpha,
        displayName: 'Alpha',
        isOnline: true,
        profiles: [buildProfile('build')],
      });
      const base = baseScenario();
      const scenario: MockApiOptions = {
        ...base,
        fleet: buildFleetResponse([alpha], [criticalIncident, warningIncident]),
        incidents: buildIncidentPage([criticalIncident, warningIncident]),
      };

      await page.setViewportSize(viewport.size);
      await setUpPage(page, scenario, theme);
      await page.goto(incidentsPath);

      await expect(page.getByRole('region', { name: 'Incident work queue' })).toBeVisible();
      await expect(
        page.getByRole('heading', { name: 'Critical capacity deficit', level: 2 }),
      ).toBeVisible();
      await expect(page.getByRole('region', { name: 'Current evidence' })).toBeVisible();
      await expect(page.getByRole('region', { name: 'Lifecycle timeline' })).toBeVisible();
      if (viewport.name === 'mobile') {
        await expect(page.getByText('Choose incident', { exact: true })).toBeVisible();
        await page.getByText('Choose incident', { exact: true }).click();
      }
      await expect(page.getByRole('list', { name: 'Operational incident queue' })).toBeVisible();

      await expectNoOverflowAndAccessible(page, testInfo, `workspace-${theme}-${viewport.name}`);
    });
  }
}

test('critical/warning mix: both severities display with labeled counts', async ({
  page,
}, testInfo) => {
  const criticalIncident = buildIncident({
    incidentId: 'c1111111-1111-4111-8111-111111111111',
    severity: 'critical',
    title: 'Critical capacity deficit',
  });
  const warningIncident = buildIncident({
    incidentId: 'b2222222-2222-4222-8222-222222222222',
    severity: 'warning',
    title: 'Elevated runner startup time',
    kind: 'capacity-deficit',
  });
  const base = baseScenario();
  const scenario: MockApiOptions = {
    ...base,
    fleet: buildFleetResponse(
      [
        buildFleetNode({
          nodeId: nodeIds.alpha,
          displayName: 'Alpha',
          isOnline: true,
          profiles: [buildProfile('build')],
        }),
      ],
      [criticalIncident, warningIncident],
    ),
    incidents: buildIncidentPage([criticalIncident, warningIncident]),
  };

  await setUpPage(page, scenario, 'light');
  await page.goto(incidentsPath);

  await expect(
    page
      .getByTestId(`incident-row-${criticalIncident.incidentId}`)
      .getByRole('heading', { name: 'Critical capacity deficit' }),
  ).toBeVisible();
  await expect(
    page
      .getByTestId(`incident-row-${warningIncident.incidentId}`)
      .getByRole('heading', { name: 'Elevated runner startup time' }),
  ).toBeVisible();
  await expect(
    page.getByRole('heading', { name: 'Critical capacity deficit', level: 2 }),
  ).toBeVisible();

  // Fleet page shows the incident banner with both counts
  await page.goto(fleetPath);
  await expect(page.getByText('2 active incidents')).toBeVisible();
  await expect(page.getByText('1 critical')).toBeVisible();
  await expect(page.getByText('1 warning')).toBeVisible();
  await expect(page.getByLabel('2 active incidents; highest severity critical')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Review 2 active incidents' })).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'critical-warning-mix');
});

test('attention queue hides acknowledged incidents and supports filtering and sorting', async ({
  page,
}, testInfo) => {
  const triggeredCritical = buildIncident({
    incidentId: 'a1111111-1111-4111-8111-111111111111',
    title: 'Critical capacity deficit',
    triggeredAt: '2026-07-19T18:10:00+00:00',
    lastObservedAt: '2026-07-19T18:25:00+00:00',
  });
  const triggeredWarning = buildIncident({
    incidentId: 'a2222222-2222-4222-8222-222222222222',
    severity: 'warning',
    title: 'Runner startup warning',
    reason: 'startup-delay',
    triggeredAt: '2026-07-19T18:05:00+00:00',
    lastObservedAt: '2026-07-19T18:20:00+00:00',
  });
  const acknowledged = buildIncident({
    incidentId: 'a3333333-3333-4333-8333-333333333333',
    status: 'acknowledged',
    title: 'Acknowledged connector outage',
    acknowledgedAt: '2026-07-19T18:15:00+00:00',
    acknowledgedByGitHubUserId: '1001',
  });
  const base = baseScenario();
  const scenario: MockApiOptions = {
    ...base,
    incidents: buildIncidentPage([triggeredCritical, triggeredWarning, acknowledged]),
  };

  await setUpPage(page, scenario, 'light');
  await page.goto(incidentsPath);

  await expect(page.getByText(/2 need attention · 1 critical · 1 warning/i)).toBeVisible();
  await expect(page.getByText(/1 acknowledged hidden/i)).toBeVisible();
  await expect(page.getByText('Acknowledged connector outage')).toBeHidden();

  await page.getByLabel('Sort by').selectOption('oldest');
  const visibleIncidentRows = page
    .getByRole('list', { name: 'Operational incident queue' })
    .locator('[data-testid^="incident-row-"]');
  await expect(visibleIncidentRows.first()).toContainText('Runner startup warning');

  await page.getByLabel('Severity', { exact: true }).selectOption('warning');
  await page.getByLabel('Search incidents').fill('startup-delay');
  await expect(
    page
      .getByTestId(`incident-row-${triggeredWarning.incidentId}`)
      .getByRole('heading', { name: 'Runner startup warning' }),
  ).toBeVisible();
  await expect(page.getByText('Critical capacity deficit')).toBeHidden();

  await page.getByLabel('Work queue').selectOption('active');
  await page.getByLabel('Severity', { exact: true }).selectOption('all');
  await page.getByLabel('Search incidents').fill('');
  await expect(
    page
      .getByTestId(`incident-row-${acknowledged.incidentId}`)
      .getByRole('heading', { name: 'Acknowledged connector outage' }),
  ).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'attention-filter-sort');
});

test('connector evidence present: retained connector health shown for connector-offline incident', async ({
  page,
}, testInfo) => {
  const connectorIncident = buildIncident({
    incidentId: 'd3333333-3333-4333-8333-333333333333',
    kind: 'connector-offline',
    severity: 'critical',
    title: 'Connector is offline',
    reason: 'connector-offline',
    summary: 'No connector synchronization has been accepted.',
    profileId: null,
  });
  const nodeWithHealth = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: 'Alpha',
    isOnline: false,
    connectorFailure: true,
    profiles: [],
  });
  const base = baseScenario();
  const scenario: MockApiOptions = {
    ...base,
    fleet: buildFleetResponse([nodeWithHealth], [connectorIncident]),
    incidents: buildIncidentPage([connectorIncident]),
  };

  await setUpPage(page, scenario, 'light');
  await page.goto(incidentsPath);

  await expect(page.getByRole('heading', { name: 'Connector is offline', level: 2 })).toBeVisible();
  await expect(
    page
      .getByRole('region', { name: 'Connector recovery evidence' })
      .getByText(/synchronization-network/)
      .first(),
  ).toBeVisible();
  await expect(
    page.getByRole('region', { name: 'Connector recovery evidence' }).getByText('degraded'),
  ).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'connector-evidence-present');
});

test('connector evidence absent: absence stated truthfully for never-replayed connector', async ({
  page,
}, testInfo) => {
  const connectorIncident = buildIncident({
    incidentId: 'd4444444-4444-4444-8444-444444444444',
    kind: 'connector-offline',
    severity: 'critical',
    title: 'Connector is offline',
    reason: 'connector-offline',
    summary: 'No connector synchronization has been accepted.',
    profileId: null,
  });
  const base = baseScenario();
  const scenario: MockApiOptions = {
    ...base,
    fleet: buildFleetResponse([], []),
    incidents: buildIncidentPage([connectorIncident]),
  };

  await setUpPage(page, scenario, 'light');
  await page.goto(`${incidentsPath}?view=active`);

  await expect(page.getByRole('heading', { name: 'Connector is offline', level: 2 })).toBeVisible();
  await expect(page.getByText(/connector recovery evidence is unavailable/i)).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'connector-evidence-absent');
});

test('acknowledged: acknowledged incident shows ack state and undo action', async ({
  page,
}, testInfo) => {
  const ackedIncident = buildIncident({
    incidentId: 'a5555555-5555-4555-8555-555555555555',
    status: 'acknowledged',
    acknowledgedAt: '2026-07-19T18:20:00+00:00',
    acknowledgedByGitHubUserId: '1001',
  });
  const base = baseScenario();
  const scenario: MockApiOptions = {
    ...base,
    incidents: buildIncidentPage([ackedIncident]),
  };

  await setUpPage(page, scenario, 'light');
  await page.goto(`${incidentsPath}?view=active`);

  await expect(
    page.getByTestId(`incident-row-${ackedIncident.incidentId}`).getByText('acknowledged', {
      exact: true,
    }),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Unacknowledge incident' })).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'acknowledged');
});

test('resolved: resolved incident displays resolved timestamp', async ({ page }, testInfo) => {
  const resolvedIncident = buildIncident({
    incidentId: 'e6666666-6666-4666-8666-666666666666',
    status: 'resolved',
    resolvedAt: '2026-07-19T18:35:00+00:00',
  });
  const base = baseScenario();
  const scenario: MockApiOptions = {
    ...base,
    incidents: buildIncidentPage([resolvedIncident]),
  };

  await setUpPage(page, scenario, 'light');
  await page.goto(`${incidentsPath}?view=resolved`);

  await expect(
    page.getByTestId(`incident-row-${resolvedIncident.incidentId}`).getByText('resolved', {
      exact: true,
    }),
  ).toBeVisible();
  await expect(
    page.getByRole('region', { name: 'Lifecycle timeline' }).getByText('Resolved', { exact: true }),
  ).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'resolved');
});

test('truncated: truncation banner communicates bounded history', async ({ page }, testInfo) => {
  const incident = buildIncident();
  const base = baseScenario();
  const scenario: MockApiOptions = {
    ...base,
    incidents: buildIncidentPage([incident], true),
  };

  await setUpPage(page, scenario, 'light');
  await page.goto(incidentsPath);

  await expect(
    page.getByText(/showing only the newest incidents allowed by the server response limit/i),
  ).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'truncated');
});

test('fleet labeled capacity: slash-separated values replaced with labeled evidence', async ({
  page,
}, testInfo) => {
  const base = baseScenario();
  await setUpPage(page, base, 'light');
  await page.goto(fleetPath);

  await expect(page.getByText('Capacity evidence')).toBeVisible();
  const row = page.getByTestId(`fleet-node-${nodeIds.alpha}`);
  await expect(row.getByText('Configured')).toBeVisible();
  await expect(row.getByText('Local')).toBeVisible();
  await expect(row.getByText('Eligible')).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'labeled-capacity');
});
