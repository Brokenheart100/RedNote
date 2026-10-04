interface DevelopmentLogin {
    email: string
    password: string
}

const DEVELOPMENT_LOGIN_KEY = 'rednote:development-login'

export function createAuthRoute(
    path: string,
    returnUrl: string | null,
) {
    if (!returnUrl) {
        return {
            path,
        }
    }

    return {
        path,
        query: {
            returnUrl,
        },
    }
}

export function resolveReturnUrl(
    value: unknown,
): string | null {
    if (typeof value !== 'string') {
        return null
    }

    if (!value.startsWith('/')) {
        return null
    }

    if (value.startsWith('//')) {
        return null
    }

    return value
}

export function createDevelopmentRandomText(
    length: number,
): string {
    const characters = 'abcdefghijklmnopqrstuvwxyz0123456789'

    return Array.from(
        {
            length,
        },
        () => {
            const index = Math.floor(
                Math.random() * characters.length,
            )

            return characters[index] ?? ''
        },
    ).join('')
}

export function storeDevelopmentLogin(
    login: DevelopmentLogin,
): void {
    if (!import.meta.dev) {
        return
    }

    sessionStorage.setItem(
        DEVELOPMENT_LOGIN_KEY,
        JSON.stringify(login),
    )
}

export function consumeDevelopmentLogin(): DevelopmentLogin | null {
    if (!import.meta.dev) {
        return null
    }

    const value = sessionStorage.getItem(
        DEVELOPMENT_LOGIN_KEY,
    )

    if (!value) {
        return null
    }

    sessionStorage.removeItem(
        DEVELOPMENT_LOGIN_KEY,
    )

    try {
        const parsed = JSON.parse(
            value,
        ) as Partial<DevelopmentLogin>

        if (
            typeof parsed.email !== 'string'
            || typeof parsed.password !== 'string'
        ) {
            return null
        }

        return {
            email: parsed.email,
            password: parsed.password,
        }
    }
    catch {
        return null
    }
}