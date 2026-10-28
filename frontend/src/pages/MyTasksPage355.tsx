import { useEffect, useState, type MouseEvent } from 'react';
import { RefreshCw } from 'lucide-react';
import { listMyApprovalTasks355, type MyApprovalTask355, type MyApprovalTaskPage355 } from '../api/myTasks355';
import { useWorkspaceRefreshAction, useWorkspaceRefreshBusy, useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import { ExpenseRequestListPagination } from '../components/expense-request-list/ExpenseRequestListPagination';
import { formatBusinessTimestamp } from '../utils/businessTime';
import { roleLabels } from '../utils/activeRole';
export function MyTasksPage355({ refreshKey, onOpenExpense, onOpenBudget, onOpenAdjustment, onRefreshRequested }: {
    refreshKey: string;
    onRefreshRequested?: () => void;
    onOpenExpense: (id: string) => void;
    onOpenBudget: (id: string, number: string) => void;
    onOpenAdjustment?: (id: string) => void;
}) {
    const revision = useWorkspaceRefreshRevision();
    const [skip, setSkip] = useState(0), take = 50;
    const [refresh, setRefresh] = useState(0);
    const [state, setState] = useState<{
        key: string;
        page: MyApprovalTaskPage355 | null;
        error: boolean;
    } | null>(null);
    const key = JSON.stringify([refreshKey, revision, refresh, skip]);
    const [loadingKey, setLoadingKey] = useState<string | null>(key);
    const current = state?.key === key ? state : null;
    const loading = loadingKey === key || current === null;
    const page = current?.page;
    useWorkspaceRefreshBusy(loading);
    useWorkspaceRefreshAction(() => onRefreshRequested?.());
    useEffect(() => { setSkip(0); }, [refreshKey]);
    useEffect(() => {
        const abort = new AbortController();
        setLoadingKey(key);
        void listMyApprovalTasks355(skip, take, abort.signal).then(page => {
            if (abort.signal.aborted)
                return;
            const lastSkip = page.total === 0 ? 0 : Math.floor((page.total - 1) / take) * take;
            if (skip > lastSkip) {
                setSkip(lastSkip);
                return;
            }
            setState({ key, page, error: false });
        }).catch(() => {
            if (!abort.signal.aborted)
                setState({ key, page: null, error: true });
        })
            .finally(() => {
            if (!abort.signal.aborted)
                setLoadingKey(null);
        });
        return () => abort.abort();
    }, [key, skip]);
    function open(event: MouseEvent<HTMLAnchorElement>, item: MyApprovalTask355) {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey)
            return;
        if (item.kind === 'BudgetAdjustment' && !onOpenAdjustment)
            return;
        event.preventDefault();
        if (item.kind === 'ExpenseRequest')
            onOpenExpense(item.id);
        else if (item.kind === 'BudgetAdjustment')
            onOpenAdjustment?.(item.id);
        else
            onOpenBudget(item.id, item.number);
    }
    return <div className="list-page">
    <div className="list-heading"><h1>Мои задачи</h1></div>
    <div className="list-toolbar">
      <button type="button" className="secondary-button compact" disabled={loading} onClick={() => { setRefresh(value => value + 1); onRefreshRequested?.(); }}>
        <RefreshCw size={15}/>Обновить
      </button>
      <span>Документы, ожидающие вашего согласования в выбранной роли.</span>
      <span className="list-toolbar-spacer"/>
      <ExpenseRequestListPagination skip={skip} take={take} total={page?.total ?? 0} itemCount={page?.items.length ?? 0} loading={loading} onSkipChange={setSkip}/>
    </div>
    {current?.error && <div role="alert" className="notice error">Не удалось загрузить мои задачи. Повторите загрузку.</div>}
    <div className="request-grid-wrap" aria-busy={loading}>
      <table className="request-grid" aria-label="Мои задачи на согласование">
        <thead><tr><th scope="col">Документ</th><th scope="col">Наименование</th><th scope="col">ЦФО</th>
          <th scope="col">Этап согласования</th><th scope="col">Дата</th></tr></thead>
        <tbody>{page?.items.map(item => <tr key={`${item.kind}:${item.id}`}>
          <td><a className="request-link" href={`?${item.kind === 'ExpenseRequest' ? 'expenseRequest' : item.kind === 'BudgetAdjustment' ? 'budgetAdjustment' : 'budgetInstance'}=${encodeURIComponent(item.id)}`} onClick={event => open(event, item)}>{item.kind === 'ExpenseRequest' ? 'Расходная заявка' : item.kind === 'BudgetAdjustment' ? 'Корректировка бюджета' : 'Бюджет'} № {item.number}</a></td>
          <td>{item.title.trim() || item.number}</td><td>{item.costCenterName || '—'}</td>
          <td>{roleLabels[item.roleCode]}</td><td><time dateTime={item.receivedAtUtc}>{formatBusinessTimestamp(item.receivedAtUtc)}</time></td>
        </tr>)}
          {loading && <tr><td colSpan={5} className="empty-grid">Загрузка…</td></tr>}
          {!loading && !current?.error && page?.items.length === 0 && <tr><td colSpan={5} className="empty-grid">
            Документов, ожидающих вашего согласования, нет.
          </td></tr>}
        </tbody>
      </table>
    </div>
    {page && <div className="list-summary">Показано на странице: {page.items.length}. Всего задач: {page.total}.</div>}
  </div>;
}
