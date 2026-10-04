export interface CsrfResponse {
    token: string
    headerName: string
}

export interface RegisterRequest {
    email: string
    password: string
    displayName: string | null
    familyName: string | null
}

export interface RegisterResponse {
    id: string
    email: string
    displayName: string | null
    familyName: string | null
    createdAtUtc: string
}

export interface LoginRequest {
    email: string
    password: string
}