import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { DisplayNameEditor } from '@/components/DisplayNameEditor';
import { CopyableId } from '@/core/ui/CopyableId';

import { renameTenant } from './settingsApi';

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
        <CardTitle as="h2">Tenant settings</CardTitle>
        <CardDescription>
          Change the operator-facing name. The stable tenant ID is not editable.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        <div className="grid gap-1">
          <span className="text-sm font-medium">Tenant ID</span>
          <CopyableId value={tenantId} label="tenant ID" />
        </div>
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
