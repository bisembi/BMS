// Closed public reasons only. Never display server detail, input echoes or exception text.
export const expenseDraftSaveMessages = {
    'expense-draft-contract-mismatch': 'Договор не соответствует организации, контрагенту или валюте заявки. На вкладке «Контрагент и основание» выберите подходящий договор; валюту и суммы менять автоматически не будем.',
    'expense-draft-article-cfo-mismatch': 'Статья расхода недоступна для выбранного ЦФО. На вкладке «Расходы» проверьте статью каждой строки и ЦФО на вкладке «Основное».',
    'expense-draft-line-cfo-mismatch': 'ЦФО строки расходов отличается от ЦФО заявки. Проверьте строки на вкладке «Расходы»; все строки должны относиться к ЦФО из вкладки «Основное».',
    'expense-draft-number-reservation-expired': 'Резерв номера заявки устарел или недоступен. Оставьте форму открытой, дождитесь обновления номера и повторите сохранение. Не обновляйте страницу: введённые данные остаются в форме.',
    'expense-draft-purpose-managed': 'Открытая форма отправляет изменение защищённого назначения платежа. Введённые данные оставлены в форме; сохраните их отдельно перед переходом на обновлённую версию BMS.',
    'expense-draft-urgency-invalid': 'На вкладке «Основное» заполните причину срочности (до 1 000 символов) либо снимите отметку «Срочная заявка».',
    'expense-draft-comment-invalid': 'Комментарий инициатора превышает 1 000 символов. Сократите его на вкладке «Основное»; остальные данные оставлены в форме.',
    'expense-draft-justification-invalid': 'Назначение платежа превышает 1 000 символов. Сократите текст на вкладке «Основное»; остальные данные оставлены в форме.',
    'expense-draft-text-invalid': 'Превышена длина реквизита. Проверьте вкладку «Контрагент и основание»: номера — до 100 символов, наименования и предмет договора — до 300, пояснения — до 1 000, БИН/ИИН — до 12.',
    'expense-draft-currency-invalid': 'На вкладке «Основное» выберите валюту из справочника. Код валюты должен состоять из трёх латинских букв.',
    'expense-draft-upload-unavailable': 'Не удалось связать загруженные файлы с черновиком. Оставьте форму открытой и проверьте список вложений; не закрывайте её до успешного сохранения.',
} as const;
export type ExpenseDraftSaveCode = keyof typeof expenseDraftSaveMessages;
export function isExpenseDraftSaveCode(value: unknown): value is ExpenseDraftSaveCode {
    return typeof value === 'string' && Object.prototype.hasOwnProperty.call(expenseDraftSaveMessages, value);
}
/** Bounded read; a stalled or oversized refusal cannot hold the form or leak its contents. */
export async function readExpenseDraftSaveCode(response: Response): Promise<ExpenseDraftSaveCode | null> {
    return readBoundedPublicCode(response, isExpenseDraftSaveCode);
}
export async function readBoundedPublicCode<T extends string>(response: Response, isCode: (value: unknown) => value is T): Promise<T | null> {
    if (response.status !== 400 || response.headers?.get('Content-Type')?.split(';')[0].trim().toLowerCase() !== 'application/problem+json' || !response.body)
        return null;
    let reader: ReadableStreamDefaultReader<Uint8Array>;
    try {
        reader = response.body.getReader();
    }
    catch {
        return null;
    }
    let timer: ReturnType<typeof setTimeout> | undefined;
    const parsed = (async () => {
        let bytes = 0;
        let text = '';
        const decoder = new TextDecoder();
        try {
            for (;;) {
                const part = await reader.read();
                if (part.done)
                    break;
                bytes += part.value.byteLength;
                if (bytes > 4096) {
                    void reader.cancel().catch(() => undefined);
                    return null;
                }
                text += decoder.decode(part.value, { stream: true });
            }
            const value: unknown = JSON.parse(text + decoder.decode());
            return value !== null && typeof value === 'object' && !Array.isArray(value)
                && isCode((value as Record<string, unknown>).code) ? (value as {
                code: T;
            }).code : null;
        }
        catch {
            return null;
        }
        finally {
            try {
                reader.releaseLock();
            }
            catch { /* refused streams are never surfaced */ }
        }
    })();
    const timeout = new Promise<null>(resolve => { timer = setTimeout(() => { void reader.cancel().catch(() => undefined); resolve(null); }, 1000); });
    try {
        return await Promise.race([parsed, timeout]);
    }
    finally {
        clearTimeout(timer);
    }
}
