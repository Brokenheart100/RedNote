import { removeAdminTokens, storeAdminTokens } from '../../utils/admin-session'
import type { AdminAccess } from '../../utils/admin-session'
import { eventLogger } from '../../utils/server-logger'

export default defineEventHandler(async event => {
  const config = useRuntimeConfig(event)
  // nuxt-auth-utils uses fixed transient cookie names. Namespace only these cookies
  // so simultaneous user/admin OIDC flows on the development host cannot collide.
  event.node.req.headers.cookie = (event.node.req.headers.cookie ?? '').split(';').map(value => value.trim())
    .filter(value => !value.startsWith('nuxt-auth-')).map(value => value.replace(/^rednote-admin-nuxt-auth-/, 'nuxt-auth-')).join('; ')
  // sendRedirect writes headers immediately. Rewrite cookies when they are set,
  // before the response is sent, rather than in a finally block afterwards.
  const response = event.node.res
  const originalSetHeader = response.setHeader
  const originalAppendHeader = response.appendHeader
  function rewriteCookies(name: string, value: string | number | readonly string[]) {
    if (name.toLowerCase() === 'set-cookie') {
      const cookies = Array.isArray(value) ? value.map(String) : [String(value)]
      value = cookies.map(cookie => cookie.startsWith('nuxt-auth-')
        ? cookie.replace(/^nuxt-auth-/, 'rednote-admin-nuxt-auth-').replace(/; Path=\//i, '; Path=/admin/').replace(/; Secure/gi, '')
        + (config.session.cookie && config.session.cookie.secure ? '; Secure' : '') : cookie)
    }
    return value
  }
  response.setHeader = function (name, value) {
    return originalSetHeader.call(this, name, rewriteCookies(name, value))
  }
  response.appendHeader = function (name, value) {
    const rewritten = rewriteCookies(name, value)
    return originalAppendHeader.call(this, name, typeof rewritten === 'number' ? String(rewritten) : rewritten)
  }
  try {
    return await defineOAuthOidcEventHandler({
      config: {
        scope: ['openid', 'profile', 'email', 'offline_access', 'rednote-api', 'rednote-admin'],
        openidConfig: {
          authorization_endpoint: new URL('/connect/authorize', config.public.adminBaseUrl).href,
          token_endpoint: new URL('/connect/token', config.gatewayBaseUrl).href
        }
      },
      async onSuccess(event, { tokens }) {
        if (!tokens.access_token) throw createError({ statusCode: 502 })
        await $fetch<AdminAccess>('/api/v1/auth/admin/access', {
          baseURL: config.gatewayBaseUrl,
          headers: { Authorization: `Bearer ${tokens.access_token}` }, retry: 0, timeout: 5_000
        })
        const previous = await getUserSession(event)
        await replaceUserSession(event, { user: { authenticated: true }, loggedInAt: Date.now() })
        const session = await getUserSession(event)
        await storeAdminTokens(session.id, {
          accessToken: tokens.access_token, refreshToken: tokens.refresh_token,
          expiresAt: Date.now() + (tokens.expires_in ?? 300) * 1000, absoluteExpiry: Date.now() + 8 * 60 * 60 * 1000,
          lastUsed: Date.now(), csrf: crypto.randomUUID()
        })
        if (previous.id) await removeAdminTokens(previous.id)
        return sendRedirect(event, '/admin/', 302)
      },
      onError(event, error) {
        eventLogger(event).error('Admin OIDC authentication failed.', {
          statusCode: error.statusCode ?? 502,
          reason: error.message?.startsWith('Missing') ? 'Missing provider configuration' : error.message?.includes('nonce') ? 'Nonce mismatch' : error.message?.includes('state') ? 'State mismatch' : 'Identity provider rejected authentication'
        })
        return sendRedirect(event, '/admin/login?error=oidc', 302)
      },
    })(event)
  } finally {
    response.setHeader = originalSetHeader
    response.appendHeader = originalAppendHeader
  }
})
