import { changeKindLabels, detailsStatusLabels } from './businessAuditFormat';
import type { BusinessAuditMonitorDetail } from '../../types';
type Props = {
    auditId: string;
    detail: BusinessAuditMonitorDetail | null;
    loading: boolean;
    /// Only the fixed message produced by the reader is accepted.
    error: string | null;
};
/// Safe detail of one audit event. Raw before/after snapshots never reach the
/// browser, so only the server-projected changes are rendered.
export function BusinessAuditDetailPanel({ auditId, detail, loading, error }: Props) {
    return (<>
  {loading && <p className="history-loading" role="status">Загрузка безопасной детализации…</p>}
  {error && <div className="history-error" role="alert">{error}</div>}
  {detail && (<div className="history-event-body">
      <div className="lines-heading">
        <strong>{detail.title}</strong>
        <span className={`history-details-status ${detail.detailsStatus}`}>{detailsStatusLabels[detail.detailsStatus]}</span>
      </div>
      {detail.changes.length > 0 && (<div className="history-table-wrap">
          <table className="history-table">
            <caption className="sr-only">Безопасная детализация события {auditId}</caption>
        <thead><tr><th scope="col">Раздел</th><th scope="col">Поле</th><th scope="col">Изменение</th><th scope="col">Было</th><th scope="col">Стало</th></tr></thead>
            <tbody>{detail.changes.map((change, index) => (<tr key={`${change.section}-${change.field}-${index}`}>
                <td>{change.section}</td><td>{change.field}</td><td>{changeKindLabels[change.changeKind]}</td><td>{change.before ?? '—'}</td><td>{change.after ?? '—'}</td>
              </tr>))}</tbody>
          </table>
        </div>)}
      {detail.detailsStatus === 'unavailable' && <p className="history-detail-note">Безопасная детализация этого события недоступна.</p>}
      {detail.detailsStatus === 'partial' && <p className="history-detail-note">Показана только безопасно распознанная часть изменений.</p>}
      {detail.omittedChanges > 0 && <p className="history-detail-note">Сервер не включил ещё {detail.omittedChanges} изменений.</p>}
    </div>)}
    </>);
}
