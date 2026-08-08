import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

import { FilterToolbar } from '@/core/ui/FilterToolbar';
import { FormField } from '@/core/ui/FormField';

describe('FilterToolbar', () => {
  it('renders each FormField child with its label intact', () => {
    render(
      <FilterToolbar>
        <FormField label="Search nodes">
          <input type="search" />
        </FormField>
        <FormField label="Density">
          <select>
            <option value="comfortable">Comfortable</option>
          </select>
        </FormField>
      </FilterToolbar>,
    );

    expect(screen.getByLabelText('Search nodes')).toBeInTheDocument();
    expect(screen.getByLabelText('Density')).toBeInTheDocument();
  });

  it('renders a plain, non-landmark container when no label is given', () => {
    const { container } = render(
      <FilterToolbar>
        <FormField label="Search nodes">
          <input type="search" />
        </FormField>
      </FilterToolbar>,
    );

    expect(screen.queryByRole('region')).not.toBeInTheDocument();
    expect(container.querySelector('section')).not.toBeInTheDocument();
    expect(container.querySelector('div')).toBeInTheDocument();
  });

  it('renders a labeled landmark when an accessible label is supplied', () => {
    render(
      <FilterToolbar label="Fleet filters">
        <FormField label="Search nodes">
          <input type="search" />
        </FormField>
      </FilterToolbar>,
    );

    expect(screen.getByRole('region', { name: 'Fleet filters' })).toBeInTheDocument();
  });
});
