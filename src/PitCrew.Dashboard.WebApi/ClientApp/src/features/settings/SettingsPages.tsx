import { useState } from 'react';
import { useParams } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';
import { formatTime } from '@/core/formatting/formatters';

import { createEnrollmentCode, type EnrollmentCodeResponse } from './settingsApi';
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

/** Owner-managed general tenant settings route. */
export function GeneralSettingsPage() {
  const { tenantId, tenant, session, refreshSession } = useCurrentTenant();
  if (!tenant || !session) return null;
  return (
    <TenantSettings
      tenantId={tenantId}
      displayName={tenant.displayName}
      antiforgeryToken={session.antiforgeryToken}
      onRenamed={() => void refreshSession()}
    />
  );
}

/** Owner-managed tenant membership route. */
export function AccessSettingsPage() {
  const { tenantId, session } = useCurrentTenant();
  if (!session) return null;
  return <TenantAdministration tenantId={tenantId} antiforgeryToken={session.antiforgeryToken} />;
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
    <Card>
      <CardHeader>
        <CardTitle>Enroll a connector</CardTitle>
        <CardDescription>
          Codes expire quickly and are consumed by exactly one enrollment or re-enrollment.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-3">
        <div className="flex flex-wrap gap-3">
          <input
            aria-label="Connector label"
            className="h-9 min-w-64 flex-1 rounded-md border bg-background px-3 text-sm"
            value={label}
            onChange={(event) => setLabel(event.target.value)}
            maxLength={128}
          />
          <Button
            type="button"
            disabled={isBusy || label.trim().length === 0}
            onClick={() => void issueCode()}
          >
            Create one-time code
          </Button>
        </div>
        {error ? (
          <p role="alert" className="text-sm text-red-700 dark:text-red-300">
            {error}
          </p>
        ) : null}
        {code ? (
          <div className="grid gap-2 rounded-lg border border-amber-300 bg-amber-50 p-4 text-amber-950 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
            <div className="text-sm font-semibold">Copy this code now</div>
            <code className="overflow-x-auto rounded bg-background p-3 text-xs">{code.code}</code>
            <div className="text-xs">
              Expires {formatTime(code.expiresAt)}. It is not stored in recoverable form.
            </div>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}
