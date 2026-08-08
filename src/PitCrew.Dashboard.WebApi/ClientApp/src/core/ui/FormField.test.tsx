import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

import { FormField } from '@/core/ui/FormField';

describe('FormField', () => {
  it('associates the visible label with its control via getByLabelText', () => {
    render(
      <FormField label="Search nodes">
        <input type="search" />
      </FormField>,
    );

    expect(screen.getByLabelText('Search nodes')).toBeInTheDocument();
  });

  it('renders an optional hint and associates it through aria-describedby', () => {
    render(
      <FormField hint="Matches display name or version" label="Search nodes">
        <input type="search" />
      </FormField>,
    );

    const hint = screen.getByText('Matches display name or version');
    const input = screen.getByLabelText('Search nodes');
    expect(hint).toHaveAttribute('id');
    expect(input.getAttribute('aria-describedby')).toContain(hint.id);
  });

  it('announces an error as an alert, marks the control invalid, and associates the error text', () => {
    render(
      <FormField error="Required" label="Search nodes">
        <input type="search" />
      </FormField>,
    );

    const error = screen.getByRole('alert');
    const input = screen.getByLabelText('Search nodes');
    expect(error).toHaveTextContent('Required');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input.getAttribute('aria-describedby')).toContain(error.id);
  });

  it('combines hint and error ids and preserves a caller-supplied aria-describedby', () => {
    render(
      <FormField error="Required" hint="Matches display name or version" label="Search nodes">
        <input aria-describedby="caller-hint" type="search" />
      </FormField>,
    );

    const hint = screen.getByText('Matches display name or version');
    const error = screen.getByRole('alert');
    const input = screen.getByLabelText('Search nodes');
    const describedBy = input.getAttribute('aria-describedby');
    expect(describedBy).toContain(hint.id);
    expect(describedBy).toContain(error.id);
    expect(describedBy).toContain('caller-hint');
  });

  it('preserves a caller-supplied aria-invalid when there is no error', () => {
    render(
      <FormField label="Search nodes">
        <input aria-invalid="true" type="search" />
      </FormField>,
    );

    expect(screen.getByLabelText('Search nodes')).toHaveAttribute('aria-invalid', 'true');
  });
});
