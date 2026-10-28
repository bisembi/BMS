import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { ChevronDown } from 'lucide-react';
import type { KeyboardEvent as ReactKeyboardEvent } from 'react';
import type { ReferenceDataItem } from '../../types';
import { matchesWildcardFragment } from '../../utils/matchesWildcardFragment';
type Props = {
    ariaLabel: string;
    value: string;
    items: readonly ReferenceDataItem[];
    displayItem?: ReferenceDataItem;
    emptyLabel: string;
    unavailableLabel?: string;
    disabled?: boolean;
    required?: boolean;
    onChange: (value: string) => void;
    matchesItem?: (item: ReferenceDataItem, query: string) => boolean;
    formatItem?: (item: ReferenceDataItem) => string;
    searchPlaceholder?: string;
    noMatchesLabel?: string;
    searchSelectsFirstMatch?: boolean;
};
type PopupPosition = {
    left: number;
    top: number;
    width: number;
    maxHeight: number;
};
function normalizeSearchText(value: string) {
    return value.normalize('NFKC').toLocaleLowerCase('ru-RU').replace(/\s+/gu, ' ').trim();
}
function publicItemCode(item: ReferenceDataItem) {
    const code = item.code.trim();
    return /^CF-/iu.test(code) ? '' : code;
}
/**
 * Matches ordinary fragments and masks. A space separates terms that must all
 * be present; `*` matches any fragment and `?` matches one character.
 */
