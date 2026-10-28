/** Presentation only. Missing ranks retain the caller's established deterministic fallback. */
export function catalogueRank(item: {
    displayOrder?: number | null;
}): number {
    return typeof item.displayOrder === 'number' && Number.isSafeInteger(item.displayOrder) && item.displayOrder >= 0
        ? item.displayOrder : Number.MAX_SAFE_INTEGER;
}
export function minimumCatalogueRank(items: readonly {
    displayOrder?: number | null;
}[]): number {
    return items.reduce((rank, item) => Math.min(rank, catalogueRank(item)), Number.MAX_SAFE_INTEGER);
}
