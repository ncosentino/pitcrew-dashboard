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

  it('uses an injected clipboard implementation and renders a prefix', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    render(<CopyableId copyText={writeText} label="node ID" prefix="Node ID" value="node-1" />);

    await user.click(screen.getByRole('button', { name: 'Copy node ID' }));

    expect(writeText).toHaveBeenCalledWith('node-1');
    expect(screen.getByRole('status')).toHaveTextContent('node ID copied.');
    expect(screen.getByText('Node ID')).toBeInTheDocument();
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
