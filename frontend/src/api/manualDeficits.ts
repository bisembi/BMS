import { apiUrl, fetchWithSafeTransport, secureReadInit, secureWriteInit } from './http';
import { adoptPendingFingerprint, clearPendingCommand, selectPendingCommand } from './expenseRequestPendingCommands';
import { isPublicExpenseRequestMoney, isPublicExpenseRequestUuid } from './expenseRequestPublicResponse';
import { isManualDeficitText, publicManualDeficitConflictCode, publicManualDeficitCorrection, publicManualDeficitDetail, publicManualDeficitPage } from './manualDeficitPublicResponse';
import type { ManualDeficitAttempt, ManualDeficitExecution, ManualDeficitQuery } from './manualDeficitTypes';
export const manualDeficitReadError = 'Не удалось загрузить корректировки дефицита. Повторите попытку.';
export const manualDeficitInputError = 'Проверьте сумму, основание и причину решения.';
export const manualDeficitUncertainError = 'Результат команды пока не подтверждён. Проверьте результат повторно; новая операция не создаётся.';
class ManualDeficitApiError extends Error {
    constructor(readonly status: number, readonly code: string | null = null) {
        super('Не удалось выполнить запрос корректировки дефицита.');
        this.name = 'ManualDeficitApiError';
    }
}
export function manualDeficitErrorMessage(error: unknown): string {
    if (!(error instanceof ManualDeficitApiError))
        return manualDeficitUncertainError;
    if (error.status === 400)
        return manualDeficitInputError;
    if (error.status === 401)
        return 'Для работы с корректировками требуется авторизация.';
    if (error.status === 403)
        return 'Недостаточно прав для работы с корректировками этой организации.';
    if (error.status === 404)
        return 'Дефицит или корректировка не найдены либо недоступны.';
    if (error.status === 409)
        return 'Состояние корректировки изменилось. Обновите данные перед новым решением.';
    return manualDeficitUncertainError;
}
export function isUncertainManualDeficitResult(error: unknown): boolean {
    return !(error instanceof ManualDeficitApiError) || ![400, 401, 403, 404, 409].includes(error.status);
}
export function manualDeficitReadErrorMessage(error: unknown): string {
    return error instanceof ManualDeficitApiError && [401, 403, 404].includes(error.status)
        ? manualDeficitErrorMessage(error) : manualDeficitReadError;
}
function validId(id: unknown): asserts id is string {
    if (!isPublicExpenseRequestUuid(id))
        throw new ManualDeficitApiError(400);
}
function validPaging(skip: number, take: number) {
    if (!Number.isInteger(skip) || !Number.isInteger(take) || skip < 0 || take < 1 || take > 100
        || skip > 2147483647 - take)
        throw new ManualDeficitApiError(400);
}
function normalizedText(value: unknown, required: boolean): string | null {
    if (value === null || value === undefined || value === '') {
        if (required)
            throw new ManualDeficitApiError(400);
        return null;
    }
    if (typeof value !== 'string')
        throw new ManualDeficitApiError(400);
    const result = value.trim();
    if (!result && !required)
        return null;
    if (!isManualDeficitText(result))
        throw new ManualDeficitApiError(400);
    return result;
}
async function failure(response: Response): Promise<ManualDeficitApiError> {
    let code: string | null = null;
    if (response.status === 409) {
        code = publicManualDeficitConflictCode(await response.json().catch(() => null));
    }
    return new ManualDeficitApiError(response.status, code);
}
async function read<T>(path: string, publicParse: (value: unknown) => T, signal?: AbortSignal): Promise<T> {
    const response = await fetchWithSafeTransport(apiUrl(path), { ...secureReadInit(), signal });
    if (!response.ok)
        throw await failure(response);
    return publicParse(await response.json());
}
export async function getManualDeficits(query: ManualDeficitQuery, signal?: AbortSignal) {
    validPaging(query.skip, query.take);
    if (query.organizationId !== undefined)
        validId(query.organizationId);
    if (typeof query.includeSettled !== 'boolean' || typeof query.onlyPending !== 'boolean')
        throw new ManualDeficitApiError(400);
    const parameters = new URLSearchParams({ skip: String(query.skip), take: String(query.take),
        includeSettled: String(query.includeSettled), onlyPending: String(query.onlyPending) });
    if (query.organizationId)
        parameters.set('organizationId', query.organizationId);
    return read(`/api/v1/overpayment-deficits?${parameters}`, (value) => publicManualDeficitPage(value, query), signal);
}
export async function getManualDeficit(id: string, skip = 0, take = 25, signal?: AbortSignal) {
    validId(id);
    validPaging(skip, take);
    return read(`/api/v1/overpayment-deficits/${encodeURIComponent(id)}?skip=${skip}&take=${take}`, (value) => publicManualDeficitDetail(value, id, skip, take), signal);
}
export async function executeManualDeficit(attempt: ManualDeficitAttempt): Promise<ManualDeficitExecution> {
    validId(attempt.deficitId);
    let body: Record<string, unknown>;
    let path: string;
    if (attempt.operation === 'create') {
        if (!isPublicExpenseRequestMoney(attempt.amount, false))
            throw new ManualDeficitApiError(400);
        body = { amount: attempt.amount, basis: normalizedText(attempt.basis, true) };
        path = `/api/v1/overpayment-deficits/${encodeURIComponent(attempt.deficitId)}/corrections`;
    }
    else {
        validId(attempt.correctionId);
        if (attempt.operation !== 'approve' && attempt.operation !== 'reject')
            throw new ManualDeficitApiError(400);
        body = { comment: normalizedText(attempt.comment, false) };
        if (attempt.operation === 'reject')
            body.reason = normalizedText(attempt.reason, true);
        path = `/api/v1/manual-deficit-corrections/${encodeURIComponent(attempt.correctionId)}/${attempt.operation}`;
    }
    const scope = `manual-deficit:${path.toLowerCase()}`;
    const selection = await selectPendingCommand(scope, body);
    let command = selection.command;
    const publicCommandResult = (value: unknown) => {
        const result = publicManualDeficitCorrection(value, attempt.deficitId, attempt.operation === 'create' ? undefined : attempt.correctionId);
        if ((attempt.operation === 'create' && result.state !== 'PendingApproval')
            || (attempt.operation === 'approve' && result.state !== 'ApprovedAndApplied' && result.state !== 'Annulled')
            || (attempt.operation === 'reject' && result.state !== 'Rejected'))
            throw new ManualDeficitApiError(503);
        return result;
    };
    const resolve = async () => {
        const init = secureReadInit();
        const response = await fetchWithSafeTransport(apiUrl(`${path}/idempotency`), {
            ...init, headers: { ...init.headers, 'Idempotency-Key': command.key },
        });
        if (response.status === 204)
            return null;
        if (response.status !== 200)
            throw await failure(response);
        return publicCommandResult(await response.json());
    };
    // Resolve even an unchanged pending body before sending. A reload, timeout or
    // changed form must not silently substitute a new financial operation.
    const recovered = await resolve();
    if (recovered) {
        clearPendingCommand(scope, command.key);
        return { correction: recovered, recovered: true };
    }
    if (selection.requiresResolution)
        command = adoptPendingFingerprint(scope, command, selection.requestedFingerprint);
    const response = await fetchWithSafeTransport(apiUrl(path), secureWriteInit('POST', {
        'Content-Type': 'application/json', 'Idempotency-Key': command.key,
    }, JSON.stringify(body)));
    if (!response.ok) {
        const error = await failure(response);
        if (error.code === 'idempotency-conflict') {
            const original = await resolve();
            if (original) {
                clearPendingCommand(scope, command.key);
                return { correction: original, recovered: true };
            }
        }
        else if ([400, 401, 403, 404, 409].includes(error.status))
            clearPendingCommand(scope, command.key);
        throw error;
    }
    if (response.status !== (attempt.operation === 'create' ? 201 : 200))
        throw new ManualDeficitApiError(503);
    const correction = publicCommandResult(await response.json());
    clearPendingCommand(scope, command.key);
    return { correction, recovered: false };
}
