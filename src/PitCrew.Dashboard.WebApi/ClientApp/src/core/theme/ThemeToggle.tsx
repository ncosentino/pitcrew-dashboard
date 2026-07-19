import { Button } from '@/components/ui/button';
import { useColorTheme } from './colorTheme';

/** Allows the current operator to persistently switch between light and dark themes. */
export function ThemeToggle() {
  const { theme, toggleTheme } = useColorTheme();
  const accessibleName = theme === 'dark' ? 'Use light mode' : 'Use dark mode';

  return (
    <Button
      type="button"
      variant="outline"
      size="icon"
      aria-label={accessibleName}
      aria-pressed={theme === 'dark'}
      title={accessibleName}
      onClick={toggleTheme}
    >
      {theme === 'dark' ? (
        <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor">
          <circle cx="12" cy="12" r="4" />
          <path d="M12 2v2M12 20v2M4.93 4.93l1.42 1.42M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.42-1.42M17.66 6.34l1.41-1.41" />
        </svg>
      ) : (
        <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor">
          <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79Z" />
        </svg>
      )}
    </Button>
  );
}
