import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';

import { OperationalTable } from '@/core/ui/OperationalTable';

describe('OperationalTable', () => {
  it('renders a captioned table with column headers and row content', () => {
    render(
      <OperationalTable
        caption="Fleet nodes for the active tenant"
        columns={[
          { key: 'node', header: 'Node' },
          { key: 'profiles', header: 'Profiles', align: 'right' },
        ]}
      >
        <tr data-testid="fleet-node-alpha">
          <td>Alpha</td>
          <td className="text-right">4</td>
        </tr>
      </OperationalTable>,
    );

    const table = screen.getByRole('table', { name: 'Fleet nodes for the active tenant' });
    expect(within(table).getByRole('columnheader', { name: 'Node' })).toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: 'Profiles' })).toBeInTheDocument();
    expect(within(table).getByTestId('fleet-node-alpha')).toHaveTextContent('Alpha');
  });

  it('wraps the table in a scrollable region named from the same caption', () => {
    render(
      <OperationalTable
        caption="Fleet nodes for the active tenant"
        columns={[{ key: 'node', header: 'Node' }]}
      >
        <tr>
          <td>Alpha</td>
        </tr>
      </OperationalTable>,
    );

    const region = screen.getByRole('region', { name: 'Fleet nodes for the active tenant' });
    expect(
      within(region).getByRole('table', { name: 'Fleet nodes for the active tenant' }),
    ).toBeInTheDocument();
  });

  it('applies a minimum table width so columns never wrap into a stack', () => {
    render(
      <OperationalTable caption="Fleet nodes" columns={[{ key: 'node', header: 'Node' }]}>
        <tr>
          <td>Alpha</td>
        </tr>
      </OperationalTable>,
    );

    expect(screen.getByRole('table')).toHaveClass('min-w-5xl');
  });

  it('accepts a custom minimum width class', () => {
    render(
      <OperationalTable
        caption="Fleet nodes"
        columns={[{ key: 'node', header: 'Node' }]}
        minWidthClassName="min-w-3xl"
      >
        <tr>
          <td>Alpha</td>
        </tr>
      </OperationalTable>,
    );

    expect(screen.getByRole('table')).toHaveClass('min-w-3xl');
    expect(screen.getByRole('table')).not.toHaveClass('min-w-5xl');
  });
});
