import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { assertExpenseRequestIdentifier } from './expenseRequestIdentifier';
import { publicExpenseRequestHistoryPage } from './expenseRequestHistoryPublicResponse';
import type { ExpenseRequestHistoryPage } from '../types';
export async function getExpenseApprovalComments360(id: string, skip = 0, take = 20): Promise<ExpenseRequestHistoryPage> {
    assertExpenseRequestIdentifier(id);
    if (!Number.isSafeInteger(skip) || skip < 0 || skip > 2147483647
        || !Number.isSafeInteger(take) || take < 1 || take > 50)
        throw new Error('Некорректная страница комментариев.');
    const historyRead = secureReadInit();
    historyRead.headers['X-Bms-History-Authors'] = 'employee-names';
    const query = new URLSearchParams({ skip: String(skip), take: String(take), commentsOnly: 'true' });
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${encodeURIComponent(id)}/history?${query}`), historyRead);
    if (!response.ok)
        throw new Error('Комментарии участников временно недоступны.');
    return publicExpenseRequestHistoryPage(await response.json(), skip, take);
}
