import { useExpenseRequestHistory } from '../../hooks/useExpenseRequestHistory';
import { approvalComment360 } from '../expense-request-history/approvalComment360';
import { formatBusinessTimestamp } from '../../utils/businessTime';
export function ApprovalComments360({ requestId, invalidationToken }: {
    requestId: string | null;
    invalidationToken: number;
}) {
    const history = useExpenseRequestHistory(requestId, invalidationToken, true);
    if (!requestId)
        return null;
    const comments = history.items.flatMap((event, index) => {
        const text = approvalComment360(event);
        return text === null ? [] : [{ event, text, index }];
    });
    if (history.loadState !== 'error' && comments.length === 0 && history.total === 0)
        return null;
    return <section className="approval-comments360" aria-label="Комментарии участников согласования">
    {comments.length > 0 && <><div className="approval-comments-heading360">Комментарии участников</div>
      <ul>{comments.map(({ event, text, index }) => <li key={`${event.occurredAtUtc}:${index}`}>
        <span>{event.actorLabel} · {formatBusinessTimestamp(event.occurredAtUtc)}:</span> <span>{text}</span>
      </li>)}</ul></>}
    {history.loadState === 'error' && <p role="status">Не удалось загрузить комментарии участников. <button type="button" className="secondary-button compact" onClick={history.retry}>Повторить загрузку комментариев</button></p>}
    {history.loadMoreError && <p role="status">Не удалось загрузить более ранние комментарии.</p>}
    {history.total > history.items.length && <button type="button" className="secondary-button compact" disabled={history.isLoadingMore} onClick={() => void history.loadMore()}>{history.isLoadingMore ? 'Загрузка…' : 'Более ранние комментарии'}</button>}
  </section>;
}
