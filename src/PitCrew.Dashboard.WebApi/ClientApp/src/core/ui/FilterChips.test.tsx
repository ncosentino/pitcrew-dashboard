import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { FilterChips } from '@/core/ui/FilterChips';

describe('FilterChips', () => {
  it('renders the result summary, chips, remove actions, and clear-all action', async () => {
    const onRemove = vi.fn();
    const onClearAll = vi.fn();
    const user = userEvent.setup();

    render(
      <FilterChips
        chips={[{ key: 'node', label: 'Node', value: 'Alpha', onRemove }]}
        resultSummary="Showing 1 of 2 slots"
        onClearAll={onClearAll}
      />,
    );

    expect(screen.getByText('Showing 1 of 2 slots')).toBeInTheDocument();
    expect(screen.getByText('Node:')).toBeInTheDocument();
    expect(screen.getByText('Alpha')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Remove filter Node: Alpha' }));
    expect(onRemove).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole('button', { name: 'Clear all filters' }));
    expect(onClearAll).toHaveBeenCalledTimes(1);
  });

  it('renders only the result summary when no chips are active', () => {
    render(<FilterChips chips={[]} resultSummary="Showing 2 of 2 slots" />);

    expect(screen.getByText('Showing 2 of 2 slots')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Clear all filters' })).not.toBeInTheDocument();
  });
});
