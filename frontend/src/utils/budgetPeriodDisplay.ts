const monthLabels = ['янв', 'фев', 'мар', 'апр', 'май', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'] as const;
/** Display only; native month inputs and API dates keep their ISO values. */
export function formatBudgetMonthDisplay(value: string): string {
    const match = /^(\d{4})-(0[1-9]|1[0-2])$/u.exec(value);
    return match && match[1] !== '0000'
        ? `${monthLabels[Number(match[2]) - 1]} ${match[1].slice(-2)}`
        : '—';
}
/** Presentation only: keep source ISO dates and filter values unchanged. */
export function formatBudgetPeriodDisplay(from: string, to: string): string {
    function monthLabel(value: string): string | null {
        const match = /^(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/u.exec(value);
        if (!match || match[1] === '0000')
            return null;
        return `${monthLabels[Number(match[2]) - 1]} ${match[1].slice(-2)}`;
    }
    const first = monthLabel(from);
    const last = monthLabel(to);
    return first !== null && last !== null ? `${first} - ${last}` : '—';
}
