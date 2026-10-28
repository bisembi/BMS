/** Recognize the entire input. Never remove an unknown character or round a cent. */
export function canonicalMoneyText(value: string, editing = false): string | null {
    if (value === '')
        return editing ? '' : null;
    const grammar = editing
        ? /^-?(?:\d+|\d{1,3}(?:[ \u00a0\u202f]\d{3})+)(?:[.,]\d{0,2})?$/u
        : /^-?(?:\d+|\d{1,3}(?:[ \u00a0\u202f]\d{3})+)(?:[.,]\d{1,2})?$/u;
    if (!grammar.test(value))
        return null;
    return value.replace(/[ \u00a0\u202f]/gu, '').replace(',', '.');
}
export function normalizeEditableMoney(value: string): string {
    return canonicalMoneyText(value, true) ?? value;
}
export function formatEditableMoney(value: string): string {
    const canonical = canonicalMoneyText(value, true);
    if (canonical === null || canonical === '')
        return value;
    const [whole, fraction] = canonical.split('.');
    const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/gu, ' ');
    return fraction === undefined ? grouped : `${grouped},${fraction}`;
}
/** A Number may be sent only when its JSON representation preserves the typed cents. */
export function exactMoneyNumber(value: string, allowZero: boolean): number | null {
    const canonical = canonicalMoneyText(value);
    if (canonical === null || !/^\d{1,16}(?:\.\d{1,2})?$/u.test(canonical))
        return null;
    const [whole, fraction = ''] = canonical.split('.');
    const cents = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
    if ((!allowZero && cents === 0n) || cents > BigInt(Number.MAX_SAFE_INTEGER))
        return null;
    const parsed = Number(canonical), wire = JSON.stringify(parsed);
    if (!Number.isFinite(parsed) || !/^\d+(?:\.\d{1,2})?$/u.test(wire))
        return null;
    const [wireWhole, wireFraction = ''] = wire.split('.');
    return BigInt(wireWhole) * 100n + BigInt(wireFraction.padEnd(2, '0')) === cents ? parsed : null;
}
