/**
 * Browser evidence for issue #89: fleet and operational incidents UX migration.
 * Covers critical/warning mix, connector evidence present/absent, acknowledged,
 * resolved, and truncated history cases.
 */
import { test, expect, type Page, type TestInfo } from '@playwright/test';

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

  await expect(page.getByRole('link', { name: 'Critical capacity deficit' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Elevated runner startup time' })).toBeVisible();

  // Fleet page shows the incident banner with both counts
  await page.goto(fleetPath);
  await expect(page.getByText('2 active incidents')).toBeVisible();
  await expect(page.getByText('1 critical')).toBeVisible();
  await expect(page.getByText('1 warning')).toBeVisible();
  await expect(page.getByLabel('2 active incidents; highest severity critical')).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'critical-warning-mix');
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

  await expect(page.getByRole('link', { name: 'Connector is offline' })).toBeVisible();
  await expect(page.getByText(/Retained connector evidence/)).toBeVisible();
  await expect(
    page
      .getByRole('region', { name: 'Active operational incidents' })
      .getByText(/synchronization-network/),
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
  await page.goto(incidentsPath);

  await expect(page.getByRole('link', { name: 'Connector is offline' })).toBeVisible();
  await expect(page.getByText(/never replayed bounded health evidence/)).toBeVisible();

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
  await page.goto(incidentsPath);

  await expect(page.getByText('acknowledged').first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Unacknowledge' })).toBeVisible();

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
  await page.goto(incidentsPath);

  await expect(page.getByText('resolved').first()).toBeVisible();
  await expect(
    page.getByRole('region', { name: 'Active operational incidents' }).getByText(/^Resolved /),
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
