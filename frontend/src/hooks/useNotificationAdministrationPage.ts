import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import type { NotificationAdministrationPage } from '../api/notificationAdministration';
const pageSize = 25;
export function useNotificationAdministrationPage<T>(load: (skip: number, take: number, signal?: AbortSignal) => Promise<NotificationAdministrationPage<T>>, reloadToken: number, restoreFocus?: () => void) {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [skip, setSkip] = useState(0);
    const [refresh, setRefresh] = useState(0);
    // Bind the focus request to the user-triggered load. A passive effect from
    // the preceding page must not be able to consume a later paging request.
    const focusAfterLoadKey = useRef<string | null>(null);
    const key = `${skip}:${reloadToken}:${refresh}`;
    const [state, setState] = useState<{
        key: string;
        page: NotificationAdministrationPage<T> | null;
        error: boolean;
    }>({ key: '', page: null, error: false });
    useEffect(() => {
        const controller = new AbortController();
        let current = true;
        setState({ key, page: null, error: false });
        void load(skip, pageSize, controller.signal).then((result) => {
            if (current)
                setState({ key, page: result, error: false });
        }).catch(() => {
            if (current)
                setState({ key, page: null, error: true });
        });
        return () => { current = false; controller.abort(); };
    }, [key, load, skip, workspaceRefresh]);
    const visible = state.key === key ? state.page : null;
    const error = state.key === key && state.error;
    useEffect(() => {
        if ((!visible && !error) || focusAfterLoadKey.current !== key)
            return;
        focusAfterLoadKey.current = null;
        restoreFocus?.();
    }, [error, key, restoreFocus, visible]);
    const move = (nextSkip: number) => {
        focusAfterLoadKey.current = `${nextSkip}:${reloadToken}:${refresh}`;
        setSkip(nextSkip);
    };
    useWorkspaceRefreshBusy(!visible && !error);
    return {
        visible, error, loading: !visible && !error,
        pageNumber: Math.floor(skip / pageSize) + 1,
        canGoBack: skip > 0, canGoNext: visible !== null && skip + visible.items.length < visible.total,
        previous: () => move(Math.max(0, skip - pageSize)),
        next: () => move(skip + pageSize),
        reload: () => {
            const nextRefresh = refresh + 1;
            focusAfterLoadKey.current = `0:${reloadToken}:${nextRefresh}`;
            setSkip(0);
            setRefresh(nextRefresh);
        },
    };
}
