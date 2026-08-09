import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { CopyableId } from './CopyableId';

describe('CopyableId', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('displays the identifier value', () => {
    render(<CopyableId value="abc-123" label="tenant ID" />);
    expect(screen.getByTestId('copyable-id-value')).toHaveTextContent('abc-123');
  });

  it('provides a labeled copy button', () => {
    render(<CopyableId value="abc-123" label="tenant ID" />);
    expect(screen.getByRole('button', { name: 'Copy tenant ID' })).toBeInTheDocument();
  });

  it('announces when clipboard access is unavailable', async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(
      new DOMException('Clipboard unavailable', 'NotAllowedError'),
    );
    render(<CopyableId value="abc-123" label="tenant ID" />);

    const button = screen.getByRole('button', { name: 'Copy tenant ID' });
    await user.click(button);

    expect(screen.getByTestId('copyable-id-value')).toHaveTextContent('abc-123');
    expect(screen.getByRole('status')).toHaveTextContent(
      'Copy unavailable. Select tenant ID manually.',
    );
  });
});
