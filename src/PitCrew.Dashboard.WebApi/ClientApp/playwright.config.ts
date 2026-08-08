import { defineConfig, devices } from '@playwright/test';

const port = 4319;
const baseURL = `http://127.0.0.1:${port}`;

/**
 * Desktop uses a 1440x900 viewport, mobile is pinned to the issue's explicit
 * 390px width, and the intermediate viewport uses Tailwind's `md` breakpoint
 * (768px) so the three sizes exercise distinct responsive layout branches.
 */
export const viewports = {
  desktop: { width: 1440, height: 900 },
  intermediate: { width: 768, height: 1024 },
  mobile: { width: 390, height: 844 },
} as const;

export default defineConfig({
  testDir: './e2e',
  outputDir: './e2e/.artifacts/test-results',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  // Bounded worker count keeps mocked-network runs deterministic locally and in CI.
  workers: process.env.CI ? 2 : undefined,
  reporter: [
    ['list'],
    ['html', { outputFolder: './e2e/.artifacts/html-report', open: 'never' }],
    ['json', { outputFile: './e2e/.artifacts/results.json' }],
  ],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'off',
    video: 'off',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    // `--host 127.0.0.1` pins IPv4 loopback: some environments resolve Vite's
    // default `localhost` bind to IPv6-only (`::1`), which then never answers
    // Playwright's IPv4 `baseURL` health check and times out.
    command: `npm run dev -- --port ${port} --strictPort --host 127.0.0.1`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});
