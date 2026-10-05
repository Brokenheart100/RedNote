import { defineConfig, devices } from '@playwright/test'

const live = process.env.REDNOTE_LIVE_E2E === '1'
const preview = process.env.REDNOTE_E2E_PREVIEW === '1'
const baseURL = process.env.REDNOTE_FRONTEND_URL ?? 'http://localhost:3000'
const frontendPort = new URL(baseURL).port || '3000'

export default defineConfig({
    testDir: './tests/e2e',
    testMatch: live ? /auth-.*\.spec\.ts/ : /smoke\.spec\.ts/,
    fullyParallel: !live,
    forbidOnly: Boolean(process.env.CI),
    retries: process.env.CI ? 1 : 0,
    reporter: [['list'], ['html', { open: 'never' }]],
    use: {
        baseURL,
        trace: 'on-first-retry',
        screenshot: 'only-on-failure',
        video: 'retain-on-failure',
    },
    projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
    webServer: live ? undefined : [
        {
            command: 'node tests/e2e/mock-gateway.mjs',
            url: 'http://localhost:4010/health',
            reuseExistingServer: false,
        },
        {
            command: preview ? 'node .output/server/index.mjs' : `npm run dev -- --host 127.0.0.1 --port ${frontendPort}`,
            url: `${baseURL}/login`,
            timeout: 120_000,
            reuseExistingServer: false,
            env: {
                NITRO_HOST: '127.0.0.1',
                NITRO_PORT: frontendPort,
                NUXT_SESSION_PASSWORD: 'frontend-test-session-password-12345678901234567890',
                NUXT_GATEWAY_BASE_URL: 'http://127.0.0.1:4010',
            },
        },
    ],
})
