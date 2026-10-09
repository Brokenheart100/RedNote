import type { H3Event } from 'h3'
import { getAuthRedis } from './auth-redis'

export interface AdminAccess { userId: string; email?: string; roles: string[]; permissions: string[] }
export interface AdminTokens { accessToken: string; refreshToken?: string; expiresAt: number; absoluteExpiry: number; lastUsed: number; csrf: string }
const key = (id: string) => `rednote:admin:session:${id}`

export async function storeAdminTokens(id: string, tokens: AdminTokens) {
  await getAuthRedis().set(key(id), JSON.stringify(tokens), 'EX', Math.max(1, Math.ceil((tokens.absoluteExpiry - Date.now()) / 1000)))
}
export async function removeAdminTokens(id: string) { await getAuthRedis().del(key(id)) }

export function checkAdminOrigin(event: H3Event) {
  const config = useRuntimeConfig(event)
  const allowed = new URL(config.public.adminBaseUrl).origin
  const origin = getHeader(event, 'origin')
  // Codespaces rewrites this origin to its local HTTPS forwarding target.
  // Only the explicitly configured development alias is accepted; production remains strict.
  const fromCodespaces = import.meta.dev && config.codespacesProxyOrigin
    && origin === config.codespacesProxyOrigin
  if (origin !== allowed && !fromCodespaces) throw createError({ statusCode: 403, statusMessage: 'Invalid request origin.' })
}

export async function adminSession(event: H3Event, write = false): Promise<{ tokens: AdminTokens; access: AdminAccess }> {
  const session = await requireUserSession(event)
  const redis = getAuthRedis()
  const raw = await redis.get(key(session.id))
  if (!raw) throw createError({ statusCode: 401, statusMessage: 'Admin session expired.' })
  let tokens = JSON.parse(raw) as AdminTokens
  if (tokens.absoluteExpiry <= Date.now() || tokens.lastUsed < Date.now() - 30 * 60 * 1000) {
    await removeAdminTokens(session.id); await clearUserSession(event)
    throw createError({ statusCode: 401, statusMessage: 'Admin session expired.' })
  }
  if (write) {
    checkAdminOrigin(event)
    if (getHeader(event, 'x-admin-csrf') !== tokens.csrf) throw createError({ statusCode: 403, statusMessage: 'CSRF verification failed.' })
  }
  if (tokens.expiresAt <= Date.now() + 15_000) tokens = await refresh(event, session.id)
  const config = useRuntimeConfig(event)
  let access: AdminAccess
  try {
    access = await $fetch<AdminAccess>('/api/v1/auth/admin/access', {
      baseURL: config.gatewayBaseUrl, headers: { Authorization: `Bearer ${tokens.accessToken}` }, retry: 0, timeout: 5_000,
    })
  } catch (error) {
    const status = (error as { statusCode?: number }).statusCode
    if (status === 401 || status === 403) {
      await removeAdminTokens(session.id); await clearUserSession(event)
      throw createError({ statusCode: 401, statusMessage: 'Admin access revoked.' })
    }
    throw createError({ statusCode: 503, statusMessage: 'Authorization service unavailable.' })
  }
  // Touch only the current record; logout and concurrent refresh must never be overwritten.
  const touched = { ...tokens, lastUsed: Date.now() }
  await redis.eval("if redis.call('EXISTS',KEYS[2]) == 0 and redis.call('GET',KEYS[1]) == ARGV[1] then redis.call('SET',KEYS[1],ARGV[2],'KEEPTTL'); return 1 end return 0",
    2, key(session.id), `${key(session.id)}:refresh`, JSON.stringify(tokens), JSON.stringify(touched))
  return { tokens, access }
}

async function refresh(event: H3Event, id: string): Promise<AdminTokens> {
  const redis = getAuthRedis(); const lock = `${key(id)}:refresh`; const owner = crypto.randomUUID()
  for (let attempt = 0; attempt < 60; attempt++) {
    const currentRaw = await redis.get(key(id))
    if (!currentRaw) throw createError({ statusCode: 401, statusMessage: 'Admin session expired.' })
    const current = JSON.parse(currentRaw) as AdminTokens
    if (current.expiresAt > Date.now() + 15_000) return current
    if (await redis.set(lock, owner, 'PX', 15_000, 'NX')) {
      try {
        if (!current.refreshToken) throw createError({ statusCode: 401 })
        const config = useRuntimeConfig(event)
        const response = await $fetch<{ access_token: string; refresh_token?: string; expires_in: number }>('/connect/token', {
          baseURL: config.gatewayBaseUrl, method: 'POST', retry: 0, timeout: 5_000,
          body: new URLSearchParams({ grant_type: 'refresh_token', refresh_token: current.refreshToken,
            client_id: config.oauth.oidc.clientId, client_secret: config.oauth.oidc.clientSecret }),
        })
        const next = { ...current, accessToken: response.access_token, refreshToken: response.refresh_token ?? current.refreshToken,
          expiresAt: Date.now() + response.expires_in * 1000, lastUsed: Date.now() }
        const replaced = await redis.eval("if redis.call('GET',KEYS[1]) == ARGV[1] and redis.call('GET',KEYS[2]) == ARGV[2] then redis.call('SET',KEYS[1],ARGV[3],'KEEPTTL'); return 1 end return 0",
          2, key(id), lock, currentRaw, owner, JSON.stringify(next))
        if (replaced !== 1) throw createError({ statusCode: 401, statusMessage: 'Admin session changed.' })
        return next
      } catch (error) {
        const status = (error as { statusCode?: number }).statusCode
        if (status === 400 || status === 401 || status === 403) {
          await removeAdminTokens(id); await clearUserSession(event)
          throw createError({ statusCode: 401, statusMessage: 'Admin authorization expired.' })
        }
        throw createError({ statusCode: 503, statusMessage: 'Session refresh unavailable.' })
      } finally {
        await redis.eval("if redis.call('GET',KEYS[1]) == ARGV[1] then redis.call('DEL',KEYS[1]) end", 1, lock, owner)
      }
    }
    await new Promise(resolve => setTimeout(resolve, 100))
  }
  throw createError({ statusCode: 503, statusMessage: 'Session refresh is busy.' })
}
