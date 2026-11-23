import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vitest/config';

// One Vitest run for every workspace. Kept apart from apps/web/vite.config.ts
// so tests do not load the build-only plugins.
export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./apps/web/src', import.meta.url))
    }
  },
  define: {
    'import.meta.env.APP_VERSION': JSON.stringify('test'),
    'import.meta.env.BUILD_TIMESTAMP': JSON.stringify('test')
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./apps/web/src/test/setup.ts'],
    include: ['packages/*/src/**/*.test.{ts,tsx}', 'apps/*/src/**/*.test.{ts,tsx}'],
    restoreMocks: true
  }
});
