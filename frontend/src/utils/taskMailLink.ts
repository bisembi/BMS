/** Mail links are navigation only. Document access/commands remain server-authorized. */
export function taskMailRequestId(search: string): string | null {
    return taskDocumentId(search, 'expenseRequest');
}
export function taskBudgetInstanceId(search: string): string | null {
    return taskDocumentId(search, 'budgetInstance');
}
export function taskBudgetAdjustmentId448(search: string): string | null {
    return taskDocumentId(search, 'budgetAdjustment');
}
export function taskStaffSubstitution471(search: string): string | null { return taskDocumentId(search, 'staffSubstitution'); }
function taskDocumentId(search: string, key: string): string | null {
    const values = new URLSearchParams(search).getAll(key);
    if (values.length !== 1 || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(values[0])
        || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(values[0]))
        return null;
    return values[0].toLowerCase();
}
