import type { AdminSession } from '../composables/useAdminApi'
export default defineNuxtRouteMiddleware(async to => {
  if (to.path === '/login') return
  const session = useState<AdminSession | null>('admin-access', () => null)
  try { session.value = await useRequestFetch()<AdminSession>(`${useRuntimeConfig().app.baseURL}api/manage/session`) }
  catch (error) {
    session.value = null; const status = (error as { statusCode?: number }).statusCode
    if (status === 401 || status === 403) return navigateTo('/login')
    throw createError({ statusCode: 503, statusMessage: '管理权限暂时无法确认，请稍后重试。' })
  }
})
