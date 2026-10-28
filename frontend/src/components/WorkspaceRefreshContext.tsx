import { createContext, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
export type RefreshResult = {
    kind: 'started' | 'static' | 'blocked' | 'failed';
    message?: string;
};
type Action = () => void | Promise<unknown>;
type Guard = () => string | null;
type Entry = {
    refresh: () => Promise<RefreshResult>;
    busy: () => boolean;
    blockedReason?: () => string | null;
};
type Registry = {
    register: (key: string, owner: symbol, entry: Entry) => () => void;
    changed: () => void;
};
type Channel = {
    revision: number;
    registerGuard: (owner: symbol, guard: Guard) => () => void;
    registerAction: (owner: symbol, action: Action) => () => void;
    registerReader: (owner: symbol) => () => void;
    setBusy: (owner: symbol, busy: boolean) => () => void;
};
const Context = createContext<Channel | null>(null);
/** A read belongs to one persistent workspace, never to the whole application. */
export function useWorkspaceRefreshController(activeKey: string, scope: string | null, blocked: () => string | null, scrollTarget?: () => HTMLElement | null) {
    const entries = useRef(new Map<string, {
        owner: symbol;
        entry: Entry;
    }>());
    const live = useRef({ activeKey, scope, blocked, scrollTarget });
    live.current = { activeKey, scope, blocked, scrollTarget };
    const [revision, changed] = useState(0);
    const [dispatching, setDispatching] = useState(false);
    const [notice, rememberNotice] = useState<{
        key: string;
        scope: string | null;
        message: string | null;
    } | null>(null);
    const inFlight = useRef(false);
    const scroll = useRef<{
        key: string;
        scope: string | null;
        record: {
            owner: symbol;
            entry: Entry;
        };
        element: HTMLElement;
        top: number;
        left: number;
    } | null>(null);
    useEffect(() => {
        // A refresh may outlive a newer user scroll. Restore layout-induced resets,
        // but never drag the viewport back after subsequent user interaction.
        const cancelRestore = () => { scroll.current = null; };
        const inputs = ['wheel', 'touchstart', 'pointerdown', 'keydown'] as const;
        inputs.forEach(input => window.addEventListener(input, cancelRestore, { capture: true, passive: true }));
        return () => {
            scroll.current = null;
            inputs.forEach(input => window.removeEventListener(input, cancelRestore, true));
        };
    }, []);
    const registry = useMemo<Registry>(() => ({
        changed: () => changed(value => value + 1),
        register(key, owner, entry) {
            entries.current.set(key, { owner, entry });
            changed(value => value + 1);
            return () => {
                if (entries.current.get(key)?.owner === owner) {
                    entries.current.delete(key);
                    changed(value => value + 1);
                }
            };
        },
    }), []);
    const refresh = useCallback(async () => {
        if (inFlight.current)
            return;
        const capture = live.current;
        const setNotice = (message: string | null) => rememberNotice({ key: capture.activeKey, scope: capture.scope, message });
        const reason = capture.scope === null ? 'Текущий сеанс ещё не подтверждён.' : capture.blocked();
        if (reason) {
            setNotice(reason);
            return;
        }
        const record = entries.current.get(capture.activeKey);
        const entry = record?.entry;
        if (!entry) {
            setNotice('На текущей странице нет серверных данных для обновления.');
            return;
        }
        const element = capture.scrollTarget?.();
        if (element && record)
            scroll.current = { key: capture.activeKey, scope: capture.scope, record, element, top: element.scrollTop, left: element.scrollLeft };
        inFlight.current = true;
        setDispatching(true);
        setNotice(null);
        try {
            const result = await entry.refresh();
            if (live.current.activeKey !== capture.activeKey || live.current.scope !== capture.scope || entries.current.get(capture.activeKey) !== record)
                return;
            // A token starts asynchronous GET effects; it is not proof that they succeeded.
            setNotice(result.message ?? (result.kind === 'static' ? 'На текущей странице нет серверных данных для обновления.'
                : result.kind === 'failed' ? 'Не удалось обновить данные. Проверьте сообщение на текущей странице.'
                    : result.kind === 'blocked' ? 'Сначала сохраните или отмените изменения в текущей форме.' : null));
        }
        finally {
            inFlight.current = false;
            setDispatching(false);
        }
    }, []);
    const busyNow = dispatching || (entries.current.get(activeKey)?.entry.busy() ?? false);
    // Pure inspection for a role/session handoff; never starts reads or callbacks.
    const blockedReasonAll = useCallback(() => {
        if (inFlight.current)
            return 'Дождитесь завершения обновления страницы.';
        for (const record of entries.current.values()) {
            const reason = record.entry.blockedReason?.();
            if (reason)
                return reason;
        }
        return null;
    }, []);
    useLayoutEffect(() => {
        const saved = scroll.current;
        if (!saved)
            return;
        if (saved.key !== activeKey || saved.scope !== scope || entries.current.get(activeKey) !== saved.record) {
            scroll.current = null;
            return;
        }
        if (busyNow)
            return;
        // A token's passive effects can begin GETs after dispatch returns. Wait one
        // frame and restore only after every registered reader has settled.
        const frame = window.requestAnimationFrame(() => {
            if (scroll.current !== saved || saved.record.entry.busy() || inFlight.current)
                return;
            if (live.current.activeKey !== saved.key || live.current.scope !== saved.scope) {
                scroll.current = null;
                return;
            }
            saved.element.scrollTop = saved.top;
            saved.element.scrollLeft = saved.left;
            scroll.current = null;
        });
        return () => window.cancelAnimationFrame(frame);
    }, [activeKey, scope, busyNow, revision, dispatching]);
    return { registry, refresh, notice: notice?.key === activeKey && notice.scope === scope ? notice.message : null,
        busy: busyNow, blockedReasonAll };
}
export function WorkspaceRefreshProvider({ viewKey, registry, children }: {
    viewKey: string;
    registry: Registry;
    children: ReactNode;
}) {
    const [revision, setRevision] = useState(0);
    const owner = useRef(Symbol('workspace-refresh'));
    const guards = useRef(new Map<symbol, Guard>());
    const actions = useRef(new Map<symbol, Action>());
    const readers = useRef(new Set<symbol>());
    const busy = useRef(new Set<symbol>());
    const running = useRef(false);
    const mounted = useRef(false);
    const registrations = useMemo<Omit<Channel, 'revision'>>(() => ({
        registerGuard(id, guard) { guards.current.set(id, guard); return () => { guards.current.delete(id); }; },
        registerAction(id, action) { actions.current.set(id, action); return () => { actions.current.delete(id); }; },
        registerReader(id) { readers.current.add(id); return () => { readers.current.delete(id); }; },
        setBusy(id, loading) {
            if (loading)
                busy.current.add(id);
            else
                busy.current.delete(id);
            registry.changed();
            return () => {
                if (busy.current.delete(id))
                    registry.changed();
            };
        },
    }), [registry]);
    const channel = useMemo<Channel>(() => ({ ...registrations, revision }), [registrations, revision]);
    useLayoutEffect(() => {
        mounted.current = true;
        const unregister = registry.register(viewKey, owner.current, {
            busy: () => running.current || busy.current.size > 0,
            blockedReason: () => {
                if (!mounted.current || running.current || busy.current.size > 0)
                    return 'Дождитесь завершения загрузки открытых страниц.';
                for (const guard of guards.current.values()) {
                    const reason = guard();
                    if (reason)
                        return reason;
                }
                return null;
            },
            async refresh() {
                if (!mounted.current || running.current || busy.current.size > 0)
                    return { kind: 'blocked', message: 'Дождитесь завершения загрузки текущей страницы.' };
                for (const guard of guards.current.values()) {
                    const reason = guard();
                    if (reason)
                        return { kind: 'blocked', message: reason };
                }
                if (readers.current.size === 0 && actions.current.size === 0)
                    return { kind: 'static' };
                running.current = true;
                registry.changed();
                try {
                    // All guards pass before any reader or document callback starts.
                    await Promise.all([...actions.current.values()].map(action => action()));
                    if (mounted.current)
                        setRevision(value => value + 1);
                    return { kind: 'started' };
                }
                catch {
                    return { kind: 'failed' };
                }
                finally {
                    running.current = false;
                    if (mounted.current)
                        registry.changed();
                }
            },
        });
        return () => { mounted.current = false; unregister(); };
    }, [registry, viewKey]);
    return <Context.Provider value={channel}>{children}</Context.Provider>;
}
export function useWorkspaceRefreshRevision(): number {
    const channel = useContext(Context);
    const owner = useRef(Symbol('workspace-reader'));
    const register = channel?.registerReader;
    useLayoutEffect(() => register?.(owner.current), [register]);
    return channel?.revision ?? 0;
}
export function useWorkspaceRefreshGuard(reason: string | null | Guard) {
    const channel = useContext(Context);
    const live = useRef(reason);
    live.current = reason;
    const owner = useRef(Symbol('workspace-guard'));
    const register = channel?.registerGuard;
    useLayoutEffect(() => register?.(owner.current, () => typeof live.current === 'function' ? live.current() : live.current), [register]);
}
export function useWorkspaceRefreshAction(action: Action) {
    const channel = useContext(Context);
    const live = useRef(action);
    live.current = action;
    const owner = useRef(Symbol('workspace-action'));
    const register = channel?.registerAction;
    useLayoutEffect(() => register?.(owner.current, () => live.current()), [register]);
}
export function useWorkspaceRefreshBusy(loading: boolean) {
    const channel = useContext(Context);
    const owner = useRef(Symbol('workspace-read-busy'));
    const setBusy = channel?.setBusy;
    useLayoutEffect(() => setBusy?.(owner.current, loading), [loading, setBusy]);
}
