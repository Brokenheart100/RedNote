import { context, propagation, SpanKind, SpanStatusCode, trace } from '@opentelemetry/api'
import { NodeSDK } from '@opentelemetry/sdk-node'
import { BatchLogRecordProcessor } from '@opentelemetry/sdk-logs'
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-grpc'
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-grpc'
import { eventLogger, serverLogger } from '../utils/server-logger'

export default defineNitroPlugin(nitroApp => {
    if (!process.env.OTEL_EXPORTER_OTLP_ENDPOINT && !process.env.OTEL_EXPORTER_OTLP_LOGS_ENDPOINT) return
    const sdk = new NodeSDK({
        serviceName: process.env.OTEL_SERVICE_NAME ?? 'frontend',
        traceExporter: new OTLPTraceExporter(),
        logRecordProcessors: [new BatchLogRecordProcessor({ exporter: new OTLPLogExporter() })],
        instrumentations: [],
    })
    sdk.start()
    serverLogger.info('🔭 [OTEL] Nuxt 服务端日志与追踪已启用')

    nitroApp.hooks.hook('request', event => {
        const path = getRequestURL(event).pathname
        if (path.startsWith('/_nuxt/') || path.startsWith('/__nuxt')) return
        const parent = propagation.extract(context.active(), event.node.req.headers)
        const span = trace.getTracer('rednote.frontend').startSpan(`${event.method} ${path}`, {
            kind: SpanKind.SERVER,
            attributes: { 'http.request.method': event.method, 'url.path': path },
        }, parent)
        event.context.telemetrySpan = span
        event.context.requestId ??= crypto.randomUUID()
        const started = performance.now()
        let finished = false
        const finish = (aborted: boolean) => {
            if (finished) return
            finished = true
            const status = event.node.res.statusCode
            const fields = {
                requestId: event.context.requestId,
                'http.request.method': event.method, 'url.path': path,
                'http.response.status_code': status,
                durationMs: Math.round(performance.now() - started), aborted,
            }
            span.setAttributes(fields)
            if (status >= 500 || aborted) span.setStatus({ code: SpanStatusCode.ERROR })
            const logger = eventLogger(event)
            const message = `${status >= 500 || aborted ? '❌' : status >= 400 ? '⚠️' : '✅'} [HTTP] ${status} ${event.method} ${path}`
            if (status >= 500 || aborted) logger.error(message, fields)
            else if (status >= 400) logger.warn(message, fields)
            else logger.info(message, fields)
            span.end()
        }
        event.node.res.once('finish', () => finish(false))
        event.node.res.once('close', () => finish(!event.node.res.writableFinished))
    })
    nitroApp.hooks.hook('close', () => sdk.shutdown())
})
