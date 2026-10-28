import { apiUrl, fetchWithSafeTransport, fetchWithOneTimeRefresh, secureReadInit, secureWriteInit } from './http';
import { getCurrentSession } from './session';
import { responseHasInvalidExpenseCsrfToken, RecoveredPendingSaveError } from './expenseRequestErrors';
import { publicSavedExpenseRequest, isPublicExpenseRequestUuid as uuid, isPublicExpenseRequestDate as day } from './expenseRequestPublicResponse';
import { selectPendingCommand, adoptPendingFingerprint, clearPendingCommand } from './expenseRequestPendingCommands';
import { useMockApi } from './runtime';
export type ApprovalAccrualLine = {
    lineId: string;
    accrualCostCenterId: string;
    accrualPeriod: string;
    vatRateCode: 'Vat16' | 'WithoutVat';
};
export type ApprovalAllocationLine = Omit<ApprovalAccrualLine, 'lineId'> & {
    sourceLineId: string;
    sourceGroupId: string;
    lineId: string | null;
    amount: number;
};
export type ApprovalDetails = {
    purpose: string | null;
    comment: string | null;
    businessJustification: string | null;
    isUrgent: boolean;
    urgencyReason: string | null;
    contractId: string | null;
    invoiceNumber: string | null;
    invoiceDate: string | null;
    publicOfferName: string | null;
    publicOfferIdentifier: string | null;
    otherBasisExplanation: string | null;
    candidateContractNumber: string | null;
    candidateContractNumberMissing: boolean;
    candidateContractDate: string | null;
    candidateContractSubject: string | null;
    candidateContractTerms: string | null;
    accrualLines?: ApprovalAccrualLine[];
    allocationLines?: ApprovalAllocationLine[];
};
export type ApprovalDetailsState = {
    canEdit: boolean;
    requestVersion: string;
    paymentBasisCode: string | null;
    isNewContract: boolean;
    details: ApprovalDetails | null;
};
const textKeys = ['purpose', 'comment', 'businessJustification', 'urgencyReason', 'publicOfferIdentifier', 'otherBasisExplanation', 'candidateContractTerms'] as const;
const shortKeys = ['invoiceNumber', 'candidateContractNumber'] as const;
const mediumKeys = ['publicOfferName', 'candidateContractSubject'] as const;
const keys = [...textKeys, ...shortKeys, ...mediumKeys, 'contractId', 'invoiceDate', 'candidateContractDate', 'isUrgent', 'candidateContractNumberMissing'];
export function publicApprovalDetails(value: unknown): ApprovalDetails {
    if (!value || typeof value !== 'object' || Array.isArray(value))
        throw new Error('Некорректный ответ реквизитов заявки.');
    const v = value as Record<string, unknown>;
    if (Object.keys(v).some(k => !keys.includes(k) && k !== 'accrualLines' && k !== 'allocationLines') || keys.some(k => !(k in v))
        || textKeys.some(k => v[k] !== null && (typeof v[k] !== 'string' || (v[k] as string).length > 1000))
        || shortKeys.some(k => v[k] !== null && (typeof v[k] !== 'string' || (v[k] as string).length > 100))
        || mediumKeys.some(k => v[k] !== null && (typeof v[k] !== 'string' || (v[k] as string).length > 300))
        || (v.contractId !== null && !uuid(v.contractId)) || ['invoiceDate', 'candidateContractDate'].some(k => v[k] !== null && !day(v[k]))
        || typeof v.isUrgent !== 'boolean' || typeof v.candidateContractNumberMissing !== 'boolean')
        throw new Error('Некорректный ответ реквизитов заявки.');
    if (v.accrualLines !== undefined && (!Array.isArray(v.accrualLines) || v.accrualLines.length > 10000
        || v.accrualLines.some((x: unknown) => {
            if (!x || typeof x !== 'object' || Array.isArray(x))
                return true;
            const l = x as Record<string, unknown>;
            return Object.keys(l).length !== 4 || !uuid(l.lineId) || !uuid(l.accrualCostCenterId)
                || !day(l.accrualPeriod) || !(l.accrualPeriod as string).endsWith('-01') || !['Vat16', 'WithoutVat'].includes(String(l.vatRateCode));
        })
        || new Set(v.accrualLines.map((l: ApprovalAccrualLine) => l.lineId.toLowerCase())).size !== v.accrualLines.length))
        throw new Error('Некорректная аналитика начисления.');
    if (v.allocationLines !== undefined && (!Array.isArray(v.allocationLines) || v.allocationLines.length > 10000
        || v.accrualLines !== undefined || v.allocationLines.some((x: unknown) => {
        if (!x || typeof x !== 'object' || Array.isArray(x))
            return true;
        const l = x as Record<string, unknown>;
        return Object.keys(l).length !== 7 || !uuid(l.sourceGroupId) || !uuid(l.sourceLineId) || (l.lineId !== null && (!uuid(l.lineId) || String(l.lineId).toLowerCase() !== String(l.sourceLineId).toLowerCase()))
            || !uuid(l.accrualCostCenterId) || !day(l.accrualPeriod) || !(l.accrualPeriod as string).endsWith('-01')
            || !['Vat16', 'WithoutVat'].includes(String(l.vatRateCode)) || typeof l.amount !== 'number' || l.amount <= 0
            || !Number.isSafeInteger(Math.round(l.amount * 100)) || Number(l.amount.toFixed(2)) !== l.amount;
    }) || new Set(v.allocationLines.filter((l: ApprovalAllocationLine) => l.lineId !== null).map((l: ApprovalAllocationLine) => l.lineId!.toLowerCase())).size
        !== v.allocationLines.filter((l: ApprovalAllocationLine) => l.lineId !== null).length))
        throw new Error('Некорректное распределение суммы.');
    return { ...Object.fromEntries(keys.map(k => [k, v[k]])), ...(v.accrualLines === undefined ? {} : { accrualLines: v.accrualLines }),
        ...(v.allocationLines === undefined ? {} : { allocationLines: v.allocationLines }) } as ApprovalDetails;
}
export class ApprovalDetailsRefusedError extends Error {
}
async function failure(r: Response): Promise<never> {
    const value: unknown = await r.json().catch(() => null);
    const v = value && typeof value === 'object' ? value as Record<string, unknown> : {};
    if (r.status === 400 && ((v.code === 'allocation-total-mismatch' && v.detail === 'Сумма строк не равна сумме заявки. Сохранение не выполнено.')
        || (v.code === 'allocation-source-mismatch' && v.detail === 'Сумма частей по каждой статье ДДС должна сохраняться. Статья ДДС и бюджет не меняются.')))
        throw new ApprovalDetailsRefusedError(v.detail as string);
    if (r.status === 400 && v.detail === 'Заполните обязательные поля, проверьте длину текста и действующий договор. Для срочной заявки укажите причину срочности.')
        throw new ApprovalDetailsRefusedError(v.detail);
    if (r.status === 400 && typeof v.detail === 'string' && v.detail.length <= 300
        && (/^(Не переданы редактируемые реквизиты заявки\.|Заполните назначение платежа\.|Укажите причину срочности\.|Нельзя удалить обоснование заявки\.|Нельзя очистить обязательные реквизиты основания оплаты или договора\.|Выберите действующий договор этого контрагента, организации и валюты\.)$/u.test(v.detail)
            || /^Поле «(Назначение платежа|Комментарий|Обоснование|Причина срочности|Номер счёта|Наименование оферты|Идентификатор оферты|Иное основание|Номер договора|Предмет договора|Условия оплаты)»: не более (100|300|1000) символов, без управляющих знаков\.$/u.test(v.detail)))
        throw new ApprovalDetailsRefusedError(v.detail);
    if (r.status === 403)
        throw new ApprovalDetailsRefusedError('Редактировать может только текущий согласующий или казначей до передачи в оплату. НСИ недоступно.');
    if (r.status === 409)
        throw new ApprovalDetailsRefusedError('Заявка уже изменена другим сотрудником. Введённый текст оставлен на экране; скопируйте его и обновите заявку.');
    if (r.status === 404)
        throw new ApprovalDetailsRefusedError('Заявка не найдена или недоступна.');
    if (r.status === 400)
        throw new ApprovalDetailsRefusedError('Проверьте обязательные реквизиты. Изменение статьи и суммы возможно только через возврат.');
    throw new Error('Не удалось подтвердить сохранение реквизитов. Повторите ту же попытку; введённые данные не очищены.');
}
export async function getApprovalDetails(id: string, signal: AbortSignal): Promise<ApprovalDetailsState | null> {
    if (!uuid(id))
        throw new Error('Некорректный идентификатор заявки.');
    const r = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${id}/approval-details?splitAccruals=true`), { ...secureReadInit(), signal });
    if (!r.ok)
        return failure(r);
    const v = await r.json() as Record<string, unknown>;
    if (!v || !uuid(v.requestId) || v.requestId.toLowerCase() !== id.toLowerCase() || typeof v.canEdit !== 'boolean' || !uuid(v.requestVersion) || typeof v.isNewContract !== 'boolean'
        || !(v.paymentBasisCode === null || ['Contract', 'InvoiceWithoutContract', 'PublicOffer', 'OtherBasis'].includes(String(v.paymentBasisCode))))
        throw new Error('Некорректный ответ реквизитов заявки.');
    return { canEdit: v.canEdit, requestVersion: v.requestVersion, isNewContract: v.isNewContract, paymentBasisCode: v.paymentBasisCode as string | null, details: v.canEdit ? publicApprovalDetails(v.details) : null };
}
export async function saveApprovalDetails(id: string, version: string, input: ApprovalDetails) {
    if (useMockApi || !uuid(id) || !uuid(version))
        throw new Error('Редактирование недоступно.');
    const body = { expectedVersion: version, details: publicApprovalDetails(input) }, scope = `expense-request:approval-details:${id.toLowerCase()}`;
    const selected = await selectPendingCommand(scope, body);
    let command = selected.command;
    if (selected.requiresResolution) {
        const read = secureReadInit();
        const r = await fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${id}/approval-details/idempotency`), { ...read, headers: { ...read.headers, 'Idempotency-Key': command.key } });
        if (r.ok && r.status !== 204) {
            const saved = publicSavedExpenseRequest(await r.json(), id);
            clearPendingCommand(scope, command.key);
            throw new RecoveredPendingSaveError(saved);
        }
        if (r.status !== 204)
            return failure(r);
        command = adoptPendingFingerprint(scope, command, selected.requestedFingerprint);
    }
    const r = await fetchWithOneTimeRefresh(() => fetchWithSafeTransport(apiUrl(`/api/v1/expense-requests/${id}/approval-details`), secureWriteInit('PUT', { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, JSON.stringify(body))), responseHasInvalidExpenseCsrfToken, getCurrentSession);
    if (!r.ok) {
        if ([400, 403, 404, 409].includes(r.status))
            clearPendingCommand(scope, command.key);
        return failure(r);
    }
    const saved = publicSavedExpenseRequest(await r.json(), id);
    clearPendingCommand(scope, command.key);
    return saved;
}
