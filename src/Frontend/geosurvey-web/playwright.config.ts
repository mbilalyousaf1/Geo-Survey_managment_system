import { defineConfig } from '@playwright/test';

// Credential-bearing flows must never produce DOM snapshots, traces, or videos.
process.env['PLAYWRIGHT_NO_COPY_PROMPT'] = '1';
delete process.env['DEBUG'];
delete process.env['PWDEBUG'];

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 420_000,
  expect: { timeout: 25_000 },
  reporter: [['./e2e/safe-reporter.ts']],
  outputDir: './test-results/transient',
  preserveOutput: 'never',
  use: {
    browserName: 'chromium',
    channel: process.env['GEOSURVEY_BROWSER_CHANNEL'] || undefined,
    ignoreHTTPSErrors: true,
    trace: 'off',
    video: 'off',
    screenshot: 'off',
    viewport: { width: 1365, height: 900 },
  },
});
