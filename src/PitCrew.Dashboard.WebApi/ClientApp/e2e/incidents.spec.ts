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

      await expect(page.getByRole('region', { name: 'Incident action queue' })).toBeVisible();
      await expect(
        page.getByRole('heading', { name: 'Critical capacity deficit', level: 2 }),
      ).toBeVisible();
      await expect(page.getByRole('region', { name: 'Confirmed problem evidence' })).toBeVisible();
      await expect(page.getByRole('region', { name: 'Lifecycle timeline' })).toBeVisible();
      if (viewport.name === 'mobile') {
        await expect(page.getByText('Filter incidents', { exact: true })).toBeVisible();
        await expect(page.getByText('Choose incident', { exact: true })).toBeVisible();
        await expect(page.getByRole('list', { name: 'Incident records' })).toBeHidden();
        const detailBox = await page
          .getByRole('heading', { name: 'Critical capacity deficit', level: 2 })
          .boundingBox();
        expect(detailBox).not.toBeNull();
        expect(detailBox?.y ?? Number.POSITIVE_INFINITY).toBeLessThan(viewport.size.height);
      } else {
        await expect(page.getByRole('list', { name: 'Incident records' })).toBeVisible();
      }

      await expectNoOverflowAndAccessible(page, testInfo, `workspace-${theme}-${viewport.name}`);
    });
  }
}

test('long incident and node evidence remains contained at the narrow viewport', async ({
  page,
}, testInfo) => {
  const longTitle = `Capacity-${'x'.repeat(145)}`;
  const longNodeName = `Node-${'y'.repeat(150)}`;
  const longIncident = buildIncident({
    incidentId: 'f3333333-3333-4333-8333-333333333333',
    title: longTitle,
    summary: 's'.repeat(512),
    evidence: 'e'.repeat(512),
    profileId: 'p'.repeat(128),
  });

  const longNode = buildFleetNode({
    nodeId: nodeIds.alpha,
    displayName: longNodeName,
    isOnline: true,
    profiles: [buildProfile('build')],
  });
  const base = baseScenario();
  const scenario: MockApiOptions = {
    ...base,
    fleet: buildFleetResponse([longNode], [longIncident]),
    incidents: buildIncidentPage([longIncident]),
  };

  await page.setViewportSize(viewports.narrow);
  await setUpPage(page, scenario, 'light');
  await page.goto(incidentsPath);

  await expect(page.getByRole('heading', { name: longTitle, level: 2 })).toBeVisible();
  await page.getByText('Choose incident', { exact: true }).click();
  await expect(page.getByRole('list', { name: 'Incident records' })).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'long-content-narrow');
});

