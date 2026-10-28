import { ExpenseRequestHistoryEventDetails } from './ExpenseRequestHistoryEventDetails';
import { detailsStatusLabels, formatHistoryUtc } from './expenseRequestHistoryFormat';
import type { ExpenseRequestHistoryEvent } from '../../types';
import { approvalComment360 } from './approvalComment360';
type Props = {
    items: ExpenseRequestHistoryEvent[];
};
/// Event list in server order — newest first. The order is never re-sorted here.
export function ExpenseRequestHistoryList({ items }: Props) {
    return (<ol className="history-list" aria-label="События истории от новых к старым">
      {items.map((event, eventIndex) => {
            const comment = approvalComment360(event);
            const detailsEvent = comment === null ? event : { ...event,
                changes: event.changes.filter(change => !(change.section === 'Согласование' && change.field === 'Причина')) };
            const hasDetails = detailsEvent.changes.some(change => Boolean(change.before?.trim() || change.after?.trim()))
                || detailsEvent.omittedChanges > 0;
            const heading = <>
              <span className="history-event-title">{event.requiresClientUpdate ? 'Новое событие заявки' : event.title}</span>
              <time dateTime={event.occurredAtUtc}>{formatHistoryUtc(event.occurredAtUtc)}</time>
              <span className="history-actor">{event.actorLabel}</span>
              {comment !== null ? <span className="history-approval-comment360">{comment}</span>
                    : (hasDetails || event.detailsStatus !== 'complete') && <span className={hasDetails
                            ? `history-details-status ${event.detailsStatus}` : 'history-details-text360'}>{detailsStatusLabels[event.detailsStatus]}</span>}
        </>;
            return <li key={`${event.occurredAtUtc}-${eventIndex}`} className={`history-event ${event.kind}`}>
          {hasDetails ? <details><summary>{heading}</summary><ExpenseRequestHistoryEventDetails event={detailsEvent}/></details>
                    : <><div className="history-event-summary360">{heading}</div>
              {(event.requiresClientUpdate || event.detailsStatus !== 'complete') && <ExpenseRequestHistoryEventDetails event={detailsEvent}/>}</>}
        </li>;
        })}
    </ol>);
}
