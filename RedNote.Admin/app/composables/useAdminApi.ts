export interface AdminSession { userId: string; email?: string; roles: string[]; permissions: string[]; csrfToken: string }
export function useAdminApi() {
  const session = useState<AdminSession | null>('admin-access', () => null)
  const fetcher = useRequestFetch(); const base = useRuntimeConfig().app.baseURL
  async function request<T>(path: string, options: { method?: 'POST'; body?: object; query?: Record<string, string | number> } = {}) {
    return fetcher(`${base}api/manage/${path}`, { ...options, retry: 0, headers: options.method ? {
      'x-admin-csrf': session.value?.csrfToken ?? '', 'Idempotency-Key': crypto.randomUUID()
    } : undefined }) as Promise<T>
  }
  return { session, request }
}
