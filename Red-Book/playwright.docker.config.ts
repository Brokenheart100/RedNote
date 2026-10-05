import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
    testDir: './tests/e2e',
    testMatch: /docker-deployment\.spec\.ts/,
    timeout: 120_000,
    workers: 1,
    reporter: 'list',
    use: {
        baseURL: process.env.REDNOTE_FRONTEND_URL ?? 'http://localhost:3000',
        trace: 'off',
    },
    projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
