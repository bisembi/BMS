import { RefreshCw } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { BusinessAuditFilterForm } from '../components/business-audit/BusinessAuditFilterForm';
import { BusinessAuditGrid } from '../components/business-audit/BusinessAuditGrid';
import { BusinessAuditPagination } from '../components/business-audit/BusinessAuditPagination';
import { emptyFilterDraft, type FilterDraft } from '../components/business-audit/businessAuditFormat';
import { useBusinessAuditDetail } from '../hooks/useBusinessAuditDetail';
import { useBusinessAuditMonitorList } from '../hooks/useBusinessAuditMonitorList';
import type { BusinessAuditSearchFilters } from '../types';
import { businessLocalInputToUtc } from '../utils/businessTime';
export function BusinessAuditMonitorPage() {
    const [filterDraft, setFilterDraft] = useState<FilterDraft>(emptyFilterDraft);
    const [filterError, setFilterError] = useState('');
    // The detail reader is created first so the list reader can invalidate an
    // open detail at exactly the same point it did before the decomposition.
    const detail = useBusinessAuditDetail();
    const list = useBusinessAuditMonitorList(detail.invalidate);
    const applyFilters = (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        const filters: BusinessAuditSearchFilters = {};
        for (const key of ['actorExternalId', 'objectType', 'objectId', 'action', 'correlationId'] as const) {
            if (filterDraft[key] !== '')
                filters[key] = filterDraft[key];
        }
        try {
            if (filterDraft.fromUtc !== '')
                filters.fromUtc = businessLocalInputToUtc(filterDraft.fromUtc);
            if (filterDraft.toUtc !== '')
                filters.toUtc = businessLocalInputToUtc(filterDraft.toUtc);
            if (filters.fromUtc && filters.toUtc && filters.fromUtc > filters.toUtc)
                throw new Error();
        }
        catch {
            setFilterError('Проверьте период: начало должно быть не позже окончания.');
            return;
        }
        setFilterError('');
        list.setSkip(0);
        list.setAppliedFilters(Object.keys(filters).length === 0 ? null : filters);
    };
    const clearFilters = () => {
        setFilterError('');
        setFilterDraft(emptyFilterDraft);
        list.setSkip(0);
        list.setAppliedFilters(null);
    };
    return (<div className="list-page" aria-label="Специализированный бизнес-аудит">
      <div className="list-heading">
        <div>
          <h1>Специализированный бизнес-аудит</h1>
          <p className="muted">Технические идентификаторы доступны только здесь. Сырые снимки до и после не передаются в браузер.</p>
        </div>
      </div>

      <BusinessAuditFilterForm draft={filterDraft} loading={list.loading} clearDisabled={list.appliedFilters === null
            && Object.values(filterDraft).every((value) => value === '')} onChange={(patch) => setFilterDraft((current) => ({ ...current, ...patch }))} onSubmit={applyFilters} onClear={clearFilters}/>

      <div className="list-toolbar">
        <button className="secondary-button compact" type="button" onClick={() => void list.reload()} disabled={list.loading}>
          <RefreshCw size={15}/>Обновить
        </button>
        <span className="list-toolbar-spacer"/>
        <BusinessAuditPagination skip={list.skip} itemCount={list.items.length} hasMore={list.hasMore} loading={list.loading} onSkipChange={list.setSkip}/>
      </div>

      {filterError && <div className="notice error" role="alert">{filterError}</div>}
      {list.error && <div className="notice error" role="alert">{list.error}</div>}
      <BusinessAuditGrid items={list.items} loading={list.loading} error={list.error} selectedAuditId={detail.selectedAuditId} detail={detail.detail} detailLoading={detail.loading} detailError={detail.error} onToggleDetail={(auditId) => void detail.toggle(auditId)}/>
    </div>);
}
