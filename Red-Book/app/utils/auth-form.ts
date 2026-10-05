export interface DevelopmentLogin {
    email: string
    password: string
}

const DEVELOPMENT_LOGIN_STORAGE_KEY =
    'rednote:development-login'

export function resolveReturnUrl(value: unknown): string | null {
    return typeof value === 'string'
        ? value
        : null
}

export function createAuthRoute(
    path: '/login' | '/register',
    returnUrl: string | null,
) {
    return returnUrl
        ? {
            path,
            query: {
                returnUrl,
            },
        }
        : {
            path,
        }
}

export function storeDevelopmentLogin(
    login: DevelopmentLogin,
): void {
    if (!import.meta.dev || !import.meta.client) {
        return
    }

    try {
        sessionStorage.setItem(
            DEVELOPMENT_LOGIN_STORAGE_KEY,
            JSON.stringify(login),
        )
    }
    catch {
        // 开发辅助数据写入失败不能影响正常注册流程。
    }
}

export function consumeDevelopmentLogin():
    DevelopmentLogin | null {
    if (!import.meta.dev || !import.meta.client) {
        return null
    }

    let value: string | null

    try {
        value = sessionStorage.getItem(
            DEVELOPMENT_LOGIN_STORAGE_KEY,
        )

        sessionStorage.removeItem(
            DEVELOPMENT_LOGIN_STORAGE_KEY,
        )
    }
    catch {
        return null
    }

    if (!value) {
        return null
    }

    try {
        const data = JSON.parse(value) as Partial<DevelopmentLogin>

        if (
            typeof data.email !== 'string'
            || !data.email
            || typeof data.password !== 'string'
            || !data.password
        ) {
            return null
        }

        return {
            email: data.email,
            password: data.password,
        }
    }
    catch {
        return null
    }
}

export function createDevelopmentRandomText(
    length: number,
): string {
    const characters = 'abcdefghijklmnopqrstuvwxyz0123456789'

    return Array.from(
        { length },
        () => {
            const index = Math.floor(
                Math.random() * characters.length,
            )

            return characters[index]
        },
    ).join('')
}
