import { Fragment } from 'react';
import { BusinessAuditDetailPanel } from './BusinessAuditDetailPanel';
import { formatUtc } from './businessAuditFormat';
import type { BusinessAuditMonitorDetail, BusinessAuditMonitorItem } from '../../types';
import { businessAuditReactKey, sameBusinessAuditIdentifier } from '../../utils/businessAuditIdentity';
type Props = {
    items: readonly BusinessAuditMonitorItem[];
    loading: boolean;
    error: string | null;
    selectedAuditId: string | null;
    detail: BusinessAuditMonitorDetail | null;
    detailLoading: boolean;
    detailError: string | null;
    onToggleDetail: (auditId: string) => void;
};
export function BusinessAuditGrid({ items, loading, error, selectedAuditId, detail, detailLoading, detailError, onToggleDetail, }: Props) {
    return (<div className="request-grid-wrap">
    <table className="request-grid" aria-label="События бизнес-аудита">
        <thead><tr><th scope="col">Время, UTC+5</th><th scope="col">Действие</th><th scope="col">Объект</th><th scope="col">ID объекта</th><th scope="col">Исполнитель</th><th scope="col">Снимки</th><th scope="col">Correlation ID</th><th scope="col">ID аудита</th><th scope="col">Детали</th></tr></thead>
      <tbody>
        {items.map((item) => (<Fragment key={businessAuditReactKey(item.auditId)}>
            <tr>
              <td>{formatUtc(item.occurredAtUtc)}</td>
              <td>{item.action}</td>
              <td>{item.objectType}</td>
              <td><code>{item.objectId}</code></td>
              <td>{item.actorExternalId}</td>
              <td>{item.hasBefore ? 'До и после' : item.hasAfter ? 'После' : 'Нет'}</td>
              <td><code>{item.correlationId}</code></td>
              <td><code>{item.auditId}</code></td>
              <td><button className="secondary-button compact" type="button" onClick={() => void onToggleDetail(item.auditId)}>{sameBusinessAuditIdentifier(selectedAuditId, item.auditId) ? 'Скрыть' : 'Подробнее'}</button></td>
            </tr>
            {sameBusinessAuditIdentifier(selectedAuditId, item.auditId) && (<tr className="audit-detail-row">
                <td colSpan={9}>
                  <BusinessAuditDetailPanel auditId={item.auditId} detail={detail} loading={detailLoading} error={detailError}/>
                </td>
              </tr>)}
          </Fragment>))}
        {!loading && items.length === 0 && !error && (<tr><td colSpan={9} className="empty-grid">Событий аудита пока нет.</td></tr>)}
        {loading && <tr><td colSpan={9} className="empty-grid">Загрузка…</td></tr>}
      </tbody>
    </table>
  </div>);
}
