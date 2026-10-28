// Unanchored fragment matching with Unicode-code-point '?' and arbitrary '*'.
// O(text length * mask length), O(text length) memory; no regex backtracking.
export function matchesWildcardFragment(text: string, mask: string): boolean {
    if (!/[*?]/u.test(mask))
        return text.includes(mask);
    const chars = [...text];
    let previous = new Uint8Array(chars.length + 1);
    previous[0] = 1;
    let previousWasStar = false;
    for (const char of ['*', ...mask, '*']) {
        if (char === '*' && previousWasStar)
            continue;
        previousWasStar = char === '*';
        const next = new Uint8Array(chars.length + 1);
        if (char === '*')
            next[0] = previous[0];
        for (let index = 1; index <= chars.length; index++) {
            next[index] = char === '*' ? previous[index] | next[index - 1]
                : previous[index - 1] && (char === '?' || char === chars[index - 1]) ? 1 : 0;
        }
        previous = next;
    }
    return previous[chars.length] === 1;
}
