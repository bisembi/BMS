import { businessDayInput, formatBusinessDay } from './businessTime';
export function toBusinessDateInput(date = new Date()): string { return businessDayInput(date); }
export function formatBusinessDate(date = new Date()): string {
    return formatBusinessDay(date.toISOString());
}
