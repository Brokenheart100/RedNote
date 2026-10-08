import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import test from 'node:test'
import { trace } from '@opentelemetry/api'
import { NodeSDK } from '@opentelemetry/sdk-node'
import { InMemoryLogRecordExporter, SimpleLogRecordProcessor } from '@opentelemetry/sdk-logs'
import { eventLogger, formatLogMessage, sanitizeLogValue } from '../../server/utils/server-logger.ts'
import { createTracedFetch } from '../../server/utils/traced-fetch.ts'

test('structured logs redact nested credentials and parallel upstream spans keep their request trace', async () => {
    const exporter = new InMemoryLogRecordExporter()
    const processor = new SimpleLogRecordProcessor({ exporter })
    const sdk = new NodeSDK({
        serviceName: 'frontend-test',
        traceExporter: { export: (_spans, done) => done({ code: 0 }), shutdown: async () => {} },
        logRecordProcessors: [processor],
        instrumentations: [],
    })
    sdk.start()
    const received = new Map()
    const server = createServer((req, res) => {
        received.set(req.url, req.headers.traceparent)
        res.setHeader('Content-Type', 'application/json')
        res.end('{}')
    })
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
    const baseURL = `http://127.0.0.1:${server.address().port}`
    const spans = [0, 1].map(() => trace.getTracer('test').startSpan('request'))
    try {
        const originalEnvironment = process.env.NODE_ENV
        process.env.NODE_ENV = 'development'
        eventLogger({ context: { telemetrySpan: spans[0] } }).info('🔭 test', {
            statusCode: 200,
            nested: { password: 'private-password', accessToken: 'private-token', safe: 'visible' },
        })
        if (originalEnvironment === undefined) delete process.env.NODE_ENV
        else process.env.NODE_ENV = originalEnvironment
        await Promise.all(spans.map((span, index) => createTracedFetch({ context: { telemetrySpan: span } })(`/${index}`, { baseURL })))
        assert.notEqual(spans[0].spanContext().traceId, spans[1].spanContext().traceId)
        for (const [index, span] of spans.entries()) {
            const header = received.get(`/${index}`)
            assert.equal(header.split('-')[1], span.spanContext().traceId)
            assert.notEqual(header.split('-')[2], span.spanContext().spanId)
        }
        // Exporting awaits resource detection; allow the simple processor to flush.
        await processor.forceFlush()
        const record = exporter.getFinishedLogRecords()[0]
        assert.equal(record.spanContext.traceId, spans[0].spanContext().traceId)
        assert.equal(record.attributes.statusCode, 200)
        assert.deepEqual(JSON.parse(record.attributes.nested), {
            password: '[redacted]', accessToken: '[redacted]', safe: 'visible',
        })
        assert.match(record.body, /"statusCode": 200/)
        assert.match(record.body, /"safe": "visible"/)
        assert.doesNotMatch(record.body, /private-password|private-token/)
        const fields = sanitizeLogValue({ body: JSON.stringify({ items: [{ title: 'Feed item' }], access_token: 'private-token' }) })
        const detail = formatLogMessage('⬅️ [API RESPONSE]', fields, true)
        assert.match(detail, /Feed item/)
        assert.doesNotMatch(detail, /private-token/)
        assert.equal(formatLogMessage('⬅️ [API RESPONSE]', fields, false), '⬅️ [API RESPONSE]')
    }
    finally {
        spans.forEach(span => span.end())
        await new Promise(resolve => server.close(resolve))
        await sdk.shutdown()
    }
})
