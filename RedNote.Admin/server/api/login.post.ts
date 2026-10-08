import { checkAdminOrigin } from '../utils/admin-session'
import { appendUpstreamSetCookies, getIdentityCsrfContext, getUpstreamSetCookies } from '../utils/identity-antiforgery'

export default defineEventHandler(async event => {
  checkAdminOrigin(event)
  const raw = await readRawBody(event)
  if (!raw || Buffer.byteLength(raw) > 8192) throw createError({ statusCode: 400, statusMessage: 'Invalid login request.' })
  let body: { email?: string; password?: string; code?: string }
  try { body = JSON.parse(raw) } catch { throw createError({ statusCode: 400 }) }
  if (!body || typeof body !== 'object' || typeof body.email !== 'string' || body.email.length > 254 || typeof body.password !== 'string' || body.password.length > 256
    || typeof body.code !== 'string' || !/^\d{6}$/.test(body.code)) throw createError({ statusCode: 400, statusMessage: 'Provide email, password and a six-digit authenticator code.' })
  const config = useRuntimeConfig(event)
  const csrf = await getIdentityCsrfContext(event, config.gatewayBaseUrl, crypto.randomUUID())
  try {
    const response = await $fetch.raw('/api/v1/auth/admin/login', {
      baseURL: config.gatewayBaseUrl, method: 'POST', body,
      headers: { Cookie: csrf.cookieHeader, [csrf.headerName]: csrf.token }, retry: 0, timeout: 10_000
    })
    appendUpstreamSetCookies(event, getUpstreamSetCookies(response.headers))
    return { success: true }
  } catch { throw createError({ statusCode: 401, statusMessage: 'Administrator credentials or authenticator code are invalid.' }) }
})
