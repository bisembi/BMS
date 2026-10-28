import type { FormEvent } from 'react';
import type { FilterDraft } from './businessAuditFormat';
type Props = {
    draft: FilterDraft;
    loading: boolean;
    clearDisabled: boolean;
    onChange: (patch: Partial<FilterDraft>) => void;
    onSubmit: (event: FormEvent<HTMLFormElement>) => void;
    onClear: () => void;
};
export function BusinessAuditFilterForm({ draft, loading, clearDisabled, onChange, onSubmit, onClear, }: Props) {
    return (<form className="audit-filter-form" aria-label="Фильтры бизнес-аудита" onSubmit={onSubmit}>
    <label>Исполнитель<input value={draft.actorExternalId} maxLength={256} onChange={(event) => onChange({ actorExternalId: event.target.value })}/></label>
    <label>Тип объекта<input value={draft.objectType} maxLength={100} placeholder="ExpenseRequest" onChange={(event) => onChange({ objectType: event.target.value })}/></label>
    <label>ID объекта<input value={draft.objectId} maxLength={100} onChange={(event) => onChange({ objectId: event.target.value })}/></label>
    <label>Действие<input value={draft.action} maxLength={100} placeholder="DraftUpdated" onChange={(event) => onChange({ action: event.target.value })}/></label>
    <label>Correlation ID<input value={draft.correlationId} maxLength={64} onChange={(event) => onChange({ correlationId: event.target.value })}/></label>
    <label>С, UTC+5<input type="datetime-local" step="60" value={draft.fromUtc} onChange={(event) => onChange({ fromUtc: event.target.value })}/></label>
    <label>По, UTC+5<input type="datetime-local" step="60" value={draft.toUtc} onChange={(event) => onChange({ toUtc: event.target.value })}/></label>
    <div className="audit-filter-actions">
      <button className="primary-button compact" type="submit" disabled={loading}>Применить</button>
      <button className="secondary-button compact" type="button" onClick={onClear} disabled={loading || clearDisabled}>Сбросить</button>
    </div>
  </form>);
}
