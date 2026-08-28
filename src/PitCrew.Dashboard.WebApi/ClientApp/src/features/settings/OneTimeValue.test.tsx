import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { OneTimeValue } from './OneTimeValue';

describe('OneTimeValue', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('focuses, copies, and clears a newly issued value', async () => {
    const onClear = vi.fn();
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);

    render(
      <OneTimeValue
        title="Enrollment code ready"
        value="one-time-value"
        description="Shown once."
        onClear={onClear}
      />,
    );

    const result = screen.getByRole('region', { name: 'Enrollment code ready' });
    await waitFor(() => expect(result).toHaveFocus());

    await user.click(screen.getByRole('button', { name: 'Copy value' }));

    expect(writeText).toHaveBeenCalledWith('one-time-value');
    expect(screen.getByRole('button', { name: 'Copied' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear one-time value' }));
    const dialog = screen.getByRole('alertdialog', { name: 'Clear this one-time value?' });
    expect(dialog).toHaveTextContent('This does not revoke or invalidate the issued value.');
    await user.click(within(dialog).getByRole('button', { name: 'Clear value' }));
    expect(onClear).toHaveBeenCalledOnce();
  });

  it('surfaces clipboard failure without clearing the value', async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(
      new Error('Clipboard unavailable'),
    );

    render(
      <OneTimeValue
        title="Diagnostic credential ready"
        value="credential-value"
        description="Shown once."
        onClear={vi.fn()}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Copy value' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Copy failed. Select the value manually before clearing it.',
    );
    expect(screen.getByText('credential-value')).toBeInTheDocument();
  });
});
