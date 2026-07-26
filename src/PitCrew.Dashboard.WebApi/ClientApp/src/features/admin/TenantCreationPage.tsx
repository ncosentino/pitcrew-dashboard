import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useSession } from '@/core/auth';

interface TenantCreationPageProps {
  readonly createTenant: (
    tenantId: string,
    displayName: string,
    antiforgeryToken: string,
  ) => Promise<void>;
}

/** Creates tenants for an authorized system administrator. */
export default function TenantCreationPage({ createTenant }: TenantCreationPageProps) {
  const { session, refreshSession } = useSession();
  const navigate = useNavigate();
  const [tenantId, setTenantId] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [error, setError] = useState<string | null>(null);
  if (!session) return null;

  const create = async () => {
    const nextTenantId = tenantId.trim();
    try {
      await createTenant(nextTenantId, displayName.trim(), session.antiforgeryToken);
      setTenantId('');
      setDisplayName('');
      setError(null);
      await refreshSession();
      navigate(`/tenants/${nextTenantId}/fleet`);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Tenant could not be created.');
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>Create tenant</CardTitle>
        <CardDescription>
          Tenant IDs are stable lowercase route identifiers. You become the initial owner.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-3 sm:grid-cols-[1fr_1fr_auto]">
        <input
          aria-label="Tenant ID"
          className="h-9 rounded-md border bg-background px-3 text-sm"
          placeholder="tenant-id"
          value={tenantId}
          onChange={(event) => setTenantId(event.target.value)}
        />
        <input
          aria-label="Tenant display name"
          className="h-9 rounded-md border bg-background px-3 text-sm"
          placeholder="Display name"
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
        />
        <Button
          type="button"
          disabled={tenantId.trim().length === 0 || displayName.trim().length === 0}
          onClick={() => void create()}
        >
          Create
        </Button>
        {error ? <p role="alert">{error}</p> : null}
      </CardContent>
    </Card>
  );
}
