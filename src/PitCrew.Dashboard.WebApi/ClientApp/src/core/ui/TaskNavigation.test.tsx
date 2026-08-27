import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { TaskNavigation } from './TaskNavigation';

describe('TaskNavigation', () => {
  it('identifies the active task and renders bounded counts', () => {
    render(
      <MemoryRouter initialEntries={['/support/sessions']}>
        <TaskNavigation
          label="Support tasks"
          items={[
            { label: 'Run diagnostic', description: 'Collect evidence', path: '/support/run' },
            {
              label: 'Sessions',
              description: 'Investigate requests',
              path: '/support/sessions',
              badge: '2',
            },
          ]}
        />
      </MemoryRouter>,
    );

    expect(screen.getByRole('navigation', { name: 'Support tasks' })).toBeVisible();
    expect(screen.getByRole('link', { name: /Sessions/ })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByText('2')).toBeVisible();
  });
});
