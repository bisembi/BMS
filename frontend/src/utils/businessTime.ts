// The BMS business clock is UTC+5, independent of browser/OS timezone.
export const businessTimeZone = 'Etc/GMT-5';
const calendar = new Intl.DateTimeFormat('ru-RU', { timeZone: businessTimeZone, day: '2-digit', month: '2-digit', year: 'numeric' });
const timestamp = new Intl.DateTimeFormat('ru-RU', { timeZone: businessTimeZone, day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' });
export function formatBusinessTimestamp(value: string | Date | null | undefined): string {
    if (!value)
        return '—';
    const date = value instanceof Date ? value : new Date(value);
    return Number.isFinite(date.getTime()) ? timestamp.format(date) : 'Время недоступно';
}
export function formatBusinessDay(value: string | null | undefined): string {
    if (!value)
        return '—';
    const match = /^(\d{4})-(\d{2})-(\d{2})$/u.exec(value);
    if (match) {
        const date = new Date(value + 'T00:00:00Z');
        return Number.isFinite(date.valueOf()) && date.toISOString().slice(0, 10) === value
            ? `${match[3]}.${match[2]}.${match[1]}` : '—';
    }
    const date = new Date(value);
    return Number.isFinite(date.valueOf()) ? calendar.format(date) : '—';
}
export function businessDayInput(date = new Date()): string {
    const parts = new Intl.DateTimeFormat('en-US', { timeZone: businessTimeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(date);
    const part = (type: string) => parts.find(item => item.type === type)?.value ?? '';
    return `${part('year')}-${part('month')}-${part('day')}`;
}
export function businessLocalInputToUtc(value: string): string {
    if (!/^[0-9]{4}-[0-9]{2}-[0-9]{2}T(?:[01][0-9]|2[0-3]):[0-5][0-9](?::[0-5][0-9])?$/u.test(value)
        || value.startsWith('0000'))
        throw new Error('Проверьте дату и время.');
    const date = new Date(value + '+05:00');
    if (!Number.isFinite(date.valueOf()) || formatBusinessDay(value.slice(0, 10)) === '—')
        throw new Error('Проверьте дату и время.');
    return date.toISOString();
}
