import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// https://vite.dev/config/
// https://vitest.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  publicDir: fileURLToPath(new URL('../../../assets', import.meta.url)),
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/setupTests.ts',
    css: true,
    // Playwright owns e2e/**; Vitest's own suite must never pick up its
    // browser specs (they run under a different test runner/global API).
    exclude: ['**/node_modules/**', '**/dist/**', 'e2e/**'],
  },
});
