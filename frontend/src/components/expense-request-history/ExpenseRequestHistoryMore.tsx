import type { HistoryLoadState } from '../../hooks/useExpenseRequestHistory';
type Props = {
    loadState: HistoryLoadState;
    shown: number;
    total: number;
    isLoadingMore: boolean;
    loadMoreError: string | null;
    onLoadMore: () => void;
};
/// Continuation control. After a failed continuation only the retry stays
/// visible, so one click never queues two follow-up pages.
export function ExpenseRequestHistoryMore({ loadState, shown, total, isLoadingMore, loadMoreError, onLoadMore, }: Props) {
    return (<>
      {loadMoreError && (<div className="history-error" role="alert">
          <span>{loadMoreError}</span>
          <button className="secondary-button compact" type="button" onClick={onLoadMore}>Повторить загрузку</button>
        </div>)}
      {loadState === 'success' && shown < total && !loadMoreError && (<button className="secondary-button history-more" type="button" disabled={isLoadingMore} onClick={onLoadMore}>
          {isLoadingMore ? 'Загрузка…' : 'Показать ещё'}
        </button>)}
    </>);
}
