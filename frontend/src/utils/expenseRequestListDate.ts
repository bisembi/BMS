export const invalidExpenseRequestDateMessage = 'Дата фильтра должна быть корректной.';
const canonicalDatePattern = /^\d{4}-\d{2}-\d{2}$/u;
export function isCanonicalExpenseRequestDate(value: string): boolean {
    if (!canonicalDatePattern.test(value))
        return false;
    const parsed = new Date(`${value}T00:00:00Z`);
    return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value;
}
