import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { OperationalList, OperationalRow } from './OperationalList';

describe('OperationalList', () => {
  it('keeps record identity and action in one named list', () => {
    render(
      <OperationalList label="Support nodes">
        <OperationalRow
          title="Build host"
          description="Last poll unavailable"
          status={<span>Active</span>}
          actions={<button type="button">View details</button>}
        />
      </OperationalList>,
    );

    expect(screen.getByRole('list', { name: 'Support nodes' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Build host' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'View details' })).toBeVisible();
  });
});
