import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import { publicExpenseRequestPaymentDetails, type ExpenseRequestPaymentDetails } from './expenseRequestPaymentDetailsPublicResponse';
class PaymentDetailsApiError extends Error {
    constructor(readonly status: number) { super('Строки платежей недоступны.'); }
}
export function paymentDetailsErrorMessage(failure: unknown): string {
    if (failure instanceof PaymentDetailsApiError) {
        if (failure.status === 401)
            return 'Для просмотра строк платежей требуется авторизация.';
        if (failure.status === 403)
            return 'Нет права просмотра строк платежей. Итог оплаты остаётся доступен в карточке.';
        if (failure.status === 404)
            return 'Заявка не найдена либо больше недоступна.';
    }
    return 'Строки платежей сейчас недоступны. Повторите загрузку; это не означает отсутствия платежей.';
}
export async function getExpenseRequestPaymentDetails(requestId: string, skip = 0, take = 25, signal?: AbortSignal): Promise<ExpenseRequestPaymentDetails> {
    if (!isPublicExpenseRequestUuid(requestId) || !Number.isInteger(skip) || skip < 0 || skip > 2147483647
        || !Number.isInteger(take) || take < 1 || take > 100)
        throw new PaymentDetailsApiError(400);
    const path = `/api/v1/expense-requests/${encodeURIComponent(requestId)}/payment-details?skip=${skip}&take=${take}`;
    const response = await fetchWithSafeTransport(apiUrl(path), { ...secureReadInit(), signal });
    if (!response.ok) {
        // Consume/discard diagnostics without waiting for a stalled error stream.
        void response.arrayBuffer().catch(() => undefined);
        throw new PaymentDetailsApiError(response.status);
    }
    return publicExpenseRequestPaymentDetails(await response.json(), skip, take);
}
