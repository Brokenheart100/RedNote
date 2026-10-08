import { readFileSync } from 'node:fs'
import { getCACertificates, setDefaultCACertificates } from 'node:tls'

export default defineNitroPlugin(() => {
    const certificatePath = process.env.NODE_EXTRA_CA_CERTS
    if (!import.meta.dev || !certificatePath) return

    // Nitro development runs in a worker; apply trust in that worker before upstream requests.
    setDefaultCACertificates([
        ...getCACertificates('default'),
        readFileSync(certificatePath, 'utf8'),
    ])
})
