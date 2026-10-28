import { businessDayInput, businessTimeZone } from './businessTime';
const time = new Intl.DateTimeFormat('ru-RU', { timeZone: businessTimeZone, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
const day = new Intl.DateTimeFormat('ru-RU', { timeZone: businessTimeZone, day: '2-digit', month: '2-digit', year: '2-digit' });
// Sort on the full SQL timestamp, never this context-dependent display label.
export function formatExpenseRequestDate(value: string, now = new Date()): string {
    const date = new Date(value);
    if (!Number.isFinite(date.getTime()))
        return '—';
    return businessDayInput(date) === businessDayInput(now) ? time.format(date) : day.format(date);
}
