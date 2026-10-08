import { context, trace } from '@opentelemetry/api'
import { logs, SeverityNumber } from '@opentelemetry/api-logs'
import type { H3Event } from 'h3'

const sensitive = /^(password|confirmpassword|authorization|cookie|set-cookie|.*token.*|clientsecret|client_secret|code|state)$/i

export function sanitizeLogValue(value: unknown, depth = 0): unknown {
    if (depth > 6) return '[truncated]'
    if (typeof value === 'string' && (value.trim().startsWith('{') || value.trim().startsWith('['))) {
        try {
            return sanitizeLogValue(JSON.parse(value), depth)
        }
        catch { /* Non-JSON text stays readable. */ }
    }
    if (Array.isArray(value)) return value.slice(0, 50).map(item => sanitizeLogValue(item, depth + 1))
    if (value && typeof value === 'object') {
        return Object.fromEntries(Object.entries(value).map(([key, item]) => [
            key, sensitive.test(key) ? '[redacted]' : sanitizeLogValue(item, depth + 1),
        ]))
    }
    return value
}

export function formatLogMessage(message: string, fields: Record<string, unknown>, includeDetails: boolean): string {
    if (!includeDetails || Object.keys(fields).length === 0) return message
    return `${message}\n${JSON.stringify(fields, null, 2)}`
}

function write(level: 'info' | 'warn' | 'error', message: string, fields: Record<string, unknown> = {}, event?: H3Event) {
    if (!message.trim() || message.startsWith('━━━━━━━━')) return
    const sanitized = sanitizeLogValue(fields) as Record<string, unknown>
    const attributes: Record<string, string | number | boolean> = {}
    for (const [key, value] of Object.entries(sanitized)) {
        if (value === null || value === undefined) continue
        attributes[key] = typeof value === 'object' ? JSON.stringify(value) : String(value)
        if (typeof value === 'number' || typeof value === 'boolean') attributes[key] = value
    }
    const span = event?.context.telemetrySpan
    const logContext = span ? trace.setSpan(context.active(), span) : context.active()
    logs.getLogger('rednote.frontend').emit({
        severityNumber: { info: SeverityNumber.INFO, warn: SeverityNumber.WARN, error: SeverityNumber.ERROR }[level],
        severityText: level.toUpperCase(),
        body: formatLogMessage(message, sanitized, process.env.NODE_ENV === 'development'),
        attributes, context: logContext,
    })
    // Retain the emoji console view alongside OTLP structured logs.
    console[level](message, sanitized)
}

export function eventLogger(event: H3Event) {
    return {
        log: (message: string, fields?: Record<string, unknown>) => write('info', message, fields, event),
        info: (message: string, fields?: Record<string, unknown>) => write('info', message, fields, event),
        warn: (message: string, fields?: Record<string, unknown>) => write('warn', message, fields, event),
        error: (message: string, fields?: Record<string, unknown>) => write('error', message, fields, event),
    }
}

export const serverLogger = {
    info: (message: string, fields?: Record<string, unknown>) => write('info', message, fields),
    warn: (message: string, fields?: Record<string, unknown>) => write('warn', message, fields),
    error: (message: string, fields?: Record<string, unknown>) => write('error', message, fields),
}
