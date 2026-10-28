import type { ExpenseLineBudgetControl } from './expenseLineBudgetControl';
export function expenseLineBudgetVerdict(control: ExpenseLineBudgetControl | undefined, limit: number | null | undefined, validAmount: boolean): 'over' | 'within' | 'unknown' {
    if (!validAmount || !control || !['saved', 'preview'].includes(control.state)
        || control.positiveDeficitKzt == null || !Number.isFinite(control.positiveDeficitKzt))
        return 'unknown';
    return control.positiveDeficitKzt > 0 || limit === 0 ? 'over' : 'within';
}
