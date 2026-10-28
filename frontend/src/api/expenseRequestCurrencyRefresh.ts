import { apiUrl, fetchWithSafeTransport, secureWriteInit } from './http';
import { publicSavedExpenseRequest, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
export async function refreshExpenseRequestCurrency(id: string, expectedVersion: string, key: string) {
    if (!isPublicExpenseRequestUuid(id) || !isPublicExpenseRequestUuid(expectedVersion))
        throw new Error('Некорректная версия заявки.');
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${id}/refresh-currency`), secureWriteInit('POST', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify({ expectedVersion })));
    if (!response.ok)
        throw new Error(response.status === 409
            ? 'Заявка изменена в другой вкладке. Обновите карточку перед пересчётом.'
            : 'Не удалось автоматически обновить валютный расчёт. Повторите пересчёт.');
    return publicSavedExpenseRequest(await response.json(), id);
}
