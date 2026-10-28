/// Only two conflict codes and the exact public CSRF refusal are recognised.
/// Server-authored diagnostic text never reaches the caller.
import type { SavedExpenseRequest } from '../types';
import { expenseDraftSaveMessages, isExpenseDraftSaveCode, readExpenseDraftSaveCode, type ExpenseDraftSaveCode } from './expenseDraftSaveProblems';
export type ExpenseRequestConflictCode = 'idempotency-conflict' | 'version-conflict' | 'csrf-token-invalid' | 'budget-limit-changed' | 'treasury_budget_check_unavailable' | 'treasury_rescheduling_paused' | ExpenseDraftSaveCode;
const expenseRequestConflictCodes = new Set<ExpenseRequestConflictCode>([
    'idempotency-conflict',
    'version-conflict',
    'budget-limit-changed',
]);
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function isExpenseRequestConflictCode(value: unknown): value is ExpenseRequestConflictCode {
    if (value !== 'idempotency-conflict' && value !== 'version-conflict' && value !== 'budget-limit-changed')
        return false;
    return expenseRequestConflictCodes.has(value);
}
export class ApiResponseError extends Error {
    constructor(message: string, readonly status: number, readonly code: string | null) {
        super(message);
        this.name = 'ApiResponseError';
    }
}
/**
 * Starts consuming a refused response's body and throws the result away.
 *
 * In the Chrome 152 HTTP loopback control, Playwright could not finish reading
 * an already-sent 403 until the page consumed its body. Consumption made the
 * body available and emitted requestfinished; requestfailed stayed false.
 * The HTTPS stand had recorded the same unfinished-response symptoms.
 *
 * Deliberately not awaited. The status is already known, so the fixed message
 * is thrown immediately and a body that stalls forever cannot delay it. The
 * bytes are discarded and a read failure is swallowed, so neither
 * server-authored content nor a parser's text can reach a caller - the same
 * shape `cashFlowAccrualSettings` and `expenseRequestPaymentDetails` already
 * use for their rejected responses.
 *
 * Tolerant of a response object without a readable body: several accepted
 * regressions construct fakes with only the fields they assert on, and a
 * diagnostic drain must never be the reason one of them fails.
 *
 * Exported for `attachmentUploadIntent`, the one client write that builds its
 * own init and therefore does not reach its failure path through this module.
 * It shares the behaviour rather than a second copy of it, so the discard has
 * exactly one definition to audit.
 */
