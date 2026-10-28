import { createContext, useCallback, useContext, useLayoutEffect, useRef } from 'react';
type Channel = {
    viewKey: string;
    scopeKey: string | null;
    isScopeCurrent: (scope: string) => boolean;
    setLock: (view: string, scope: string, owner: symbol, locked: boolean) => boolean;
    hasViewCommands: () => boolean;
};
export const WorkspaceCommandLockContext = createContext<Channel | null>(null);
type Ticket = {
    channel: Channel | null;
    view: string;
    scope: string;
    entity: string;
};
/** Private attempts stay in their form; the shell retains only ownership metadata. */
export function useCommandLock(entity: string) {
    const channel = useContext(WorkspaceCommandLockContext);
    const live = useRef({ channel, entity });
    live.current = { channel, entity };
    const owner = useRef(Symbol('workspace-command'));
    const mounted = useRef(true);
    const held = useRef<Ticket | null>(null);
    const busy = useRef(false);
    const isCurrent = useCallback((ticket: Ticket) => {
        const current = live.current;
        return mounted.current && held.current === ticket && current.entity === ticket.entity
            && (current.channel?.viewKey ?? 'standalone') === ticket.view
            && (current.channel ? current.channel.scopeKey === ticket.scope
                && current.channel.isScopeCurrent(ticket.scope) : ticket.channel === null);
    }, []);
    const release = useCallback((ticket: Ticket) => {
        if (held.current !== ticket)
            return;
        held.current = null;
        busy.current = false;
        ticket.channel?.setLock(ticket.view, ticket.scope, owner.current, false);
    }, []);
    useLayoutEffect(() => {
        mounted.current = true;
        return () => {
            mounted.current = false;
            if (held.current)
                release(held.current);
        };
    }, [release]);
    const acquire = useCallback((): Ticket | null => {
        const { channel: current, entity: currentEntity } = live.current;
        if (!mounted.current || busy.current || (current && (current.scopeKey === null
            || !current.isScopeCurrent(current.scopeKey))))
            return null;
        const ticket = held.current ?? { channel: current, view: current?.viewKey ?? 'standalone',
            scope: current?.scopeKey ?? 'standalone', entity: currentEntity };
        if (held.current && !isCurrent(ticket))
            return null;
        if (!held.current && current && !current.setLock(ticket.view, ticket.scope, owner.current, true))
            return null;
        held.current = ticket;
        busy.current = true;
        return ticket;
    }, [isCurrent]);
    const retain = useCallback((ticket: Ticket) => {
        if (held.current === ticket)
            busy.current = false;
    }, []);
    return { acquire, retain, release, isCurrent,
        hasViewCommands: () => live.current.channel?.hasViewCommands() ?? false };
}