for (const total of [0, 1, 62, 200, 201]) {
  test(`preserves exact incident hierarchy at the ${total}-record boundary`, async ({ page }) => {
    const visibleCount = total === 201 ? 200 : total;
    const incidents = Array.from({ length: visibleCount }, (_, index) =>
      buildIncident({
        incidentId: `f6000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
        severity: index === 0 ? 'critical' : 'warning',
        title: `Boundary incident ${index + 1}`,
      }),
    );
    const alpha = buildFleetNode({
      nodeId: nodeIds.alpha,
      displayName: 'Alpha',
      isOnline: true,
      profiles: [buildProfile('build')],
    });
    const scenario: MockApiOptions = {
      ...baseScenario(),
      fleet: buildFleetResponse([alpha], incidents, {
        activeIncidentTotal: total,
        activeCriticalIncidentTotal: total > 0 ? 1 : 0,
        activeIncidentsTruncated: total === 201,
      }),
      incidents: buildIncidentPage(incidents, total === 201),
    };

    await page.setViewportSize(total >= 200 ? viewports.mobile : viewports.desktop);
    await setUpPage(page, scenario, 'light');
    await page.goto(incidentsPath);
    await page.waitForLoadState('networkidle');

    const readiness = page.getByRole('region', { name: 'Incident action queue' });
    await expect(readiness.getByText('Records in view').locator('..')).toContainText(String(total));
    if (total === 0) {
      await expect(page.getByText('No incidents require action')).toBeVisible();
    } else {
      await expect(
        page.getByText(`${visibleCount} require action`, { exact: false }),
      ).toBeVisible();
    }
    if (total === 201) {
      await expect(page.getByRole('button', { name: 'Load more incidents' })).toBeVisible();
    }
    expect((await measureDocumentOverflow(page)).overflowPx).toBe(0);
  });
}

test('exactly retrieves an older critical incident beyond the visible queue', async ({ page }) => {
  const visible = buildIncident({ title: 'Visible warning', severity: 'warning' });
  const olderCritical = buildIncident({
    incidentId: 'f8000000-0000-4000-8000-000000000001',
    title: 'Older retained critical incident',
    triggeredAt: '2026-08-01T10:00:00+00:00',
  });
  const scenario = {
    ...baseScenario(),
    incidents: buildIncidentPage([visible], true),
    incidentDetails: [olderCritical],
  };

  await setUpPage(page, scenario, 'light');
  await page.goto(`${incidentsPath}?view=active&incident=${olderCritical.incidentId}`);

  await expect(
    page.getByRole('heading', { level: 2, name: 'Older retained critical incident' }),
  ).toBeVisible();
  await expect(page.getByText(/outside the current queue filters/i)).toBeVisible();
  await expect(page.getByRole('list', { name: 'Incident records' })).toContainText(
    'Visible warning',
  );
});

test('mobile incident selection collapses the queue and focuses the case file', async ({
  page,
}, testInfo) => {
  const criticalIncident = buildIncident({
    incidentId: 'f4444444-4444-4444-8444-444444444444',
    title: 'Critical capacity deficit',
  });
  const warningIncident = buildIncident({
    incidentId: 'f5555555-5555-4555-8555-555555555555',
    severity: 'warning',
    title: 'Runner startup warning',
    reason: 'startup-delay',
  });
  const base = baseScenario();
  const scenario: MockApiOptions = {
    ...base,
    incidents: buildIncidentPage([criticalIncident, warningIncident]),
  };

  await page.setViewportSize(viewports.mobile);
  await setUpPage(page, scenario, 'light');
  await page.goto(`${incidentsPath}?view=active`);

  await page.getByText('Choose incident', { exact: true }).click();
  const queue = page.getByRole('list', { name: 'Incident records' });
  await expect(queue).toBeVisible();
  await page
    .getByTestId(`incident-row-${warningIncident.incidentId}`)
    .getByRole('link', { name: 'Investigate' })
    .click();

  const selectedCase = page.getByRole('region', { name: 'Selected incident case' });
  await expect(selectedCase).toBeFocused();
  await expect(
    page.getByRole('heading', { name: 'Runner startup warning', level: 2 }),
  ).toBeVisible();
  await expect(queue).toBeHidden();

  await expectNoOverflowAndAccessible(page, testInfo, 'mobile-selection-focus');
});

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
  const incidentSummary = page.getByTestId('fleet-active-incidents');
  await expect(incidentSummary.getByText('2 open incident records')).toBeVisible();
  await expect(incidentSummary.getByText('1 critical', { exact: true })).toBeVisible();
  await expect(incidentSummary.getByText('1 warning', { exact: true })).toBeVisible();
  await expect(
    page.getByLabel('2 open incident records; highest unowned current severity critical'),
  ).toBeVisible();
  await expect(
    incidentSummary.getByRole('link', { name: 'Review 2 open incident records' }),
  ).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'critical-warning-mix');
});

test('truncated fleet summary qualifies slice-derived severity counts', async ({
  page,
}, testInfo) => {
  const warningIncident = buildIncident({
    incidentId: 'b3333333-3333-4333-8333-333333333333',
    severity: 'warning',
    title: 'Visible warning from bounded fleet slice',
  });
  const base = baseScenario();
  const scenario: MockApiOptions = {
    ...base,
    fleet: buildFleetResponse(base.fleet.nodes, [warningIncident], {
      activeIncidentTotal: 3,
      activeCriticalIncidentTotal: 1,
      activeIncidentsTruncated: true,
    }),
  };

  await setUpPage(page, scenario, 'light');
  await page.goto(fleetPath);

  const incidentSummary = page.getByTestId('fleet-active-incidents');
  await expect(incidentSummary.getByText('3 open incident records', { exact: true })).toBeVisible();
  await expect(incidentSummary.getByText('1 critical', { exact: true })).toBeVisible();
  await expect(incidentSummary.getByText('1 warning shown', { exact: true })).toBeVisible();
  await expect(incidentSummary.getByText('1 warning', { exact: true })).toHaveCount(0);
  await expect(
    page.getByLabel('3 open incident records; highest unowned current severity critical'),
  ).toBeVisible();

  await expectNoOverflowAndAccessible(page, testInfo, 'truncated-fleet-summary');
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

  await expect(page.getByText(/2 require action · 1 critical · 1 warning/i)).toBeVisible();
  await expect(page.getByText(/1 operator-owned hidden/i)).toBeVisible();
  await expect(page.getByText('Acknowledged connector outage')).toBeHidden();

  await page.getByLabel('Sort by').selectOption('oldest');
  const visibleIncidentRows = page
    .getByRole('list', { name: 'Incident records' })
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

  await page.getByLabel('Work queue', { exact: true }).selectOption('active');
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
  await expect(
    page.getByText(/referenced node is not present in the accepted fleet projection/i),
  ).toBeVisible();

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
    page
      .getByTestId(`incident-row-${ackedIncident.incidentId}`)
      .getByText('Operator acknowledged', {
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
    page.getByTestId(`incident-row-${resolvedIncident.incidentId}`).getByText('Legacy evidence', {
      exact: true,
    }),
  ).toBeVisible();
  await expect(
    page.getByRole('region', { name: 'Lifecycle timeline' }).getByText('Resolved', { exact: true }),
  ).toBeVisible();
  await expect(page.getByRole('region', { name: 'Retained incident evidence' })).toBeVisible();

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
    page.getByText(/authoritative matching incidents in attention-ranked order/i),
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
