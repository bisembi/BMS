import { useEffect, useId, useMemo, useRef, useState, type FocusEvent, type KeyboardEvent } from 'react';
import { Search } from 'lucide-react';
import type { NavigationSearchItem, View } from '../../navigation';
const maximumVisibleResults = 8;
function normalizedTokens(value: string): string[] {
    return value
        .normalize('NFKC')
        .toLocaleLowerCase('ru-RU')
        .trim()
        .split(/\s+/u)
        .filter((token) => token !== '');
}
export function ShellNavigationSearch({ items, onOpen, }: {
    items: readonly NavigationSearchItem[];
    onOpen: (view: View) => void;
}) {
    const inputRef = useRef<HTMLInputElement>(null);
    const rootRef = useRef<HTMLDivElement>(null);
    const listboxId = useId();
    const [query, setQuery] = useState('');
    const [isOpen, setIsOpen] = useState(false);
    const [activeIndex, setActiveIndex] = useState(0);
    const matches = useMemo(() => {
        const tokens = normalizedTokens(query);
        if (tokens.length === 0)
            return [];
        return items.filter((item) => {
            const searchable = `${item.title} ${item.context}`
                .normalize('NFKC')
                .toLocaleLowerCase('ru-RU');
            return tokens.every((token) => searchable.includes(token));
        });
    }, [items, query]);
    const results = matches.slice(0, maximumVisibleResults);
    const selectedIndex = Math.min(activeIndex, Math.max(results.length - 1, 0));
    const expanded = isOpen && normalizedTokens(query).length > 0;
    useEffect(() => {
        if (items.length !== 0)
            return;
        setQuery('');
        setIsOpen(false);
        setActiveIndex(0);
    }, [items.length]);
    useEffect(() => {
        const focusSearch = (event: globalThis.KeyboardEvent) => {
            if (!event.ctrlKey || !event.shiftKey || event.altKey || event.metaKey
                || event.key.toLocaleLowerCase('en-US') !== 'f')
                return;
            event.preventDefault();
            inputRef.current?.focus();
            inputRef.current?.select();
            if (normalizedTokens(query).length > 0)
                setIsOpen(true);
        };
        document.addEventListener('keydown', focusSearch);
        return () => document.removeEventListener('keydown', focusSearch);
    }, [query]);
    const activate = (item: NavigationSearchItem) => {
        onOpen(item.view);
        setQuery('');
        setIsOpen(false);
        inputRef.current?.blur();
    };
    const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            setIsOpen(false);
            return;
        }
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            if (results.length === 0)
                return;
            event.preventDefault();
            setIsOpen(true);
            setActiveIndex((current) => {
                const delta = event.key === 'ArrowDown' ? 1 : -1;
                return ((current + delta) % results.length + results.length) % results.length;
            });
            return;
        }
        if (event.key === 'Home' && expanded && results.length > 0) {
            event.preventDefault();
            setActiveIndex(0);
            return;
        }
        if (event.key === 'End' && expanded && results.length > 0) {
            event.preventDefault();
            setActiveIndex(results.length - 1);
            return;
        }
        if (event.key === 'Enter' && expanded && results[selectedIndex]) {
            event.preventDefault();
            activate(results[selectedIndex]);
        }
    };
    const onBlurCapture = (event: FocusEvent<HTMLDivElement>) => {
        if (event.relatedTarget instanceof Node && rootRef.current?.contains(event.relatedTarget))
            return;
        setIsOpen(false);
    };
    return (<div ref={rootRef} className="global-search" role="search" aria-label="Навигация по BMS" onBlurCapture={onBlurCapture}>
      <Search size={18} aria-hidden="true"/>
      <input ref={inputRef} type="search" role="combobox" aria-label="Глобальный поиск" aria-autocomplete="list" aria-expanded={expanded} aria-controls={expanded ? listboxId : undefined} aria-activedescendant={expanded && results[selectedIndex]
            ? `${listboxId}-option-${selectedIndex}`
            : undefined} placeholder="Раздел или команда" autoComplete="off" spellCheck={false} maxLength={100} disabled={items.length === 0} value={query} onChange={(event) => {
            setQuery(event.target.value);
            setActiveIndex(0);
            setIsOpen(true);
        }} onFocus={() => {
            if (normalizedTokens(query).length > 0)
                setIsOpen(true);
        }} onKeyDown={onKeyDown}/>
      <kbd aria-label="Сочетание клавиш Control Shift F">Ctrl+Shift+F</kbd>
      {expanded && (<div className="global-search-popup">
          <div id={listboxId} className="global-search-results" role="listbox">
            {results.map((item, index) => (<button key={item.key} id={`${listboxId}-option-${index}`} className="global-search-option" type="button" role="option" aria-label={`${item.title}. ${item.context}`} aria-selected={index === selectedIndex} onMouseMove={() => setActiveIndex(index)} onClick={() => activate(item)}>
                <span>{item.title}</span>
                <small>{item.context}</small>
              </button>))}
          </div>
          {results.length === 0 && (<div className="global-search-empty" role="status">
              Доступных разделов и команд не найдено.
            </div>)}
          {matches.length > maximumVisibleResults && <div className="global-search-empty" role="status">
            Показано {results.length} из {matches.length} — уточните запрос
          </div>}
        </div>)}
    </div>);
}
