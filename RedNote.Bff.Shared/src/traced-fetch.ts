import { context, propagation, SpanKind, SpanStatusCode, trace } from '@opentelemetry/api'
import type { Span } from '@opentelemetry/api'
import type { H3Event } from 'h3'
import { $fetch } from 'ofetch'

// Each invocation has its own fetch context, including concurrent BFF requests.
export function createTracedFetch(event: H3Event) {
    const spans = new WeakMap<object, Span>()
    return $fetch.create({
        onRequest({ request, options }) {
            const url = new URL(String(request), options.baseURL || 'http://localhost')
            const parent = event.context.telemetrySpan
                ? trace.setSpan(context.active(), event.context.telemetrySpan) : context.active()
            const span = trace.getTracer('rednote.bff').startSpan(`${options.method ?? 'GET'} ${url.pathname}`, {
                kind: SpanKind.CLIENT,
                attributes: {
                    'http.request.method': options.method ?? 'GET',
                    'url.path': url.pathname, 'server.address': url.hostname,
                },
            }, parent)
            spans.set(options, span)
            const carrier: Record<string, string> = {}
            propagation.inject(trace.setSpan(parent, span), carrier)
            for (const [name, value] of Object.entries(carrier)) options.headers.set(name, value)
        },
        onResponse({ options, response }) {
            const span = spans.get(options)
            span?.setAttribute('http.response.status_code', response.status)
            if (response.status >= 400) span?.setStatus({ code: SpanStatusCode.ERROR })
            span?.end()
            spans.delete(options)
        },
        onRequestError({ options }) {
            const span = spans.get(options)
            span?.setStatus({ code: SpanStatusCode.ERROR, message: 'Upstream request failed' })
            span?.end()
            spans.delete(options)
        },
    })
}
