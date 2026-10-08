import { createRequire } from 'node:module'

export default defineNuxtConfig({
  compatibilityDate: '2026-09-01',
  modules: ['@nuxt/ui', 'nuxt-auth-utils'],
  app: { baseURL: '/admin/', head: { title: 'RedNote 管理后台', htmlAttrs: { lang: 'zh-CN' } } },
  css: ['~/assets/main.css'],
  devtools: { enabled: false },
  icon: { serverBundle: { collections: ['lucide'] } },
  nitro: {
    preset: 'node-server',
    typescript: { tsConfig: { include: ['./types/nitro.d.ts'] } },
    alias: { '@opentelemetry/api': createRequire(import.meta.url).resolve('@opentelemetry/api') },
    externals: { external: ['@opentelemetry/'] },
  },
  runtimeConfig: {
    gatewayBaseUrl: '',
    session: { password: '', name: 'rednote-admin-session', maxAge: 8 * 60 * 60, cookie: { path: '/admin/', httpOnly: true, sameSite: 'lax', secure: true } },
    oauth: { oidc: { clientId: 'rednote-admin', clientSecret: '', openidConfig: '', redirectURL: '' } },
    public: { adminBaseUrl: '' },
  },
  typescript: { typeCheck: true },
})
