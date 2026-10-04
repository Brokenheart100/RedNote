import { getRedisConnectionOptions } from './config/redis'

const redis = getRedisConnectionOptions()

export default defineNuxtConfig({
  compatibilityDate: '2026-09-01',

  modules: [
    '@nuxt/ui',
    '@nuxt/content',
    'nuxt-auth-utils',
    '@pinia/nuxt',
  ],

  components: [
    {
      path: '~/components',
      pathPrefix: false,
    },
  ],
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

    // storage: {
    //   authTokens: {
    //     driver: 'redis',
    //     base: 'rednote:auth-tokens',

    //     host: redis.host,
    //     port: redis.port,

    //     ...(redis.username
    //       ? {
    //         username: redis.username,
    //       }
    //       : {}),

    //     ...(redis.password
    //       ? {
    //         password: redis.password,
    //       }
    //       : {}),

    //     ...(redis.tls
    //       ? {
    //         tls: redis.tls,
    //       }
    //       : {}),
    //   },
    // },
  },

  runtimeConfig: {
    gatewayBaseUrl: '',
    session: {
      maxAge:
        60 * 60 * 24 * 7,
      cookie: {
        sameSite: 'lax',
        secure: false,
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