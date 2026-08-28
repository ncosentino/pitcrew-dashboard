import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { TaskWorkspace } from './TaskWorkspace';

describe('TaskWorkspace', () => {
  it('renders navigation before the focused work region', () => {
    render(
      <MemoryRouter initialEntries={['/node/overview']}>
        <TaskWorkspace
          navigationLabel="Node tasks"
          navigationItems={[
            {
              label: 'Overview',
              description: 'Current readiness',
              path: '/node/overview',
            },
          ]}
        >
          <section aria-label="Focused evidence">Evidence</section>
        </TaskWorkspace>
      </MemoryRouter>,
    );

    const navigation = screen.getByRole('navigation', { name: 'Node tasks' });
    const evidence = screen.getByRole('region', { name: 'Focused evidence' });
    expect(
      navigation.compareDocumentPosition(evidence) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });
});
