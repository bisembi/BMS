import { apiUrl, fetchWithSafeTransport, fetchWithOneTimeRefresh, secureReadInit, secureWriteInit } from './http';
import { getCurrentSession } from './session';
import { responseHasInvalidExpenseCsrfToken } from './expenseRequestErrors';
import { isPublicExpenseRequestUuid as uuid, isPublicExpenseRequestDate as day, publicSavedExpenseRequest } from './expenseRequestPublicResponse';
export type TreasuryPaymentDateState = {
    requestId: string;
    requestVersion: string;
    date: string | null;
    defaultDate: string;
    canEdit: boolean;
    confirmed: boolean;
};
const invalid = () => new Error('Некорректный ответ API даты оплаты.');
export function publicTreasuryPaymentDateState(value: unknown, expectedId: string): TreasuryPaymentDateState {
    if (!value || typeof value !== 'object' || Array.isArray(value))
        throw invalid();
    const v = value as Record<string, unknown>;
    if (!uuid(expectedId) || !uuid(v.requestId) || String(v.requestId).toLowerCase() !== expectedId.toLowerCase()
        || !uuid(v.requestVersion) || (v.date !== null && !day(v.date)) || !day(v.defaultDate)
        || typeof v.canEdit !== 'boolean' || typeof v.confirmed !== 'boolean' || (v.canEdit && v.confirmed))
        throw invalid();
    return { requestId: v.requestId, requestVersion: v.requestVersion, date: v.date as string | null,
        defaultDate: v.defaultDate, canEdit: v.canEdit, confirmed: v.confirmed };
}
async function failure(response: Response): Promise<never> {
    // Only a fixed reviewed problem code is consumed; provider text is discarded.
    const body: unknown = await response.json().catch(() => null);
    const code = body && typeof body === 'object' && !Array.isArray(body) ? (body as Record<string, unknown>).code : null;
    if (response.status === 503 && code === 'treasury_budget_check_unavailable')
        throw new Error('Не удалось проверить факт 1С и бюджет месяца оплаты. Дата не изменена; проверьте журнал обмена.');
    if (response.status === 503 && code === 'treasury_rescheduling_paused')
        throw new Error('Передача в оплату и перенос резерва временно приостановлены администратором. Дата не изменена.');
    if (response.status === 409 && code === 'treasury_payment_date_confirmed')
        throw new Error('Дата оплаты уже подтверждена в 1С. Изменение запрещено.');
    if (response.status === 409)
        throw new Error('Заявка изменена. Обновите её перед изменением даты оплаты.');
    if (response.status === 403)
        throw new Error('Изменять дату оплаты может только казначей.');
    if (response.status === 404)
        throw new Error('Расходная заявка не найдена или недоступна.');
    throw new Error('Не удалось сохранить или прочитать дату оплаты.');
}
export async function getTreasuryPaymentDate(id: string, signal: AbortSignal): Promise<TreasuryPaymentDateState | null> {
    if (!uuid(id))
        throw invalid();
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${id}/treasury-payment-date`), { ...secureReadInit(), signal });
    if (!response.ok)
        return failure(response);
    return publicTreasuryPaymentDateState(await response.json(), id);
}
export async function setTreasuryPaymentDate(id: string, version: string, date: string, key: string) {
    if (!uuid(id) || !uuid(version) || !day(date) || !key || key.length > 127)
        throw invalid();
    const response = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${id}/treasury-payment-date`), secureWriteInit('PUT', { 'Content-Type': 'application/json', 'Idempotency-Key': key }, JSON.stringify({ expectedVersion: version, date }))), responseHasInvalidExpenseCsrfToken, getCurrentSession);
    if (!response.ok)
        return failure(response);
    const body: unknown = await response.json();
    if (!body || typeof body !== 'object' || Array.isArray(body))
        throw invalid();
    const v = body as Record<string, unknown>;
    const state = publicTreasuryPaymentDateState(v.state, id);
    const saved = publicSavedExpenseRequest(v.request, id);
    if (state.requestVersion.toLowerCase() !== saved.concurrencyStamp.toLowerCase())
        throw invalid();
    return { state, saved };
}
