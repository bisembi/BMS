const expenseRequestGuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
export const invalidExpenseRequestIdentifierMessage = 'Некорректный идентификатор расходной заявки.';
export function isExpenseRequestIdentifier(value: unknown): value is string {
    return typeof value === 'string'
        && value !== '00000000-0000-0000-0000-000000000000'
        && expenseRequestGuidPattern.test(value);
}
export function assertExpenseRequestIdentifier(value: unknown): asserts value is string {
    if (!isExpenseRequestIdentifier(value)) {
        throw new Error(invalidExpenseRequestIdentifierMessage);
    }
}
