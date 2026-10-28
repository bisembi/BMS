import type { HistoryLoadState } from '../../hooks/useExpenseRequestHistory';
type Props = {
    loadState: HistoryLoadState;
    loadError: string | null;
    isEmpty: boolean;
    onRetry: () => void;
};
/// Loading, error and empty states of the first page. Exactly one of them is
/// visible at a time, in the same order as before the decomposition.
export function ExpenseRequestHistoryStatus({ loadState, loadError, isEmpty, onRetry }: Props) {
    return (<>
      {loadState === 'loading' && <p className="history-loading" role="status">Загрузка истории…</p>}
      {loadState === 'error' && (<div className="history-error" role="alert">
          <span>{loadError}</span>
          <button className="secondary-button compact" type="button" onClick={onRetry}>Повторить</button>
        </div>)}
      {loadState === 'success' && isEmpty && <p className="history-empty">Событий пока нет.</p>}
    </>);
}
