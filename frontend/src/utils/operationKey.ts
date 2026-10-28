/**
 * Creates a client operation key without requiring a secure browser context.
 *
 * `crypto.randomUUID()` is exposed only in secure contexts by some browsers,
 * while `crypto.getRandomValues()` remains available on the internal HTTP
 * staging origin. The latter is still a cryptographically secure source, so
 * the temporary HTTP VM143 route can keep idempotency protection instead of
 * falling back to timestamps or Math.random().
 */
export function createOperationKey(): string {
    const cryptoApi = globalThis.crypto;
    if (typeof cryptoApi?.randomUUID === 'function')
        return cryptoApi.randomUUID();
    if (typeof cryptoApi?.getRandomValues !== 'function') {
        throw new Error('Не удалось подготовить безопасный ключ операции.');
    }
    const bytes = new Uint8Array(16);
    cryptoApi.getRandomValues(bytes);
    // RFC 4122 version 4 / variant 1 UUID layout.
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
