/** Presentation only: retain stored document identities and historical receipts. */
export function budgetAdjustmentNumber(item: {
    number: string;
    baseBudgetNumber: string;
}): string {
    const prefix = item.baseBudgetNumber + '-';
    if (!item.baseBudgetNumber || !item.number.startsWith(prefix))
        return item.number;
    const sequence = /^(?:К)?0*([1-9][0-9]*)$/u.exec(item.number.slice(prefix.length));
    return sequence ? prefix + sequence[1] : item.number;
}
