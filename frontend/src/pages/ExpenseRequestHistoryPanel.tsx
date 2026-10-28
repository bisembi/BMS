import { ExpenseRequestHistoryHeader } from '../components/expense-request-history/ExpenseRequestHistoryHeader';
import { ExpenseRequestHistoryList } from '../components/expense-request-history/ExpenseRequestHistoryList';
import { ExpenseRequestHistoryMore } from '../components/expense-request-history/ExpenseRequestHistoryMore';
import { ExpenseRequestHistoryStatus } from '../components/expense-request-history/ExpenseRequestHistoryStatus';
import { useExpenseRequestHistory } from '../hooks/useExpenseRequestHistory';
export { formatHistoryUtc } from '../components/expense-request-history/expenseRequestHistoryFormat';
type Props = {
    requestId: string | null;
    invalidationToken: number;
    labelledBy?: string;
};
export function ExpenseRequestHistoryPanel({ requestId, invalidationToken, labelledBy }: Props) {
    const history = useExpenseRequestHistory(requestId, invalidationToken);
    if (!requestId) {
        return (<section id="expense-panel-history" role={labelledBy ? 'region' : 'tabpanel'} aria-labelledby={labelledBy ?? 'expense-tab-history'} className="form-card history-card">
        <div className="section-title">История изменений</div>
        <p className="history-empty">Сохраните черновик, чтобы появилась история изменений.</p>
      </section>);
    }
    return (<section id="expense-panel-history" role={labelledBy ? 'region' : 'tabpanel'} aria-labelledby={labelledBy ?? 'expense-tab-history'} className="form-card history-card">
      <ExpenseRequestHistoryHeader showCount={history.loadState === 'success'} shown={history.items.length} total={history.total}/>

      <ExpenseRequestHistoryStatus loadState={history.loadState} loadError={history.loadError} isEmpty={history.items.length === 0} onRetry={history.retry}/>

      {history.items.length > 0 && <ExpenseRequestHistoryList items={history.items}/>}

      <ExpenseRequestHistoryMore loadState={history.loadState} shown={history.items.length} total={history.total} isLoadingMore={history.isLoadingMore} loadMoreError={history.loadMoreError} onLoadMore={() => void history.loadMore()}/>
    </section>);
}
