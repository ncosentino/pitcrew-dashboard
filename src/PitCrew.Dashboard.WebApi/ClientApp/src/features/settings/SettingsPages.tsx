import { type ReactNode, useMemo, useState } from 'react';
import { Navigate, useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { hasMinimumTenantRole, useSession } from '@/core/auth';
import { formatTime } from '@/core/formatting/formatters';
import { FormField } from '@/core/ui/FormField';
import { SectionNavigation, type SectionNavigationItem } from '@/core/ui/SectionNavigation';

import { createEnrollmentCode, type EnrollmentCodeResponse } from './settingsApi';
import { DiagnosticCredentials } from './DiagnosticCredentials';
import { TenantAdministration } from './TenantAdministration';
import { TenantSettings } from './TenantSettings';

function useCurrentTenant() {
  const { tenantId = '' } = useParams();
  const sessionContext = useSession();
  const tenant = sessionContext.session?.tenants.find(
    (candidate) => candidate.tenantId === tenantId,
  );
  return { tenantId, tenant, ...sessionContext };
}

interface SettingsPageProps {
  readonly children: ReactNode;
}

function SettingsPage({ children }: SettingsPageProps) {
  const { tenantId, tenant } = useCurrentTenant();
  const settingsPath = `/tenants/${tenantId}/settings`;

  const items = useMemo(() => {
    if (!tenant) return [];
    const navItems: SectionNavigationItem[] = [];
    if (hasMinimumTenantRole(tenant.role, 'owner')) {
      navItems.push(
        { label: 'General', path: `${settingsPath}/general` },
        { label: 'Access', path: `${settingsPath}/access` },
      );
    }
    navItems.push({ label: 'Enrollment', path: `${settingsPath}/enrollment` });
    if (hasMinimumTenantRole(tenant.role, 'administrator')) {
      navItems.push({ label: 'Diagnostics', path: `${settingsPath}/diagnostics` });
    }
    return navItems;
  }, [settingsPath, tenant]);

  if (!tenant) return null;

  return (
    <section className="grid gap-4">
      <SectionNavigation label="Tenant settings" items={items} />
      {children}
    </section>
  );
}

/** Resolves the shared Settings destination to the first route the current role can access. */
export function SettingsLandingPage() {
  const { tenantId, tenant } = useCurrentTenant();
  if (!tenant) return null;

  const destination = hasMinimumTenantRole(tenant.role, 'owner')
    ? `/tenants/${tenantId}/settings/general`
    : `/tenants/${tenantId}/settings/enrollment`;
  return <Navigate replace to={destination} />;
}

/** Owner-managed general tenant settings route. */
export function GeneralSettingsPage() {
  const { tenantId, tenant, session, refreshSession } = useCurrentTenant();
  if (!tenant || !session) return null;
  return (
    <SettingsPage>
      <TenantSettings
        tenantId={tenantId}
        displayName={tenant.displayName}
        antiforgeryToken={session.antiforgeryToken}
        onRenamed={() => void refreshSession()}
      />
    </SettingsPage>
  );
}

/** Owner-managed tenant membership route. */
export function AccessSettingsPage() {
  const { tenantId, session } = useCurrentTenant();
  if (!session) return null;
  return (
    <SettingsPage>
      <TenantAdministration tenantId={tenantId} antiforgeryToken={session.antiforgeryToken} />
    </SettingsPage>
  );
}

/** Administrator-managed connector enrollment route. */
export function EnrollmentSettingsPage() {
  const { tenantId, session } = useCurrentTenant();
  const [label, setLabel] = useState('New server');
  const [code, setCode] = useState<EnrollmentCodeResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  if (!session) return null;

  const issueCode = async () => {
    setIsBusy(true);
    try {
      setCode(await createEnrollmentCode(tenantId, label.trim(), session.antiforgeryToken));
      setError(null);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Enrollment code could not be created.');
    } finally {
      setIsBusy(false);
    }
  };

  return (
    <SettingsPage>
      <Card>
        <CardHeader>
          <CardTitle as="h2">Enroll a connector</CardTitle>
          <CardDescription>
            Codes expire quickly and are consumed by exactly one enrollment or re-enrollment.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3">
          <div className="flex flex-wrap items-end gap-3">
            <FormField label="Connector label" hint="A name for this enrolled node.">
              <input
                className="h-9 min-w-64 flex-1 rounded-md border bg-background px-3 text-sm"
                value={label}
                onChange={(event) => setLabel(event.target.value)}
                maxLength={128}
              />
            </FormField>
            <Button
              type="button"
              disabled={isBusy || label.trim().length === 0}
              onClick={() => void issueCode()}
            >
              Create one-time code
            </Button>
          </div>
          {error ? (
            <p role="alert" className="text-sm text-destructive">
              {error}
            </p>
          ) : null}
          {code ? (
            <div
              className="grid gap-2 rounded-lg border border-status-caution-foreground/30 bg-status-caution p-4 text-status-caution-foreground"
              role="status"
            >
              <div className="text-sm font-semibold">Copy this code now</div>
              <code className="overflow-x-auto rounded bg-background p-3 text-xs text-foreground">
                {code.code}
              </code>
              <div className="text-xs">
                Expires {formatTime(code.expiresAt)}. It is not stored in recoverable form.
              </div>
              <div className="text-xs">
                Set the connector environment variable PitCrew__Connector__EnrollmentCode to this
                value and start the connector. After enrollment succeeds, remove the consumed
                variable from its environment.
              </div>
            </div>
          ) : null}
        </CardContent>
      </Card>
    </SettingsPage>
  );
}

/** Administrator-managed noninteractive diagnostics route. */
export function DiagnosticsSettingsPage() {
  const { tenantId, session } = useCurrentTenant();
  if (!session) return null;
  return (
    <SettingsPage>
      <DiagnosticCredentials tenantId={tenantId} antiforgeryToken={session.antiforgeryToken} />
    </SettingsPage>
  );
}
