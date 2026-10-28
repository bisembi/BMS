/** Budget follows the planned payment date, never today's or the accrual month. */
export function expenseBudgetMonthLabel362(paymentDate = ''): string {
    if (!/^\d{4}-\d{2}-\d{2}$/u.test(paymentDate))
        return 'месяца оплаты';
    const date = new Date(`${paymentDate}T00:00:00Z`);
    if (!Number.isFinite(date.valueOf()) || date.toISOString().slice(0, 10) !== paymentDate)
        return 'месяца оплаты';
    const label = new Intl.DateTimeFormat('ru-RU', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(date).replace(/\s*г\.$/u, '');
    return label.charAt(0).toUpperCase() + label.slice(1);
}
