import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from 'react';
import { isBookmarkableView, keyForView, type View } from '../navigation';
import { browserState, emptyScroll, historyContext, historyView, isTransientView, normalizeReloadView, readHistoryEntry, sameSavedDocument, type ScrollPosition, type WorkspaceHistoryEntry } from '../navigation/browserHistory';
import { readWorkspaceScroll, restoreWorkspaceScroll } from '../navigation/workspaceScroll';
type Options = {
    view: View;
    views: View[];
    binding: string | null;
    generation: string | null;
    enabled: boolean;
    signedOut?: boolean;
    main: RefObject<HTMLElement | null>;
    canRestore: (view: View) => boolean;
    onRestore: (view: View) => void;
    blockedReason: () => string | null;
    onNotice: (message: string) => void;
};
type Controller = {
    generation: string;
    entry: WorkspaceHistoryEntry;
    live: Map<string, View>;
    closed: Set<string>;
    opened: Set<string>;
    positions: Map<string, ScrollPosition>;
    pending: {
        key: string;
        scroll: ScrollPosition;
    } | null;
    bounce: number | null;
    stopScroll: (() => void) | null;
};
export function useBrowserWorkspaceHistory(options: Options) {
    const latest = useRef(options);
    latest.current = options;
    const controller = useRef<Controller | null>(null);
    const page = useRef<string | null>(null);
    const lastPersist = useRef(0);
    const [context, setContext] = useState<{
        binding: string;
        hash: string;
    } | null>(null);
    const [historyRevision, setHistoryRevision] = useState(0);
    useEffect(() => {
        let active = true;
        if (!options.binding) {
            setContext(null);
            return;
        }
        void historyContext(options.binding).then(hash => {
            if (active)
                setContext(hash ? { binding: options.binding!, hash } : null);
        });
        return () => { active = false; };
    }, [options.binding]);
    function persist(entry: WorkspaceHistoryEntry, push = false) {
        try {
            window.history[push ? 'pushState' : 'replaceState'](browserState(entry), '', window.location.href);
            lastPersist.current = Date.now();
        }
        catch {
            latest.current.onNotice('Не удалось обновить историю браузера. Используйте вкладки BMS.');
        }
    }
    function capture(persistNow: boolean) {
        const state = controller.current, args = latest.current;
        if (!state || state.pending || state.bounce !== null || state.entry.key !== keyForView(args.view))
            return;
        const scroll = readWorkspaceScroll(args.main.current);
        state.positions.set(state.entry.key, scroll);
        state.entry = { ...state.entry, scroll };
        if (persistNow)
            persist(state.entry);
    }
    function resolve(entry: WorkspaceHistoryEntry, state: Controller): View | null {
        const args = latest.current;
        if (state.closed.has(entry.key))
            return null;
        const live = state.live.get(entry.key) ?? [...state.live.values()].find(view => sameSavedDocument(view, entry.view));
        const view = live ?? normalizeReloadView(entry.view);
        if (isTransientView(view) && (entry.page !== page.current || !live))
            return null;
        return args.canRestore(view) ? view : null;
    }
    useLayoutEffect(() => {
        // popstate is not cancelable: never replace the rejected physical history
        // slot while history.go() is returning to the last accepted entry.
        if (controller.current?.bounce !== null && controller.current?.bounce !== undefined)
            return;
        if (options.signedOut) {
            const state = controller.current;
            if (state && state.generation !== 'signed-out') {
                state.stopScroll?.();
                state.live.clear();
                state.opened.clear();
                state.positions.clear();
                state.closed.clear();
                state.pending = null;
                state.bounce = null;
                state.generation = 'signed-out';
                state.entry = { ...state.entry, run: crypto.randomUUID(), key: 'section:home', view: { kind: 'section', sectionId: 'home' }, scroll: emptyScroll, closed: [] };
                persist(state.entry);
            }
            return;
        }
        if (!options.enabled || !options.generation || !context || context.binding !== options.binding)
            return;
        const key = keyForView(options.view);
        page.current ??= crypto.randomUUID();
        let state = controller.current;
        if (!state || state.generation !== options.generation || state.entry.context !== context.hash) {
            state?.stopScroll?.();
            const prior = readHistoryEntry(window.history.state);
            const adopting = !state && prior?.context === context.hash;
            const entry: WorkspaceHistoryEntry = { version: 1, context: context.hash, run: adopting ? prior.run : crypto.randomUUID(),
                page: page.current, index: prior?.index ?? 0, ceiling: prior?.ceiling ?? 0, key, view: historyView(options.view), scroll: emptyScroll,
                closed: adopting ? prior.closed : [] };
            state = { generation: options.generation, entry, live: new Map(), closed: new Set(entry.closed), opened: new Set(),
                positions: new Map(), pending: null, bounce: null, stopScroll: null };
            controller.current = state;
            for (const view of [...options.views, options.view])
                state.live.set(keyForView(view), view);
            const restored = adopting ? resolve(prior, state) : null;
            if (restored && prior) {
                state.entry = { ...prior, page: page.current, closed: [...state.closed] };
                state.pending = { key: keyForView(restored), scroll: prior.scroll };
                options.onRestore(restored);
            }
            else
                persist(entry);
            return;
        }
        const opened = new Set(options.views.filter(isBookmarkableView).map(keyForView));
        for (const old of state.opened)
            if (!opened.has(old) && old !== key) {
                state.closed.add(old);
                state.live.delete(old);
                state.positions.delete(old);
            }
        for (const view of [...options.views, options.view]) {
            const viewKey = keyForView(view);
            state.live.set(viewKey, view);
            state.closed.delete(viewKey);
        }
        state.opened = opened;
        while (state.closed.size > 512)
            state.closed.delete(state.closed.values().next().value!);
        const next = { ...state.entry, key, view: historyView(options.view), page: page.current, closed: [...state.closed] };
        if (state.pending && state.pending.key === key) {
            const scroll = state.pending.scroll;
            state.pending = null;
            state.entry = { ...next, scroll };
            persist(state.entry);
            state.stopScroll?.();
            state.stopScroll = restoreWorkspaceScroll(options.main.current, scroll);
        }
        else if (state.entry.key !== key) {
            const scroll = state.positions.get(key) ?? emptyScroll;
            state.entry = { ...next, index: state.entry.index + 1, ceiling: state.entry.index + 1, scroll };
            persist(state.entry, true);
            state.stopScroll?.();
            state.stopScroll = restoreWorkspaceScroll(options.main.current, scroll);
        }
        else {
            state.entry = next;
            persist(next);
        }
    }, [options.view, options.views, options.enabled, options.signedOut, options.binding, options.generation, context, historyRevision]);
    useEffect(() => {
        const previousRestoration = window.history.scrollRestoration;
        window.history.scrollRestoration = 'manual';
        function pop(event: PopStateEvent) {
            const state = controller.current, args = latest.current;
            if (!state)
                return;
            const target = readHistoryEntry(event.state);
            if (!target)
                return; // A genuine external-page transition belongs to the browser.
            if (state.bounce === target.index && target.context === state.entry.context && target.run === state.entry.run) {
                state.bounce = null;
                setHistoryRevision(revision => revision + 1);
                return;
            }
            const delta = target.index - state.entry.index;
            const blocked = !args.enabled || state.generation !== args.generation
                ? 'Дождитесь завершения проверки или переключения роли.' : args.blockedReason();
            if (blocked || target.context !== state.entry.context || target.run !== state.entry.run) {
                const distance = state.entry.index - target.index;
                args.onNotice(blocked ?? 'История предыдущего сеанса или роли недоступна.');
                if (distance) {
                    state.bounce = state.entry.index;
                    window.history.go(distance);
                }
                else
                    persist(state.entry);
                return;
            }
            capture(false);
            const view = resolve(target, state);
            if (!view) {
                // Skip explicitly closed tabs and stale unsaved drafts. Do not recreate
                // a creation/copy intent or restore permissions from history.state.
                if (delta > 0 && target.index >= state.entry.ceiling) {
                    state.bounce = state.entry.index;
                    window.history.go(-delta);
                }
                else if (delta)
                    window.history.go(delta > 0 ? 1 : -1);
                else
                    persist(state.entry);
                return;
            }
            const key = keyForView(view), scroll = state.positions.get(key) ?? target.scroll;
            state.stopScroll?.();
            state.entry = { ...target, ceiling: Math.max(state.entry.ceiling, target.ceiling) };
            state.pending = { key, scroll };
            state.bounce = null;
            args.onRestore(view);
        }
        let scrollTimer: ReturnType<typeof setTimeout> | null = null;
        const scroll = () => {
            capture(false);
            if (scrollTimer === null)
                scrollTimer = setTimeout(() => {
                    scrollTimer = null;
                    const state = controller.current;
                    if (state && latest.current.enabled && !state.pending && state.bounce === null && state.entry.key === keyForView(latest.current.view)
                        && Date.now() - lastPersist.current >= 450)
                        persist(state.entry);
                }, 500);
        };
        const beforeUnload = () => capture(true);
        window.addEventListener('popstate', pop);
        window.addEventListener('scroll', scroll, true);
        window.addEventListener('beforeunload', beforeUnload);
        return () => {
            window.removeEventListener('popstate', pop);
            window.removeEventListener('scroll', scroll, true);
            window.removeEventListener('beforeunload', beforeUnload);
            if (scrollTimer !== null)
                clearTimeout(scrollTimer);
            controller.current?.stopScroll?.();
            window.history.scrollRestoration = previousRestoration ?? 'auto';
        };
    }, []);
    return { captureBeforeNavigation: () => capture(true) };
}
