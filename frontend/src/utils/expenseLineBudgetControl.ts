import type { BudgetPosition, BudgetReservationSummary, ExpenseLineDraft } from '../types';
import { sameOpaqueIdentifier } from './expenseRequestDraft';
export type ExpenseLineBudgetControl = {
    state: 'saved' | 'preview' | 'pending' | 'unavailable';
    availableAmountKzt: number | null;
    positiveDeficitKzt: number | null;
    availableBeforeRequestKzt?: number | null;
    reason?: string;
    register343?: true;
};
/** A version-bound position is independent of whether this line still holds a
 * reserve. It is never presented as the calculation of unsaved edits. */
export function expenseLineBudgetControl(line: ExpenseLineDraft, plannedPaymentDate: string, positions: readonly BudgetPosition[] | null, isSavedAndClean: boolean): ExpenseLineBudgetControl {
    const empty = (state: 'pending' | 'unavailable'): ExpenseLineBudgetControl => ({ state, availableAmountKzt: null, positiveDeficitKzt: null });
    if (!isSavedAndClean)
        return empty('pending');
    if (positions === null || !line.id?.trim() || !line.costCenterId.trim() || !line.cashFlowItemId.trim())
        return empty('unavailable');
    const cfo = line.costCenterId;
    const month = `${plannedPaymentDate.slice(0, 7)}-01`;
    const matches = positions.filter(p => sameOpaqueIdentifier(p.cashFlowItemId, line.cashFlowItemId) && sameOpaqueIdentifier(p.costCenterId, cfo)
        && p.budgetMonth === month);
    if (matches.length !== 1)
        return empty('unavailable');
    const position = matches[0];
    if (position.requestControl343 !== undefined) {
        const control = position.requestControl343;
        return { state: 'saved', availableAmountKzt: control.availableAfterKzt,
            availableBeforeRequestKzt: control.availableBeforeKzt, positiveDeficitKzt: control.positiveDeficitKzt, register343: true };
    }
    if (typeof position.limitAmountKzt !== 'number' || !Number.isFinite(position.limitAmountKzt)
        || typeof position.availableAmountKzt !== 'number' || !Number.isFinite(position.availableAmountKzt))
        return empty('unavailable');
    return { state: 'saved', availableAmountKzt: position.availableAmountKzt,
        positiveDeficitKzt: Math.max(0, -position.availableAmountKzt) };
}
/** Exclude this request's whole outstanding reserve, not just the displayed
 * line. This is a current calculation, never a reconstructed historical balance.
 * Settled requests need an accounting attribution not available in this capture. */
export function expenseLineBudgetBeforeRequest(control: ExpenseLineBudgetControl, line: ExpenseLineDraft, paymentDate: string, reservation: BudgetReservationSummary | null): ExpenseLineBudgetControl {
    if (control.register343 === true)
        return control;
    if (control.state !== 'saved' || control.availableAmountKzt === null || reservation === null || !reservation.isComplete)
        return { ...control, availableBeforeRequestKzt: null };
    const month = `${paymentDate.slice(0, 7)}-01`;
    const matching = reservation.positions.filter(p => sameOpaqueIdentifier(p.costCenterId, line.costCenterId)
        && sameOpaqueIdentifier(p.cashFlowItemId, line.cashFlowItemId) && p.budgetMonth === month);
    const seen = new Set<string>();
    let before = Math.round(control.availableAmountKzt * 100);
    for (const position of matching) {
        const amount = position.reservedAmountKzt;
        const id = position.expenseRequestLineId.toLowerCase();
        if (!id || seen.has(id) || !Number.isFinite(amount) || amount < 0
            || Number(amount.toFixed(2)) !== amount || !Number.isSafeInteger(Math.round(amount * 100)))
            return { ...control, availableBeforeRequestKzt: null };
        seen.add(id);
        before += Math.round(amount * 100);
        if (!Number.isSafeInteger(before))
            return { ...control, availableBeforeRequestKzt: null };
    }
    return { ...control, availableBeforeRequestKzt: before / 100 };
}