export function matchesCashFlowItemMask(item: ReferenceDataItem, query: string) {
    const normalizedQuery = normalizeSearchText(query);
    if (normalizedQuery === '')
        return true;
    const haystack = normalizeSearchText(`${publicItemCode(item)} ${item.name}`);
    return normalizedQuery.split(' ').every((token) => {
        return matchesWildcardFragment(haystack, token);
    });
}
function sameIdentifier(left: string, right: string) {
    return left.toLocaleLowerCase('en-US') === right.toLocaleLowerCase('en-US');
}
function itemLabel(item: ReferenceDataItem) {
    const code = publicItemCode(item);
    return code === '' ? item.name : `${code} — ${item.name}`;
}
export function CashFlowItemCombobox({ ariaLabel, value, items, displayItem, emptyLabel, unavailableLabel = 'Выбранная статья ДДС недоступна в справочнике', disabled = false, required = false, onChange, matchesItem = matchesCashFlowItemMask, formatItem = itemLabel, searchPlaceholder = 'Код, наименование или маска * ?', noMatchesLabel = 'Статьи не найдены', searchSelectsFirstMatch = false, }: Props) {
    const triggerRef = useRef<HTMLButtonElement>(null);
    const popupRef = useRef<HTMLDivElement>(null);
    const searchRef = useRef<HTMLInputElement>(null);
    const listboxId = `cash-flow-listbox-${useId().replace(/:/gu, '')}`;
    const optionsRef = useRef<HTMLDivElement>(null);
    const [open, setOpen] = useState(false);
    const [query, setQuery] = useState('');
    const [activeIndex, setActiveIndex] = useState(0);
    const [visibilityRequest, setVisibilityRequest] = useState(0);
    const handledVisibilityRequest = useRef(-1);
    const [position, setPosition] = useState<PopupPosition>({
        left: 8,
        top: 4,
        width: 360,
        maxHeight: 360,
    });
    const selectedItem = useMemo(() => items.find((item) => sameIdentifier(item.id, value))
        ?? (displayItem && sameIdentifier(displayItem.id, value) ? displayItem : undefined), [items, value, displayItem]);
    const filteredItems = useMemo(() => items.filter((item) => matchesItem(item, query)), [items, query, matchesItem]);
    const updatePosition = useCallback(() => {
        const trigger = triggerRef.current;
        if (trigger === null)
            return;
        const rect = trigger.getBoundingClientRect();
        const viewportWidth = document.documentElement.clientWidth || window.innerWidth || 1024;
        const viewportHeight = window.innerHeight || 768;
        const width = Math.max(1, Math.min(Math.max(rect.width, 420), viewportWidth - 16));
        const left = Math.min(Math.max(rect.left, 8), Math.max(8, viewportWidth - width - 8));
        const below = Math.max(0, viewportHeight - rect.bottom - 12);
        const above = Math.max(0, rect.top - 12);
        const upward = below < 240 && above > below;
        const maxHeight = Math.max(1, Math.min(420, upward ? above : below));
        const nextPosition = {
            left,
            top: Math.min(Math.max(8, upward ? rect.top - maxHeight - 4 : rect.bottom + 4), Math.max(8, viewportHeight - maxHeight - 8)),
            width,
            maxHeight,
        };
        setPosition((current) => current.left === nextPosition.left && current.top === nextPosition.top
            && current.width === nextPosition.width && current.maxHeight === nextPosition.maxHeight ? current : nextPosition);
    }, []);
    const closePopup = useCallback((restoreFocus = false) => {
        setOpen(false);
        setQuery('');
        if (restoreFocus)
            requestAnimationFrame(() => triggerRef.current?.focus());
    }, []);
    const openPopup = useCallback(() => {
        if (disabled || triggerRef.current?.matches(':disabled'))
            return;
        setQuery('');
        const selectedIndex = items.findIndex((item) => sameIdentifier(item.id, value));
        setActiveIndex(selectedIndex < 0 ? 0 : selectedIndex + 1);
        setVisibilityRequest((current) => current + 1);
        updatePosition();
        setOpen(true);
    }, [disabled, items, updatePosition, value]);
    useEffect(() => {
        if (!open)
            return undefined;
        updatePosition();
        requestAnimationFrame(() => searchRef.current?.focus());
        const handlePointerDown = (event: PointerEvent) => {
            const target = event.target;
            if (!(target instanceof Node))
                return;
            if (!triggerRef.current?.contains(target) && !popupRef.current?.contains(target))
                closePopup();
        };
        const handleScroll = (event: Event) => {
            // Listbox scrolling does not move the trigger. Repositioning on this
            // event used to feed back into active-option autoscroll and fight wheels.
            if (event.target instanceof Node && popupRef.current?.contains(event.target))
                return;
            updatePosition();
        };
        window.addEventListener('resize', updatePosition);
        window.addEventListener('scroll', handleScroll, true);
        document.addEventListener('pointerdown', handlePointerDown);
        return () => {
            window.removeEventListener('resize', updatePosition);
            window.removeEventListener('scroll', handleScroll, true);
            document.removeEventListener('pointerdown', handlePointerDown);
        };
    }, [closePopup, open, updatePosition]);
    useEffect(() => {
        if (disabled && open)
            closePopup();
    }, [closePopup, disabled, open]);
    useEffect(() => {
        setActiveIndex((current) => Math.min(current, filteredItems.length));
    }, [filteredItems.length]);
    useEffect(() => {
        // Reveal only after opening, search or keyboard navigation. Hover and
        // popup/page geometry changes must never override the user's scroll.
        if (!open || handledVisibilityRequest.current === visibilityRequest)
            return;
        const list = optionsRef.current;
        const option = document.getElementById(`${listboxId}-option-${activeIndex}`);
        if (!list || !option)
            return;
        handledVisibilityRequest.current = visibilityRequest;
        const outer = list.getBoundingClientRect();
        const inner = option.getBoundingClientRect();
        if (inner.top < outer.top)
            list.scrollTop -= outer.top - inner.top;
        else if (inner.bottom > outer.bottom)
            list.scrollTop += inner.bottom - outer.bottom;
    }, [activeIndex, filteredItems, listboxId, open, position, visibilityRequest]);
    const choose = (nextValue: string) => {
        if (disabled || triggerRef.current?.matches(':disabled')) {
            closePopup();
            return;
        }
        onChange(nextValue);
        closePopup(true);
    };
    const handleSearchKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            closePopup(true);
            return;
        }
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            setVisibilityRequest((current) => current + 1);
            setActiveIndex((current) => Math.min(current + 1, filteredItems.length));
            return;
        }
        if (event.key === 'ArrowUp') {
            event.preventDefault();
            setVisibilityRequest((current) => current + 1);
            setActiveIndex((current) => Math.max(current - 1, 0));
            return;
        }
        if (event.key === 'Home') {
            event.preventDefault();
            setVisibilityRequest((current) => current + 1);
            setActiveIndex(0);
            return;
        }
        if (event.key === 'End') {
            event.preventDefault();
            setVisibilityRequest((current) => current + 1);
            setActiveIndex(filteredItems.length);
            return;
        }
        if (event.key === 'Enter') {
            event.preventDefault();
            if (searchSelectsFirstMatch && query.trim() && filteredItems.length === 0)
                return;
            choose(activeIndex === 0 ? '' : filteredItems[activeIndex - 1]?.id ?? '');
        }
    };
    const selectedLabel = selectedItem !== undefined
        ? formatItem(selectedItem)
        : value === '' ? emptyLabel : unavailableLabel;
    return (<>
      <button ref={triggerRef} type="button" className="cash-flow-combobox-trigger" aria-label={ariaLabel} aria-haspopup="listbox" aria-expanded={open} aria-controls={open ? listboxId : undefined} aria-required={required || undefined} disabled={disabled} title={selectedLabel} onClick={() => {
            if (open)
                closePopup();
            else
                openPopup();
        }} onKeyDown={(event) => {
            if (event.key !== 'ArrowDown')
                return;
            event.preventDefault();
            if (!open)
                openPopup();
        }}>
        <span>{selectedLabel}</span>
        <ChevronDown size={16} aria-hidden="true"/>
      </button>
      {open && createPortal(<div ref={popupRef} className="cash-flow-combobox-popup" style={{
                left: `${position.left}px`,
                top: `${position.top}px`,
                width: `${position.width}px`,
                height: `${position.maxHeight}px`,
                maxHeight: `${position.maxHeight}px`,
            }}>
          <input ref={searchRef} type="search" className="cash-flow-combobox-search" aria-label={`Поиск: ${ariaLabel}`} placeholder={searchPlaceholder} maxLength={100} value={query} aria-controls={listboxId} aria-activedescendant={`${listboxId}-option-${activeIndex}`} onChange={(event) => {
                setQuery(event.target.value);
                setVisibilityRequest((current) => current + 1);
                setActiveIndex(event.target.value === '' && selectedItem !== undefined
                    ? items.findIndex((item) => sameIdentifier(item.id, selectedItem.id)) + 1
                    : searchSelectsFirstMatch && event.target.value.trim() && items.some(item => matchesItem(item, event.target.value)) ? 1 : 0);
            }} onKeyDown={handleSearchKeyDown}/>
          <div ref={optionsRef} id={listboxId} className="cash-flow-combobox-options" role="listbox" aria-label={ariaLabel}>
            <button id={`${listboxId}-option-0`} type="button" role="option" aria-selected={value === ''} className={activeIndex === 0 ? 'is-active' : undefined} onMouseMove={() => setActiveIndex(0)} onClick={() => choose('')}>
              {emptyLabel}
            </button>
            {filteredItems.map((item, index) => (<button id={`${listboxId}-option-${index + 1}`} key={item.id} type="button" role="option" aria-selected={sameIdentifier(item.id, value)} className={activeIndex === index + 1 ? 'is-active' : undefined} onMouseMove={() => setActiveIndex(index + 1)} onClick={() => choose(item.id)}>
                {formatItem(item)}
              </button>))}
            {filteredItems.length === 0 && <div className="cash-flow-combobox-empty" role="status">{noMatchesLabel}</div>}
          </div>
        </div>, document.body)}
    </>);
}
