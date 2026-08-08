import { useState } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { ConfirmationSummary } from '@/core/ui/ConfirmationSummary';

describe('ConfirmationSummary', () => {
  it('renders identity, effects, and prohibited effects', () => {
    render(
      <ConfirmationSummary
        identity={[{ label: 'Node', value: 'Alpha' }]}
        effects={['The connector will stop synchronizing until it re-enrolls.']}
        prohibitedEffects={['No profile or capacity configuration is changed.']}
      />,
    );

    expect(screen.getByText('Node')).toBeInTheDocument();
    expect(screen.getByText('Alpha')).toBeInTheDocument();
    expect(screen.getByText('What will happen')).toBeInTheDocument();
    expect(
      screen.getByText('The connector will stop synchronizing until it re-enrolls.'),
    ).toBeInTheDocument();
    expect(screen.getByText('What will not happen')).toBeInTheDocument();
    expect(
      screen.getByText('No profile or capacity configuration is changed.'),
    ).toBeInTheDocument();
  });

  it('renders fenced preconditions in their own evidence grid', () => {
    render(
      <ConfirmationSummary
        identity={[{ label: 'Node', value: 'Alpha' }]}
        effects={['Restarts the profile manager exactly once.']}
        fences={[{ label: 'Expected generation', value: '12' }]}
      />,
    );

    expect(screen.getByText('Expected fences')).toBeInTheDocument();
    expect(screen.getByText('Expected generation')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
  });

  it('gates on an explicit acknowledgement when the caller requires one', async () => {
    const user = userEvent.setup();
    const onCheckedChange = vi.fn();

    function Harness() {
      const [checked, setChecked] = useState(false);
      return (
        <ConfirmationSummary
          identity={[{ label: 'Node', value: 'Alpha' }]}
          effects={['Restarts the profile manager exactly once.']}
          acknowledgement={{
            label: 'I confirm this recovery.',
            checked,
            onCheckedChange: (next) => {
              setChecked(next);
              onCheckedChange(next);
            },
            testId: 'acknowledgement-checkbox',
          }}
        />
      );
    }

    render(<Harness />);

    const checkbox = screen.getByRole('checkbox', { name: 'I confirm this recovery.' });
    expect(checkbox).not.toBeChecked();
    await user.click(checkbox);
    expect(onCheckedChange).toHaveBeenCalledWith(true);
    expect(checkbox).toBeChecked();
  });

  it('omits the fences grid and acknowledgement when the caller does not supply them', () => {
    render(
      <ConfirmationSummary
        identity={[{ label: 'Node', value: 'Alpha' }]}
        effects={['The connector will stop synchronizing.']}
      />,
    );

    expect(screen.queryByText('Expected fences')).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });
});
