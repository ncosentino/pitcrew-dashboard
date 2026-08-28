import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { TaskNavigation } from './TaskNavigation';

const originalScrollIntoView = Object.getOwnPropertyDescriptor(
  HTMLElement.prototype,
  'scrollIntoView',
);

describe('TaskNavigation', () => {
  afterEach(() => {
    if (originalScrollIntoView) {
      Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', originalScrollIntoView);
    } else {
      Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView');
    }
  });

  it('identifies the active task and renders bounded counts', () => {
    const scrollIntoView = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: scrollIntoView,
    });
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
    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'nearest', inline: 'center' });
  });
});
