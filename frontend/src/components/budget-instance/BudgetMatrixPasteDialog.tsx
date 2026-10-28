import { useEffect, useId, useRef } from 'react';
import type { BudgetPastePlan } from '../../pages/budgetMatrixPaste';
const money = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 });
const month = new Intl.DateTimeFormat('ru-RU', { month: 'short', year: '2-digit', timeZone: 'UTC' });
export function BudgetMatrixPasteDialog({ plan, onApply, onClose }: {
    plan: BudgetPastePlan;
    onApply: () => void;
    onClose: () => void;
}) {
    const dialog = useRef<HTMLDialogElement>(null);
    const cancel = useRef<HTMLButtonElement>(null);
    const title = useId();
    useEffect(() => {
        const previous = document.activeElement;
        const element = dialog.current;
        try {
            element?.showModal();
        }
        catch {
            element?.setAttribute('open', '');
        }
        if (element && !element.open)
            element.setAttribute('open', '');
        cancel.current?.focus();
        return () => {
            if (element?.open) {
                try {
                    element.close();
                }
                catch {
                    element.removeAttribute('open');
                }
            }
            if (previous instanceof HTMLElement && previous.isConnected)
                previous.focus();
        };
    }, []);
    const cleared = plan.cells.filter(cell => cell.raw === null).length;
    const detailed = plan.cells.filter(cell => cell.operationCount > 1 || cell.hasComments).length;
    const mixed = plan.cells.filter(cell => cell.mixed).length;
    return <dialog ref={dialog} role="dialog" aria-modal="true" aria-labelledby={title} className="budget-copy-actuals-dialog budget-paste-dialog" onCancel={event => { event.preventDefault(); onClose(); }} onKeyDown={event => {
            if (event.key === 'Escape') {
                event.preventDefault();
                event.stopPropagation();
                onClose();
            }
            if (event.key !== 'Tab')
                return;
            const targets = [...(dialog.current?.querySelectorAll<HTMLElement>('button:not(:disabled), [tabindex="0"]') ?? [])];
            const first = targets[0];
            const last = targets.at(-1);
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last?.focus();
            }
            else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first?.focus();
            }
        }}>
    <h2 id={title}>Вставка из Excel — предпросмотр</h2>
    <p>Блок: {plan.rowCount} × {plan.columnCount}; ячеек: {plan.cells.length}; очистка: {cleared}.</p>
    <p>Пустые ячейки очистят все операции статьи за месяц. Ноль создаст нулевую строку.
      Направление берётся из существующей ячейки, а для новой или смешанной — из справочника статьи.
      Знак вставленной суммы направление не меняет.</p>
    {detailed > 0 && <p role="alert">В {detailed} ячейках детализация операций и комментарии будут заменены.
      Непустая ячейка станет одной строкой с пустой «Операцией».</p>}
    {mixed > 0 && <p role="alert">В {mixed} ячейках были оба направления. Замена чистой суммой изменит расходный лимит.</p>}
    <div className="budget-paste-preview" tabIndex={0} aria-label="Предпросмотр изменяемых ячеек">
      <table aria-label="Предпросмотр вставки сумм из Excel"><thead><tr><th scope="col">Статья ДДС</th><th scope="col">Месяц</th><th scope="col">Было, ₸</th><th scope="col">Станет, ₸</th></tr></thead>
        <tbody>{plan.cells.map(cell => <tr key={`${cell.articleId}/${cell.month}`}>
          <td>{cell.articleLabel}</td><td>{month.format(new Date(`${cell.month}T00:00:00Z`)).replace('.', '')}</td>
          <td>{cell.beforeInvalid ? 'Некорректная сумма' : cell.beforeCents === null ? 'Пусто' : money.format(cell.beforeCents / 100)}</td>
          <td>{cell.raw === null ? 'Очистить' : money.format(cell.afterCents! / 100)}</td>
        </tr>)}</tbody></table>
    </div>
    <p>«Применить» изменит только форму. Для записи в SQL нажмите «Сохранить черновик».</p>
    <div className="directory-actions"><button type="button" onClick={onApply}>Применить вставку</button>
      <button ref={cancel} type="button" onClick={onClose}>Отмена вставки</button></div>
  </dialog>;
}
