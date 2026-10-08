import { defineConfig, devices } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { getCACertificates, setDefaultCACertificates } from 'node:tls'

if (process.env.NODE_EXTRA_CA_CERTS) {
  setDefaultCACertificates([...getCACertificates('default'), readFileSync(process.env.NODE_EXTRA_CA_CERTS, 'utf8')])
}

export default defineConfig({
  testDir: './tests/e2e', testMatch: /admin\.spec\.ts/,
  timeout: 300_000, workers: 1, reporter: 'list',
  use: { baseURL: process.env.REDNOTE_GATEWAY_URL ?? 'https://localhost:8443', trace: 'off', ignoreHTTPSErrors: false },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