export function startDiscardingBody(response: Response): void {
    try {
        void Promise.resolve(response.arrayBuffer?.()).catch(() => undefined);
    }
    catch {
        // Nothing readable to drain.
    }
}
export async function readExpenseRequestConflictCode(response: Response): Promise<ExpenseRequestConflictCode | null> {
    if (response.status === 400 && response.headers?.get('Content-Type')?.split(';')[0].trim().toLowerCase() === 'application/problem+json' && response.body)
        return readExpenseDraftSaveCode(response);
    if (response.status === 503 && response.headers?.get('Content-Type')?.split(';')[0].trim().toLowerCase() === 'application/problem+json' && response.body) {
        const code = await readBoundedRefusalCode(response, ['treasury_budget_check_unavailable', 'treasury_rescheduling_paused']);
        return code === 'treasury_budget_check_unavailable' || code === 'treasury_rescheduling_paused' ? code : null;
    }
    if (response.status === 403 && response.headers?.get('Content-Type')
        ?.split(';')[0].trim().toLowerCase() === 'application/problem+json') {
        let pending = csrfResponseCodes.get(response);
        if (pending === undefined) {
            pending = readBoundedCsrfCode(response);
            csrfResponseCodes.set(response, pending);
        }
        return pending;
    }
    // Only 409 carries a code this boundary recognises. Every other refusal is
    // still a response whose body has to be consumed exactly once, here, because
    // this is the one place all four expense-request command surfaces pass
    // through on their failure path.
    if (response.status !== 409) {
        startDiscardingBody(response);
        return null;
    }
    const payload = await response.json().catch(() => null);
    if (!isRecord(payload) || !isExpenseRequestConflictCode(payload.code)) {
        return null;
    }
    return payload.code;
}
const csrfResponseCodes = new WeakMap<Response, Promise<'csrf-token-invalid' | null>>();
/** Bounded bytes and time: inspecting a refusal must never hold a form forever. */
async function readBoundedCsrfCode(response: Response): Promise<'csrf-token-invalid' | null> {
    const code = await readBoundedRefusalCode(response, ['csrf-token-invalid']);
    return code === 'csrf-token-invalid' ? code : null;
}
async function readBoundedRefusalCode(response: Response, allowed: readonly string[]): Promise<string | null> {
    if (!response.body)
        return null;
    let reader: ReadableStreamDefaultReader<Uint8Array>;
    try {
        reader = response.body.getReader();
    }
    catch {
        return null;
    }
    let timer: ReturnType<typeof setTimeout> | undefined;
    let expired = false;
    const deadline = new Promise<null>(resolve => {
        timer = setTimeout(() => { expired = true; void reader.cancel().catch(() => undefined); resolve(null); }, 1000);
    });
    const parsed = (async () => {
        try {
            const decoder = new TextDecoder();
            let bytes = 0;
            let text = '';
            for (;;) {
                const chunk = await reader.read();
                if (expired)
                    return null;
                if (chunk.done)
                    break;
                bytes += chunk.value.byteLength;
                if (bytes > 4096) {
                    void reader.cancel().catch(() => undefined);
                    return null;
                }
                text += decoder.decode(chunk.value, { stream: true });
            }
            const payload: unknown = JSON.parse(text + decoder.decode());
            return isRecord(payload) && typeof payload.code === 'string' && allowed.includes(payload.code) ? payload.code : null;
        }
        catch {
            return null;
        }
        finally {
            try {
                reader.releaseLock();
            }
            catch { /* A broken refusal stream cannot expose its diagnostic error. */ }
        }
    })();
    try {
        return await Promise.race([parsed, deadline]);
    }
    finally {
        clearTimeout(timer);
    }
}
export async function responseHasInvalidExpenseCsrfToken(response: Response): Promise<boolean> {
    return response.status === 403 && await readExpenseRequestConflictCode(response) === 'csrf-token-invalid';
}
export function expenseRequestErrorMessage(status: number, code: ExpenseRequestConflictCode | null): string {
    if (status === 400 && isExpenseDraftSaveCode(code))
        return expenseDraftSaveMessages[code];
    if (status === 403 && code === 'csrf-token-invalid') {
        return 'Сеанс безопасности устарел. Повторите сохранение заявки.';
    }
    if (code === 'idempotency-conflict') {
        return 'Ключ операции уже использован с другими данными. Обновите заявку и повторите сохранение.';
    }
    if (code === 'version-conflict') {
        return 'Заявка была изменена другим пользователем. Обновите данные и повторите сохранение.';
    }
    if (status === 409) {
        return 'Операция конфликтует с текущим состоянием заявки. Обновите данные и повторите сохранение.';
    }
    if (status === 400)
        return 'Сервер отклонил запрос. Проверьте введённые данные.';
    if (status === 401)
        return 'Для сохранения заявки требуется авторизация.';
    if (status === 403)
        return 'Недостаточно прав для сохранения заявки.';
    if (status === 404)
        return 'Расходная заявка не найдена или недоступна.';
    if (status === 503)
        return 'Сохранение заявки временно недоступно. Повторите попытку позднее.';
    return `Ошибка API: ${status}`;
}
export function expenseRequestWorkflowCommandErrorMessage(status: number, code: ExpenseRequestConflictCode | null): string {
    if (status === 503 && code === 'treasury_budget_check_unavailable')
        return 'Не удалось проверить факт 1С и бюджет месяца оплаты. Заявка не передана в оплату; проверьте журнал обмена.';
    if (status === 503 && code === 'treasury_rescheduling_paused')
        return 'Передача в оплату и перенос резерва временно приостановлены администратором.';
    if (code === 'budget-limit-changed')
        return 'Бюджет изменился: превышение выросло относительно суммы, зафиксированной при подаче. Верните заявку на доработку и подайте её повторно для нового согласования.';
    if (status === 403 && code === 'csrf-token-invalid') {
        return 'Сеанс безопасности устарел. Повторите команду заявки.';
    }
    if (code === 'idempotency-conflict') {
        return 'Ключ операции уже использован с другими данными. Обновите заявку и повторите команду.';
    }
    if (code === 'version-conflict') {
        return 'Заявка была изменена другим пользователем. Обновите данные и повторите команду.';
    }
    if (status === 400)
        return 'Сервер отклонил команду. Проверьте данные заявки.';
    if (status === 401)
        return 'Для выполнения команды требуется авторизация.';
    if (status === 403)
        return 'Недостаточно прав для выполнения команды.';
    if (status === 404)
        return 'Расходная заявка не найдена или недоступна.';
    if (status === 409)
        return 'Команда конфликтует с текущим состоянием заявки. Обновите данные.';
    if (status === 503)
        return 'Команды заявки временно недоступны. Повторите попытку позднее.';
    return 'Не удалось выполнить команду расходной заявки.';
}
export class RecoveredPendingSaveError extends Error {
    constructor(readonly recoveredRequest: SavedExpenseRequest) {
        super('Предыдущая попытка сохранения подтверждена сервером. Загружена сохранённая версия; более поздние изменения в форме не были отправлены.');
        this.name = 'RecoveredPendingSaveError';
    }
}
