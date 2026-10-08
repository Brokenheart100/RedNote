import { createRequire } from 'node:module'

const telemetryApi = createRequire(import.meta.url).resolve('@opentelemetry/api')

export default defineNuxtConfig({
  compatibilityDate: '2026-09-01',

  modules: [
    '@nuxt/ui',
    '@nuxt/image',
    '@nuxt/content',
    'nuxt-auth-utils',
    '@pinia/nuxt',
    '@vueuse/nuxt',
  ],

  components: [
    {
      path: '~/components',
      pathPrefix: false,
    },
  ],
  image: {
    // Keep MinIO signatures and authenticated media redirects intact.
    provider: 'none',
  },
  icon: {
    serverBundle: {
      collections: ['lucide'],
    },
  },

  css: [
    '~/assets/css/main.css',
  ],

  devtools: {
    enabled: true,
  },

  nitro: {
    preset: 'node-server',
    // Resolve the API through Node's supported entry rather than Rollup's "module" condition.
    alias: {
      '@opentelemetry/api': telemetryApi,
    },
    // Keep Node telemetry SDKs as runtime dependencies instead of rebundling their ESM helpers.
    externals: {
      external: ['@opentelemetry/'],
    },
  },

  runtimeConfig: {
    gatewayBaseUrl: '',
    session: {
      maxAge:
        60 * 60 * 24 * 7,
      cookie: {
        sameSite: 'lax',
        secure: process.env.NODE_ENV === 'production',
      },
    },

    oauth: {
      oidc: {
        clientId: '',
        openidConfig: '',
        redirectURL: '',

        scope: [
          'openid',
          'profile',
          'email',
          'offline_access',
          'rednote-api',
        ],
      },
    },

    public: {
      apiBaseUrl: '',
      appName: 'RedNote',
    },
  },

  typescript: {
    typeCheck: true,
  },
  content: {
    experimental: {
      sqliteConnector: 'native',
    },
  },
})
