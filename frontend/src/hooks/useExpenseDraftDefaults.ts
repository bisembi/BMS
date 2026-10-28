import { useEffect, useState } from 'react';
import { getExpenseDraftDefaults, type ExpenseDraftDefaults } from '../api/expenseDraftDefaults';
import { useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
export function useExpenseDraftDefaults(currencyCode: string, actorId: string | null) {
    const refresh = useWorkspaceRefreshRevision();
    const [tick, setTick] = useState(0);
    useEffect(() => {
        const update = () => {
            if (document.visibilityState !== 'hidden')
                setTick(value => value + 1);
        };
        const timer = window.setInterval(update, 60000);
        window.addEventListener('focus', update);
        return () => { window.clearInterval(timer); window.removeEventListener('focus', update); };
    }, []);
    const [state, setState] = useState<{
        key: string;
        data: ExpenseDraftDefaults | null;
        loading: boolean;
        error: string | null;
    }>({ key: '', data: null, loading: true, error: null });
    const key = `${actorId}\0${currencyCode}\0${refresh}\0${tick}`;
    useEffect(() => {
        const abort = new AbortController();
        setState({ key, data: null, loading: true, error: null });
        void getExpenseDraftDefaults(currencyCode, abort.signal).then(data => {
            if (!abort.signal.aborted)
                setState({ key, data, loading: false, error: null });
        }).catch(() => {
            if (!abort.signal.aborted)
                setState({ key, data: null, loading: false, error: 'Не удалось загрузить последний курс. Проверьте справочник «Курсы валют».' });
        });
        return () => abort.abort();
    }, [key, currencyCode]);
    return state.key === key ? state : { key, data: null, loading: true, error: null };
}
