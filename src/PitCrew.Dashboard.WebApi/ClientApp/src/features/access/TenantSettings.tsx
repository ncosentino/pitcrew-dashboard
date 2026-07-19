import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { DisplayNameEditor } from '@/components/DisplayNameEditor';

import { renameTenant } from './accessApi';

/** Props for owner-managed tenant settings. */
export interface TenantSettingsProps {
  readonly tenantId: string;
  readonly displayName: string;
  readonly antiforgeryToken: string;
  readonly onRenamed: (displayName: string) => void;
}

/** Allows a tenant owner to change the operator-facing name while preserving its stable ID. */
export function TenantSettings({
  tenantId,
  displayName,
  antiforgeryToken,
  onRenamed,
}: TenantSettingsProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Tenant settings</CardTitle>
        <CardDescription>
          Change the operator-facing name. The stable tenant ID remains {tenantId}.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <DisplayNameEditor
          value={displayName}
          label="Tenant display name"
          submitLabel="Rename tenant"
          successMessage="Tenant name updated."
          onSave={async (nextDisplayName) => {
            await renameTenant(tenantId, nextDisplayName, antiforgeryToken);
            onRenamed(nextDisplayName);
          }}
        />
      </CardContent>
    </Card>
  );
}
