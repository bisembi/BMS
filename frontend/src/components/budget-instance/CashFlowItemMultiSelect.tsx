import { ChevronDown } from 'lucide-react';
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent } from 'react';
import type { ReferenceDataItem } from '../../types';
import '../../styles/budget-copy-actuals.css';
export type CashFlowItemMultiSelectProps = {
    /** The parent supplies the complete active, non-group catalogue. */
    items: readonly ReferenceDataItem[];
    value: readonly string[];
    onChange: (value: string[]) => void;
    ariaLabel?: string;
    disabled?: boolean;
};
const identifier = (value: string) => value.toLocaleLowerCase('en-US');
const normalizeSearch = (value: string) => value.normalize('NFKC')
    .toLocaleLowerCase('ru-RU').replace(/\s+/gu, ' ').trim();
/** Greedy wildcard matching avoids constructing a backtracking regular expression. */
function matchesMaskFragment(text: string, token: string): boolean {
    const characters = [...text];
    const mask = [...`*${token}*`];
    let characterIndex = 0;
    let maskIndex = 0;
    let lastStarIndex = -1;
    let retryCharacterIndex = 0;
    while (characterIndex < characters.length) {
        if (mask[maskIndex] === '?'
            || (mask[maskIndex] !== '*' && mask[maskIndex] === characters[characterIndex])) {
            characterIndex += 1;
            maskIndex += 1;
        }
        else if (mask[maskIndex] === '*') {
            lastStarIndex = maskIndex;
            maskIndex += 1;
            retryCharacterIndex = characterIndex;
        }
        else if (lastStarIndex >= 0) {
            maskIndex = lastStarIndex + 1;
            retryCharacterIndex += 1;
            characterIndex = retryCharacterIndex;
        }
        else {
            return false;
        }
    }
    while (mask[maskIndex] === '*')
        maskIndex += 1;
    return maskIndex === mask.length;
}
/** Search only the public name, never the internal CF-* identifier. */
export function matchesCashFlowItemNameMask(name: string, query: string): boolean {
    const normalizedQuery = normalizeSearch(query);
    if (normalizedQuery === '')
        return true;
    const haystack = normalizeSearch(name);
    return normalizedQuery.split(' ').every(token => token.includes('*') || token.includes('?')
        ? matchesMaskFragment(haystack, token)
        : haystack.includes(token));
}
export function CashFlowItemMultiSelect({ items, value, onChange, ariaLabel = 'Статьи ДДС', disabled = false, }: CashFlowItemMultiSelectProps) {
    const wrapper = useRef<HTMLDivElement>(null);
    const trigger = useRef<HTMLButtonElement>(null);
    const search = useRef<HTMLInputElement>(null);
    const selectVisible = useRef<HTMLInputElement>(null);
    const popupId = `cash-flow-multi-${useId().replace(/:/gu, '')}`;
    const [open, setOpen] = useState(false);
    const [query, setQuery] = useState('');
    const selected = useMemo(() => new Set(value.map(identifier)), [value]);
    const visibleItems = useMemo(() => items.filter(item => matchesCashFlowItemNameMask(item.name, query)), [items, query]);
    const allVisibleSelected = visibleItems.length > 0
        && visibleItems.every(item => selected.has(identifier(item.id)));
    const someVisibleSelected = visibleItems.some(item => selected.has(identifier(item.id)));
    const closePopup = useCallback((restoreFocus = false) => {
        setOpen(false);
        setQuery('');
        if (restoreFocus)
            trigger.current?.focus();
    }, []);
    useEffect(() => {
        if (!open)
            return undefined;
        search.current?.focus();
        const outside = (event: Event) => {
            if (event.target instanceof Node && !wrapper.current?.contains(event.target))
                closePopup();
        };
        // This popup participates in the dialog layout. Closing it on pointerdown
        // moves Copy/Cancel before pointerup and can consume their first click.
        // Keep it in place throughout pointer activation; keyboard focus still
        // closes it immediately, and the bubbling click closes it after the action.
        let pointerActive = false;
        const pointerDown = () => { pointerActive = true; };
        const pointerEnd = () => { pointerActive = false; };
        const focusOutside = (event: Event) => {
            if (!pointerActive)
                outside(event);
        };
        document.addEventListener('pointerdown', pointerDown, true);
        document.addEventListener('pointerup', pointerEnd, true);
        document.addEventListener('pointercancel', pointerEnd, true);
        document.addEventListener('click', outside);
        document.addEventListener('focusin', focusOutside);
        return () => {
            document.removeEventListener('pointerdown', pointerDown, true);
            document.removeEventListener('pointerup', pointerEnd, true);
            document.removeEventListener('pointercancel', pointerEnd, true);
            document.removeEventListener('click', outside);
            document.removeEventListener('focusin', focusOutside);
        };
    }, [closePopup, open]);
    useEffect(() => {
        if (disabled && open)
            closePopup();
    }, [closePopup, disabled, open]);
    useEffect(() => {
        if (selectVisible.current) {
            selectVisible.current.indeterminate = someVisibleSelected && !allVisibleSelected;
        }
    }, [allVisibleSelected, open, someVisibleSelected]);
    function toggleItems(nextItems: readonly ReferenceDataItem[], checked: boolean) {
        if (disabled || nextItems.length === 0)
            return;
        const next = new Map(value.map(id => [identifier(id), id]));
        nextItems.forEach(item => {
            if (checked)
                next.set(identifier(item.id), item.id);
            else
                next.delete(identifier(item.id));
        });
        onChange([...next.values()]);
    }
    function handleKeyDown(event: ReactKeyboardEvent<HTMLDivElement>) {
        if (open && event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            closePopup(true);
        }
    }
    return (<div ref={wrapper} className="cash-flow-multi-select" onKeyDown={handleKeyDown}>
      <button ref={trigger} type="button" className="cash-flow-multi-select-trigger" aria-label={ariaLabel} aria-expanded={open} aria-controls={open ? popupId : undefined} disabled={disabled} onClick={() => {
            if (open)
                closePopup();
            else {
                setQuery('');
                setOpen(true);
            }
        }} onKeyDown={event => {
            if (event.key === 'ArrowDown' && !disabled) {
                event.preventDefault();
                setOpen(true);
            }
        }}>
        <span>{selected.size === 0 ? 'Выберите статьи ДДС' : `Выбрано статей: ${selected.size}`}</span>
        <ChevronDown size={16} aria-hidden="true"/>
      </button>
      {open && <div id={popupId} className="cash-flow-multi-select-popup" role="group" aria-label={ariaLabel}>
        <input ref={search} type="search" className="cash-flow-multi-select-search" aria-label={`Поиск: ${ariaLabel}`} placeholder="Наименование или маска * ?" maxLength={100} value={query} onChange={event => setQuery(event.target.value)} onKeyDown={event => {
                if (event.key === 'Enter')
                    event.preventDefault();
                if (event.key === 'ArrowDown') {
                    event.preventDefault();
                    wrapper.current?.querySelector<HTMLInputElement>('[data-article-checkbox="true"]')?.focus();
                }
            }}/>
        <label className="cash-flow-multi-select-visible">
          <input ref={selectVisible} type="checkbox" checked={allVisibleSelected} disabled={disabled || visibleItems.length === 0} onChange={event => toggleItems(visibleItems, event.target.checked)}/>
          <span>Выбрать все найденные</span>
        </label>
        <div className="cash-flow-multi-select-options">
          {visibleItems.map(item => <label className="cash-flow-multi-select-option" key={item.id}>
            <input type="checkbox" data-article-checkbox="true" checked={selected.has(identifier(item.id))} disabled={disabled} onChange={event => toggleItems([item], event.target.checked)}/>
            <span>{item.name}</span>
          </label>)}
          {visibleItems.length === 0 && <p className="cash-flow-multi-select-empty" role="status">Статьи не найдены</p>}
        </div>
        <p className="cash-flow-multi-select-summary">Найдено: {visibleItems.length}; выбрано: {selected.size}</p>
      </div>}
    </div>);
}
