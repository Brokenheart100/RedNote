declare module '#auth-utils' {
    interface User {
        authenticated: boolean
        provider: 'oidc'
    }

    interface SecureSessionData {
        accessToken: string

        refreshToken?: string

        tokenType: string

        expiresAt?: number
    }
}

export { }