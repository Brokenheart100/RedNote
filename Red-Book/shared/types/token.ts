export interface AuthTokenSet {
    accessToken: string
    refreshToken?: string
    tokenType: string
    expiresAt?: number
}
