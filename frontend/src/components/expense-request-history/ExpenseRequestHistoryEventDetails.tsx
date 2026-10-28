import { changeKindLabels } from './expenseRequestHistoryFormat';
import { expenseRequestHistoryStatusLabel } from '../../expenseRequestStatus';
import type { ExpenseRequestHistoryEvent } from '../../types';
type Props = {
    event: ExpenseRequestHistoryEvent;
};
/// Safe projection of one event. Only the fields the server already exposes in
/// the history contract are rendered: no raw snapshots and no identifiers.
export function ExpenseRequestHistoryEventDetails({ event }: Props) {
    return (<div className="history-event-body">
      {event.requiresClientUpdate && (<p className="history-detail-note">Новое событие заявки. Для просмотра подробностей требуется обновление BMS. Остальные события истории доступны.</p>)}
      {!event.requiresClientUpdate && event.detailsStatus === 'unavailable' && event.changes.length === 0 && (<p className="history-detail-note">Безопасная детализация этого события недоступна.</p>)}
      {event.changes.length > 0 && (<div className="history-table-wrap">
          <table className="history-table">
            <caption className="sr-only">Изменения события «{event.title}»</caption>
        <thead><tr><th scope="col">Раздел</th><th scope="col">Поле</th><th scope="col">Изменение</th><th scope="col">Было</th><th scope="col">Стало</th></tr></thead>
            <tbody>
              {event.changes.map((change, changeIndex) => (<tr key={`${change.section}-${change.field}-${changeIndex}`}>
                  <td>{change.section}</td>
                  <td>{change.field}</td>
                  <td>{changeKindLabels[change.changeKind]}</td>
                  <td>{change.field === 'Статус' ? expenseRequestHistoryStatusLabel(change.before) : change.before ?? '—'}</td>
                  <td>{change.field === 'Статус' ? expenseRequestHistoryStatusLabel(change.after) : change.after ?? '—'}</td>
                </tr>))}
            </tbody>
          </table>
        </div>)}
      {event.omittedChanges > 0 && (<p className="history-detail-note">Сервер не включил в представление ещё {event.omittedChanges} изменений.</p>)}
      {event.detailsStatus === 'partial' && (<p className="history-detail-note">Показана только безопасно распознанная часть изменений.</p>)}
    </div>);
}
