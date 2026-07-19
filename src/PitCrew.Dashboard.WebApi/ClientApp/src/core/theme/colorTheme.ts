import { useCallback, useEffect, useState } from 'react';

/** Color themes supported by the dashboard. */
export type ColorTheme = 'light' | 'dark';

/** Browser storage key for an operator's explicit color-theme preference. */
export const colorThemeStorageKey = 'pitcrew-dashboard-theme';

const darkThemeQuery = '(prefers-color-scheme: dark)';

function isColorTheme(value: string | null | undefined): value is ColorTheme {
  return value === 'light' || value === 'dark';
}

function readStoredTheme(): ColorTheme | null {
  try {
    const storedTheme = globalThis.localStorage.getItem(colorThemeStorageKey);
    return isColorTheme(storedTheme) ? storedTheme : null;
  } catch (error) {
    if (error instanceof DOMException) {
      console.warn('The dashboard could not read the saved color theme.', error);
      return null;
    }
    throw error;
  }
}

function getSystemTheme(): ColorTheme {
  return globalThis.matchMedia(darkThemeQuery).matches ? 'dark' : 'light';
}

function getAppliedTheme(): ColorTheme {
  const appliedTheme = document.documentElement.dataset.theme;
  return isColorTheme(appliedTheme) ? appliedTheme : (readStoredTheme() ?? getSystemTheme());
}

function applyTheme(theme: ColorTheme): void {
  const root = document.documentElement;
  root.dataset.theme = theme;
  root.classList.toggle('dark', theme === 'dark');

  const themeColor = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
  if (themeColor) {
    themeColor.content = theme === 'dark' ? '#071825' : '#f7fafb';
  }
}

function storeTheme(theme: ColorTheme): void {
  try {
    globalThis.localStorage.setItem(colorThemeStorageKey, theme);
  } catch (error) {
    if (error instanceof DOMException) {
      console.warn('The dashboard could not save the color theme.', error);
      return;
    }
    throw error;
  }
}

/** Applies the saved or system color theme before the React application renders. */
export function initializeColorTheme(): void {
  applyTheme(readStoredTheme() ?? getSystemTheme());
}

/** Tracks the applied color theme and provides an explicit light/dark toggle. */
export function useColorTheme(): {
  readonly theme: ColorTheme;
  readonly toggleTheme: () => void;
} {
  const [theme, setTheme] = useState<ColorTheme>(getAppliedTheme);

  useEffect(() => {
    const mediaQuery = globalThis.matchMedia(darkThemeQuery);
    const applySystemTheme = (event: MediaQueryListEvent) => {
      if (readStoredTheme() !== null) return;

      const nextTheme = event.matches ? 'dark' : 'light';
      applyTheme(nextTheme);
      setTheme(nextTheme);
    };

    mediaQuery.addEventListener('change', applySystemTheme);
    return () => mediaQuery.removeEventListener('change', applySystemTheme);
  }, []);

  const toggleTheme = useCallback(() => {
    const nextTheme = theme === 'dark' ? 'light' : 'dark';
    storeTheme(nextTheme);
    applyTheme(nextTheme);
    setTheme(nextTheme);
  }, [theme]);

  return { theme, toggleTheme };
}
