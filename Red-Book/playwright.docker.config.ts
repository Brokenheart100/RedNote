import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
    testDir: './tests/e2e',
    testMatch: /docker-deployment\.spec\.ts/,
    // Aspire HTTPS targets include Nuxt development compilation on first navigation.
    timeout: process.env.REDNOTE_FRONTEND_URL?.startsWith('https://') ? 300_000 : 120_000,
    workers: 1,
    reporter: 'list',
    use: {
        baseURL: process.env.REDNOTE_FRONTEND_URL ?? 'http://localhost:3000',
        trace: 'off',
    },
    projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
