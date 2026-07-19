import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { ThemeToggle } from './ThemeToggle';
import { colorThemeStorageKey, initializeColorTheme } from './colorTheme';

const defaultMatchMedia = window.matchMedia;

function installMatchMedia(initialMatches: boolean) {
  let matches = initialMatches;
  const listeners = new Set<(event: MediaQueryListEvent) => void>();
  const mediaQuery = {
    get matches() {
      return matches;
    },
    media: '(prefers-color-scheme: dark)',
    onchange: null,
    addEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => {
      listeners.add(listener);
    },
    removeEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => {
      listeners.delete(listener);
    },
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => true,
  } as unknown as MediaQueryList;

  window.matchMedia = vi.fn(() => mediaQuery);

  return {
    setMatches(nextMatches: boolean) {
      matches = nextMatches;
      const event = { matches, media: mediaQuery.media } as MediaQueryListEvent;
      listeners.forEach((listener) => listener(event));
    },
  };
}

describe('ThemeToggle', () => {
  afterEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
    delete document.documentElement.dataset.theme;
    window.matchMedia = defaultMatchMedia;
    vi.restoreAllMocks();
  });

  it('initializes from a saved preference and persists toggles', async () => {
    installMatchMedia(false);
    localStorage.setItem(colorThemeStorageKey, 'dark');
    initializeColorTheme();
    const user = userEvent.setup();

    render(<ThemeToggle />);

    expect(document.documentElement).toHaveClass('dark');
    const toggle = screen.getByRole('button', { name: 'Use light mode' });
    expect(toggle).toHaveAttribute('aria-pressed', 'true');

    await user.click(toggle);

    expect(document.documentElement).not.toHaveClass('dark');
    expect(document.documentElement.dataset.theme).toBe('light');
    expect(localStorage.getItem(colorThemeStorageKey)).toBe('light');
    expect(screen.getByRole('button', { name: 'Use dark mode' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });

  it('tracks system changes until the operator chooses a theme', () => {
    const media = installMatchMedia(true);
    initializeColorTheme();

    render(<ThemeToggle />);

    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(localStorage.getItem(colorThemeStorageKey)).toBeNull();

    act(() => media.setMatches(false));

    expect(document.documentElement.dataset.theme).toBe('light');
    expect(screen.getByRole('button', { name: 'Use dark mode' })).toBeInTheDocument();
  });
});
