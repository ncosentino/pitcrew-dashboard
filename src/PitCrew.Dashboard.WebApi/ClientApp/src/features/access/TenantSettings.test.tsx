import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { TenantSettings } from './TenantSettings';

describe('TenantSettings', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renames the tenant and reports the normalized name', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response(null, { status: 204 }));
    const onRenamed = vi.fn();
    const user = userEvent.setup();
    render(
      <TenantSettings
        tenantId="local"
        displayName="Local"
        antiforgeryToken="token"
        onRenamed={onRenamed}
      />,
    );

    const input = screen.getByLabelText('Tenant display name');
    await user.clear(input);
    await user.type(input, '  Renamed tenant  ');
    await user.click(screen.getByRole('button', { name: 'Rename tenant' }));

    await waitFor(() => expect(onRenamed).toHaveBeenCalledWith('Renamed tenant'));
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(String(url)).toMatch(/\/api\/tenants\/local$/);
    expect(init?.method).toBe('PUT');
    expect(new Headers(init?.headers).get('X-PitCrew-Antiforgery')).toBe('token');
    expect(JSON.parse(String(init?.body))).toEqual({ displayName: 'Renamed tenant' });
    expect(input).toHaveValue('Renamed tenant');
    expect(screen.getByRole('button', { name: 'Rename tenant' })).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent('Tenant name updated.');
  });

  it('surfaces API failures without reporting success', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          error: {
            code: 'invalid_tenant_name',
            message: 'Tenant display name is invalid.',
          },
        }),
        {
          status: 400,
          headers: { 'Content-Type': 'application/json' },
        },
      ),
    );
    const onRenamed = vi.fn();
    const user = userEvent.setup();
    render(
      <TenantSettings
        tenantId="local"
        displayName="Local"
        antiforgeryToken="token"
        onRenamed={onRenamed}
      />,
    );

    const input = screen.getByLabelText('Tenant display name');
    await user.clear(input);
    await user.type(input, 'Renamed tenant');
    await user.click(screen.getByRole('button', { name: 'Rename tenant' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Tenant display name is invalid.');
    expect(onRenamed).not.toHaveBeenCalled();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
