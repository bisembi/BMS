import { useCallback, useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent } from 'react';
import type { ReferenceDataItem } from '../../types';
import { CashFlowItemMultiSelect } from './CashFlowItemMultiSelect';
import { budgetActualsErrorMessage } from '../../api/budgetActuals';
export type BudgetCopyActualsSelection = {
    fromMonth: string;
    toMonth: string;
    cashFlowItemIds: string[];
};
export type BudgetCopyActualsDialogProps = {
    items: readonly ReferenceDataItem[];
    loading: boolean;
    catalogueError: string | null;
    reload: () => void;
    initialFrom: string;
    initialTo: string;
    /** Root sets 2026; no source-year translation or copy outside that year. */
    allowedYear?: number;
    /** Display-only scope chosen and authorised by the parent; never inferred here. */
    scopeLabel: string;
    /** Reads a SQL143 preview and atomically fills the parent form, without saving it. */
    onCopy: (selection: BudgetCopyActualsSelection) => Promise<void>;
    /** Parent must invalidate any pending preview before closing/unmounting the dialog. */
    onClose: () => void;
};
export function isBudgetCopyActualsMonth(value: string): boolean {
    return /^\d{4}-(0[1-9]|1[0-2])$/u.test(value) && !value.startsWith('0000-');
}
const catalogueErrorText = 'Не удалось загрузить статьи ДДС.';
/** Render this as a sibling after the budget form, not as another nested form. */
export function BudgetCopyActualsDialog({ items, loading, catalogueError, reload, initialFrom, initialTo, allowedYear, scopeLabel, onCopy, onClose, }: BudgetCopyActualsDialogProps) {
    const dialog = useRef<HTMLDialogElement>(null);
    const fromInput = useRef<HTMLInputElement>(null);
    const mounted = useRef(false);
    const closed = useRef(false);
    const copyInFlight = useRef(false);
    const generation = useRef(0);
    const closeCallback = useRef(onClose);
    closeCallback.current = onClose;
    const [fromMonth, setFromMonth] = useState(initialFrom);
    const [toMonth, setToMonth] = useState(initialTo);
    const [selectedIds, setSelectedIds] = useState<string[]>([]);
    const [allAvailable, setAllAvailable] = useState(false);
    const [copying, setCopying] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const titleId = `budget-copy-actuals-title-${useId().replace(/:/gu, '')}`;
    const scopeId = `budget-copy-actuals-scope-${useId().replace(/:/gu, '')}`;
    const allowedYearText = allowedYear === undefined ? null
        : Number.isInteger(allowedYear) && allowedYear >= 1 && allowedYear <= 9999
            ? String(allowedYear).padStart(4, '0') : '';
    const minimumMonth = allowedYearText ? `${allowedYearText}-01` : undefined;
    const maximumMonth = allowedYearText ? `${allowedYearText}-12` : undefined;
    const closeDialog = useCallback(() => {
        if (closed.current)
            return;
        closed.current = true;
        generation.current += 1;
        copyInFlight.current = false;
        if (mounted.current)
            setCopying(false);
        const element = dialog.current;
        if (element?.open) {
            try {
                element.close();
            }
            catch {
                element.removeAttribute('open');
            }
            // Test/embedded environments may expose an unimplemented close method.
            if (element.open)
                element.removeAttribute('open');
        }
        closeCallback.current();
    }, []);
    useEffect(() => {
        mounted.current = true;
        closed.current = false;
        const previousFocus = document.activeElement;
        const element = dialog.current;
        if (element) {
            try {
                element.showModal();
            }
            catch {
                element.setAttribute('open', '');
            }
            if (!element.open)
                element.setAttribute('open', '');
            fromInput.current?.focus();
        }
        return () => {
            mounted.current = false;
            generation.current += 1;
            copyInFlight.current = false;
            if (element?.open) {
                try {
                    element.close();
                }
                catch {
                    element.removeAttribute('open');
                }
                if (element.open)
                    element.removeAttribute('open');
            }
            if (previousFocus instanceof HTMLElement && previousFocus.isConnected)
                previousFocus.focus();
        };
    }, []);
    function handleDialogKeyDown(event: ReactKeyboardEvent<HTMLDialogElement>) {
        if (event.defaultPrevented)
            return;
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            closeDialog();
            return;
        }
        // Native dialogs trap focus; this also covers the non-native fallback.
        if (event.key !== 'Tab')
            return;
        const targets = [...(dialog.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex="0"]') ?? [])];
        const first = targets[0];
        const last = targets[targets.length - 1];
        if (!first || !last)
            return;
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        }
        else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    }
    async function copy() {
        if (closed.current || copyInFlight.current || loading || catalogueError !== null)
            return;
        if (!isBudgetCopyActualsMonth(fromMonth) || !isBudgetCopyActualsMonth(toMonth) || fromMonth > toMonth) {
            setError('Выберите корректный период: начало должно быть не позже окончания.');
            return;
        }
        if (allowedYearText !== null
            && (allowedYearText === '' || fromMonth.slice(0, 4) !== allowedYearText || toMonth.slice(0, 4) !== allowedYearText)) {
            setError(allowedYearText === ''
                ? 'Не удалось определить доступный период копирования.'
                : `Выберите период ${allowedYearText} года.`);
            return;
        }
        const effectiveIds = allAvailable ? items.map(item => item.id) : selectedIds;
        if (effectiveIds.length === 0) {
            setError('Выберите хотя бы одну статью ДДС.');
            return;
        }
        const availableIds = new Set(items.map(item => item.id.toLocaleLowerCase('en-US')));
        if (effectiveIds.some(id => !availableIds.has(id.toLocaleLowerCase('en-US')))) {
            setError('Выбранные статьи ДДС недоступны. Проверьте выбор после обновления справочника.');
            return;
        }
        const ids = [...new Map(effectiveIds.map(id => [id.toLocaleLowerCase('en-US'), id])).values()];
        if (ids.length > 2000) {
            setError('За один раз можно скопировать не более 2 000 статей ДДС. Выберите статьи вручную.');
            return;
        }
        Object.freeze(ids);
        const selection = { fromMonth, toMonth, cashFlowItemIds: ids };
        Object.freeze(selection);
        const request = ++generation.current;
        copyInFlight.current = true;
        setCopying(true);
        setError(null);
        try {
            await onCopy(selection);
            if (!mounted.current || closed.current || generation.current !== request)
                return;
            closeDialog();
        }
        catch (cause) {
            if (mounted.current && !closed.current && generation.current === request)
                setError(budgetActualsErrorMessage(cause));
        }
        finally {
            if (mounted.current && !closed.current && generation.current === request) {
                copyInFlight.current = false;
                setCopying(false);
            }
        }
    }
    return (<dialog ref={dialog} className="budget-copy-actuals-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={scopeId} onCancel={event => { event.preventDefault(); closeDialog(); }} onKeyDown={handleDialogKeyDown}>
      <h2 id={titleId}>Копирование факта</h2>
      <p id={scopeId} className="budget-copy-actuals-scope">Область копирования: {scopeLabel}</p>
      <div className="budget-copy-actuals-period">
        <label>
          <span>Период с</span>
          <input ref={fromInput} type="month" aria-label="Период с" value={fromMonth} min={minimumMonth} max={maximumMonth} disabled={copying} onChange={event => { setFromMonth(event.target.value); setError(null); }}/>
        </label>
        <label>
          <span>Период по</span>
          <input type="month" aria-label="Период по" value={toMonth} min={minimumMonth} max={maximumMonth} disabled={copying} onChange={event => { setToMonth(event.target.value); setError(null); }}/>
        </label>
      </div>
      <div className="budget-copy-actuals-articles">
        <label className="budget-copy-actuals-all">
          <input type="checkbox" checked={allAvailable} disabled={copying || loading || catalogueError !== null} onChange={event => { setAllAvailable(event.target.checked); setError(null); }}/>
          <span>Все доступные статьи ДДС</span>
        </label>
        <span>Статьи ДДС</span>
        <CashFlowItemMultiSelect items={items} value={allAvailable ? items.map(item => item.id) : selectedIds} disabled={allAvailable || copying || loading || catalogueError !== null} onChange={value => { setSelectedIds(value); setError(null); }}/>
      </div>
      {loading && <p role="status">Загрузка статей ДДС…</p>}
      {catalogueError !== null && <div className="budget-copy-actuals-error" role="alert">
        <p>{catalogueErrorText}</p>
        <button type="button" disabled={copying || loading} onClick={reload}>Повторить загрузку статей ДДС</button>
      </div>}
      {!loading && catalogueError === null && items.length === 0 && <p role="status">Нет доступных статей ДДС.</p>}
      {error !== null && <p className="budget-copy-actuals-error" role="alert">{error}</p>}
      {copying && <p role="status">Копирование факта…</p>}
      <div className="budget-copy-actuals-actions">
        <button type="button" className="primary-button" disabled={copying || loading || catalogueError !== null || items.length === 0} onClick={() => void copy()}>Скопировать</button>
        <button type="button" onClick={closeDialog}>Отмена</button>
      </div>
    </dialog>);
}
