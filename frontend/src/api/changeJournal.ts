import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
export type ChangeJournalItem = {
    occurredAtUtc: string;
    author: string;
    event: string;
    object: string;
    description: string;
};
export type ChangeJournalPageData = {
    items: ChangeJournalItem[];
    skip: number;
    take: number;
    total: number;
};
export type ChangeJournalFilters = {
    fromUtc?: string;
    toUtc?: string;
    category?: string;
};
export async function getChangeJournal(filters: ChangeJournalFilters, skip: number, take: number, signal?: AbortSignal): Promise<ChangeJournalPageData> {
    if (!Number.isSafeInteger(skip) || skip < 0 || skip > 2147483647 || ![50, 100, 500].includes(take))
        throw new Error('Некорректные параметры страницы журнала.');
    const query = new URLSearchParams({ skip: String(skip), take: String(take) });
    for (const [key, value] of Object.entries(filters))
        if (value)
            query.set(key, value);
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/administration/change-journal?${query}`), { ...secureReadInit(), signal });
    if (!response.ok)
        throw new Error(response.status === 403 ? 'Нет доступа к журналу изменений в выбранной роли.'
            : response.status === 401 ? 'Необходимо войти в BMS.' : 'Журнал изменений временно недоступен.');
    return publicChangeJournalPage(await response.json(), skip, take);
}
export function publicChangeJournalPage(value: unknown, skip: number, take: number): ChangeJournalPageData {
    const invalid = () => new Error('Не удалось прочитать журнал изменений.');
    if (typeof value !== 'object' || value === null)
        throw invalid();
    const page = value as Record<string, unknown>;
    if (!Array.isArray(page.items) || page.items.length > take || page.skip !== skip || page.take !== take
        || !Number.isSafeInteger(page.total) || (page.total as number) < 0)
        throw invalid();
    const items = page.items.map((value: unknown) => {
        if (typeof value !== 'object' || value === null)
            throw invalid();
        const row = value as Record<string, unknown>;
        for (const key of ['occurredAtUtc', 'author', 'event', 'object', 'description'])
            if (typeof row[key] !== 'string' || (row[key] as string).length > 8192)
                throw invalid();
        if (Number.isNaN(Date.parse(row.occurredAtUtc as string)))
            throw invalid();
        // Construct the reviewed contract; do not spread server properties.
        return { occurredAtUtc: row.occurredAtUtc as string, author: row.author as string,
            event: row.event as string, object: row.object as string, description: row.description as string };
    });
    return { items, skip, take, total: page.total as number };
}
