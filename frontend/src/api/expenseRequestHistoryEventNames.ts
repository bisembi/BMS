import type { ExpenseRequestHistoryKind } from '../types';
// Card history and administrative detail consume the same server projection.
// Each kind has an exact title set; a known title under another kind is not accepted.
const titlesByKind: Record<ExpenseRequestHistoryKind, readonly string[]> = {
    created: ['Черновик создан', 'Черновик создан копированием'],
    updated: ['Черновик изменён', 'Реквизиты изменены при согласовании'],
    workflow: [
        'Заявка подана на согласование',
        'Заявка передана на проверку реквизитов НСИ',
        'Согласование выполнено',
        'Маршрут пересчитан по бюджету и факту',
        'Бюджет проверен перед передачей в оплату',
        'Месяц бюджета изменён казначеем',
        'Заявка утверждена',
        'Заявка утверждена при подаче',
        'Заявка возвращена',
        'Заявка отклонена',
        'Заявка отменена',
        'НСИ подтверждена',
        'Заявка передана в оплату',
    ],
    closing: ['Заявка закрыта частично', 'Заявка закрыта', 'Заявка открыта повторно'],
    payment: ['Состояние оплаты обновлено'],
    attachment: ['Файл прикреплён', 'Файл удалён'],
    unsupported: ['Событие заявки'],
};
// Own-property lookup also rejects inherited names such as "constructor".
export function isHistoryKind(value: unknown): value is ExpenseRequestHistoryKind {
    return typeof value === 'string' && Object.prototype.hasOwnProperty.call(titlesByKind, value);
}
// Only the administrative endpoint has the generic non-request audit fallback.
export function isHistoryTitle(kind: ExpenseRequestHistoryKind, title: string, auditDetail = false): boolean {
    return titlesByKind[kind].includes(title)
        || (auditDetail && kind === 'unsupported' && title === 'Событие аудита');
}
// Known captions under another kind are a contradiction, not a future event.
export function isKnownHistoryTitle(title: string): boolean {
    return Object.values(titlesByKind).some(titles => titles.includes(title));
}
