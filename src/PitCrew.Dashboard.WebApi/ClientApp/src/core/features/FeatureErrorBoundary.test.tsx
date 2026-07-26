import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { lazyFeature } from './lazyFeature';

describe('FeatureErrorBoundary', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('offers a real page reload when a lazy feature chunk cannot load', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const BrokenFeature = lazyFeature('broken', async () => {
      throw new Error('Chunk load failed.');
    });

    render(<BrokenFeature />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The broken feature could not be displayed.',
    );
    expect(screen.getByRole('link', { name: 'Reload page' })).toHaveAttribute(
      'href',
      globalThis.location.href,
    );
  });
});
