import { adminSession, removeAdminTokens } from '../../utils/admin-session'
import { getIdentityCsrfContext, appendUpstreamSetCookies, getUpstreamSetCookies } from '../../utils/identity-antiforgery'

export default defineEventHandler(async event => {
  setHeader(event, 'cache-control', 'no-store')
  const method = getMethod(event)
  const write = method !== 'GET'
  if (method !== 'GET' && method !== 'POST') throw createError({ statusCode: 405 })
  const { tokens, access } = await adminSession(event, write)
  const path = getRouterParam(event, 'path') ?? ''
  if (path === 'session' && !write) return { ...access, csrfToken: tokens.csrf }
  if (path === 'logout' && write) {
    const session = await getUserSession(event)
    let identityFailed = false
    try {
      const baseURL = useRuntimeConfig(event).gatewayBaseUrl
      const browserCookie = getHeader(event, 'cookie')
      const csrf = await getIdentityCsrfContext(event, baseURL, crypto.randomUUID(), browserCookie)
      const response = await $fetch.raw('/api/v1/auth/session/logout', {
        baseURL, method: 'POST', timeout: 5_000, retry: 0,
        headers: { Cookie: [browserCookie, csrf.cookieHeader].filter(Boolean).join('; '), [csrf.headerName]: csrf.token },
      })
      appendUpstreamSetCookies(event, getUpstreamSetCookies(response.headers))
    } catch { identityFailed = true }
    finally { await removeAdminTokens(session.id); await clearUserSession(event) }
    if (identityFailed) throw createError({ statusCode: 502, statusMessage: '后台会话已清除，但身份服务退出失败。' })
    return { success: true }
  }
  const content = /^(posts|comments)(\/[0-9a-f-]{36}(\/(hide|restore))?)?$/.test(path)
  const users = /^users(\/[0-9a-f-]{36}\/restrictions)?$/.test(path)
  const audit = path === 'content-audit' || path === 'user-audit' || path === 'audit'
  if (!content && !users && !audit) throw createError({ statusCode: 404 })
  const permission = content ? 'content.moderate' : users ? 'users.restrict' : 'audit.read'
  if (!access.permissions.includes(permission)) throw createError({ statusCode: 403 })
  let body: Record<string, unknown> | undefined
  if (write) {
    const raw = await readRawBody(event)
    if (!raw || Buffer.byteLength(raw) > 8192) throw createError({ statusCode: 400 })
    try { body = JSON.parse(raw) } catch { throw createError({ statusCode: 400 }) }
  }
  try {
    return await $fetch(`/api/v1/admin/${path}`, {
      baseURL: useRuntimeConfig(event).gatewayBaseUrl,
      method: method as 'GET' | 'POST', headers: { Authorization: `Bearer ${tokens.accessToken}`,
        ...(write ? { 'Idempotency-Key': getHeader(event, 'idempotency-key') ?? crypto.randomUUID() } : {}) },
      query: getQuery(event), body, timeout: 15_000, retry: 0
    })
  } catch (error) {
    const statusCode = (error as { statusCode?: number }).statusCode ?? 502
    throw createError({ statusCode, statusMessage: statusCode === 409 ? '记录已变更，请刷新后重试。' : '管理请求失败。' })
  }
})
