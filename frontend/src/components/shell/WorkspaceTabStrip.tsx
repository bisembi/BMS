import { Home, X } from 'lucide-react';
import { useEffect, useRef, type KeyboardEvent } from 'react';
export type ShellTab = {
    key: string;
    label: string;
    open: () => void;
    closable?: boolean;
    onClose?: () => void;
};
type Props = {
    tabs: readonly ShellTab[];
    selectedIndex: number;
    /// Legacy close control used by the pre-persistent-tab shell. New callers
    /// put a close control on each individual ShellTab instead.
    closable: boolean;
    onCloseDocument: () => void;
};
/**
 * Keep the DOM relationship between a workspace tab and its panel stable
 * without copying document identifiers into markup. Request identifiers are
 * server data and must never become visible as part of an HTML id (the same
 * markup is inspected by assistive technology and browser-side diagnostics).
 * The legacy shell keeps its historical ids; persistent document tabs use
 * their ordinal in the current tab strip instead.
 */
export function workspaceTabDomId(key: string, index: number): string {
    if (key === 'home')
        return 'workspace-tab-home';
    if (key === 'document')
        return 'workspace-tab-document';
    if (key.startsWith('context:')) {
        const section = key.slice('context:'.length).replace(/[^a-z0-9_-]/giu, '-');
        return `workspace-tab-context-${section || 'section'}`;
    }
    return `workspace-tab-open-${index + 1}`;
}
export function WorkspaceTabStrip({ tabs, selectedIndex, closable, onCloseDocument }: Props) {
    const tabCount = tabs.length;
    const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);
    const focusAfterCloseIndex = useRef<number | null>(null);
    const stripRef = useRef<HTMLDivElement>(null);
    useEffect(() => {
        const strip = stripRef.current;
        const selected = tabRefs.current[selectedIndex];
        if (!strip || !selected)
            return;
        const entry = selected.closest('.workspace-tab-entry') ?? selected;
        const outer = strip.getBoundingClientRect();
        const inner = entry.getBoundingClientRect();
        if (inner.right > outer.right)
            strip.scrollLeft += inner.right - outer.right;
        else if (inner.left < outer.left)
            strip.scrollLeft -= outer.left - inner.left;
        // Scroll only this horizontal container; never focus or scroll the document.
    }, [selectedIndex, tabCount, tabs[selectedIndex]?.key]);
    useEffect(() => {
        // Drop refs of tabs that no longer exist, so a closed document tab can
        // never be focused through a stale entry.
        tabRefs.current.length = tabCount;
        const pending = focusAfterCloseIndex.current;
        if (pending === null)
            return;
        focusAfterCloseIndex.current = null;
        const available = tabRefs.current.filter((tab): tab is HTMLButtonElement => tab !== null);
        if (available.length === 0)
            return;
        available[Math.min(Math.max(pending, 0), available.length - 1)]?.focus();
    });
    const focusTab = (index: number) => {
        const total = tabs.length;
        const target = ((index % total) + total) % total;
        tabRefs.current[target]?.focus();
    };
    const onTabsKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        const current = tabRefs.current.findIndex((tab) => tab === document.activeElement);
        if (current < 0)
            return;
        if (event.key === 'ArrowRight') {
            event.preventDefault();
            focusTab(current + 1);
        }
        else if (event.key === 'ArrowLeft') {
            event.preventDefault();
            focusTab(current - 1);
        }
        else if (event.key === 'Home') {
            event.preventDefault();
            focusTab(0);
        }
        else if (event.key === 'End') {
            event.preventDefault();
            focusTab(tabs.length - 1);
        }
        else if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            tabs[current]?.open();
        }
    };
    const closeAt = (index: number, onClose: () => void) => {
        // Focus moves to the neighbour that survives the close.
        focusAfterCloseIndex.current = Math.min(index, Math.max(tabs.length - 2, 0));
        onClose();
    };
    const usesPerTabCloseButtons = tabs.some((tab) => tab.closable === true);
    return (<div className="workspace-tab-strip">
      <div ref={stripRef} className="workspace-tabs" role="tablist" aria-label="Открытые вкладки" onKeyDown={onTabsKeyDown}>
        {tabs.map((tab, index) => {
            const selected = index === selectedIndex;
            const isHome = tab.key === 'home';
            const isDocumentTab = tab.key === 'document';
            const className = [
                'workspace-tab',
                isHome ? 'workspace-home' : '',
                isDocumentTab ? 'document' : '',
                selected ? 'active' : '',
            ].filter((token) => token !== '').join(' ');
            const tabButton = (<button key={tab.key} id={workspaceTabDomId(tab.key, index)} ref={(element) => {
                    tabRefs.current[index] = element;
                }} className={className} type="button" role="tab" aria-selected={selected} aria-controls="workspace-panel" tabIndex={selected || (selectedIndex < 0 && index === 0) ? 0 : -1} {...(isHome ? { 'aria-label': 'Начальная страница' } : {})} onClick={tab.open}>
              {isHome && <Home size={17} aria-hidden="true"/>}
              <span>{tab.label}</span>
            </button>);
            if (isHome || !usesPerTabCloseButtons || tab.closable !== true || tab.onClose === undefined) {
                return tabButton;
            }
            const entryClassName = [
                'workspace-tab-entry',
                isDocumentTab ? 'document' : '',
                selected ? 'active' : '',
            ].filter((token) => token !== '').join(' ');
            return (<div key={`${tab.key}-entry`} className={entryClassName}>
              {tabButton}
              <button className="workspace-tab-close" type="button" aria-label={`Закрыть вкладку «${tab.label}»`} onClick={() => closeAt(index, tab.onClose!)}>
                <X size={15} aria-hidden="true"/>
              </button>
            </div>);
        })}
      </div>
      {!usesPerTabCloseButtons && closable && (<button className="workspace-tab-close" type="button" aria-label="Закрыть вкладку" onClick={() => {
                // Legacy consumers replace the document with its list at the same
                // ordinal; the permanent start tab must not steal that focus.
                focusAfterCloseIndex.current = Math.max(selectedIndex, 0);
                onCloseDocument();
            }}>
          <X size={15} aria-hidden="true"/>
        </button>)}
    </div>);
}
