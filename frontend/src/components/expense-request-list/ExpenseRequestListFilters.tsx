import { SlidersHorizontal } from 'lucide-react';
import { useId, useState, type KeyboardEvent } from 'react';
import { expenseRequestStatusCodes, expenseRequestStatusLabel } from '../../expenseRequestStatus';
import type { ReferenceDataItem } from '../../types';
import type { ExpenseRequestListFilterState } from '../../utils/expenseRequestListFilters';
import { referenceToken, initiatorToken } from '../../utils/expenseRequestListFilters';
import type { ExpenseRequestInitiatorOption } from '../../api/expenseRequestInitiators';
import { parseExpenseRequestStatusFilter, parseExpenseRequestUrgencyFilter } from '../../utils/expenseRequestSelectValues';
type Props = {
    disclosure?: {
        expanded: boolean;
        panelId: string;
    };
    draft: ExpenseRequestListFilterState;
    onChange: (patch: Partial<ExpenseRequestListFilterState>) => void;
    onApply: () => void;
    onReset: () => void;
    organizations: readonly ReferenceDataItem[];
    costCenters: readonly ReferenceDataItem[];
    counterparties: readonly ReferenceDataItem[];
    initiators?: readonly ExpenseRequestInitiatorOption[];
    activeFilterCount: number;
    error: string | null;
};
export function ExpenseRequestListFilters({ disclosure, draft, onChange, onApply, onReset, organizations, costCenters, counterparties, initiators = [], activeFilterCount, error, }: Props) {
    const [localExpanded, setExpanded] = useState(true);
    const localPanelId = useId();
    const expanded = disclosure?.expanded ?? localExpanded;
    const panelId = disclosure?.panelId ?? localPanelId;
    const applyOnEnter = (event: KeyboardEvent<HTMLFieldSetElement>) => {
        if (event.key !== 'Enter')
            return;
        event.preventDefault();
        onApply();
    };
    return (<section className="list-filter-panel" aria-label="Фильтры списка расходных заявок" hidden={disclosure !== undefined && !expanded}>
      {!disclosure && <div className="list-filter-bar">
        <button className="secondary-button compact" type="button" aria-expanded={expanded} aria-controls={panelId} onClick={() => setExpanded((current) => !current)}>
          <SlidersHorizontal size={15} aria-hidden="true"/>
          {expanded ? 'Свернуть фильтры' : 'Фильтры'}
        </button>
        {activeFilterCount > 0 && (<span className="list-filter-count" aria-live="polite">
            Применено фильтров: {activeFilterCount}
          </span>)}
      </div>}

      <div className="list-filter-body" id={panelId} hidden={!expanded}>
        <fieldset className="list-filter-fields" aria-label="Параметры фильтрации" onKeyDown={applyOnEnter}>
          <label className="list-filter-field">
            <span>Организация</span>
            <select aria-label="Организация" value={draft.organizationToken} onChange={event => onChange({ organizationToken: event.target.value })}>
              <option value="">Все организации</option>
              {organizations.map(organization => <option key={organization.id} value={referenceToken(organization)}>{organization.name}</option>)}
            </select>
          </label>

          <label className="list-filter-field">
            <span>Создана с (UTC+5)</span>
            <input aria-label="Создана с (UTC+5)" type="date" value={draft.createdFromDate} max={draft.createdThroughDate || undefined} onChange={(event) => onChange({ createdFromDate: event.target.value })}/>
          </label>

          <label className="list-filter-field">
            <span>Создана по (UTC+5)</span>
            <input aria-label="Создана по (UTC+5)" type="date" value={draft.createdThroughDate} min={draft.createdFromDate || undefined} onChange={(event) => onChange({ createdThroughDate: event.target.value })}/>
          </label>

          <label className="list-filter-field">
            <span>Срочность</span>
            <select aria-label="Срочность" value={draft.urgency} onChange={(event) => onChange({
            urgency: parseExpenseRequestUrgencyFilter(event.target.value),
        })}>
              <option value="all">Все</option>
              <option value="urgent">Срочные</option>
              <option value="normal">Обычные</option>
            </select>
          </label>

          <label className="list-filter-field">
            <span>ЦФО</span>
            <select aria-label="ЦФО" value={draft.costCenterToken} onChange={(event) => onChange({ costCenterToken: event.target.value })}>
              <option value="">Все ЦФО</option>
              {costCenters.map((costCenter) => (<option key={costCenter.id} value={referenceToken(costCenter)}>
                  {costCenter.name}
                </option>))}
            </select>
          </label>

          <label className="list-filter-field">
            <span>Контрагент</span>
            <select aria-label="Контрагент" value={draft.counterpartyToken} onChange={(event) => onChange({ counterpartyToken: event.target.value })}>
              <option value="">Все контрагенты</option>
              {counterparties.map((counterparty) => (<option key={counterparty.id} value={referenceToken(counterparty)}>
                  {counterparty.name}
                </option>))}
            </select>
          </label>

          <label className="list-filter-field">
            <span>Инициатор</span>
            <select aria-label="Инициатор" value={draft.initiator} onChange={(event) => onChange({ initiator: event.target.value })}>
              <option value="">Все сотрудники</option>
              {initiators.map(employee => <option key={employee.id} value={initiatorToken(employee)}>{employee.name}</option>)}
            </select>
          </label>

          <label className="list-filter-field">
            <span>Статус</span>
            <select aria-label="Статус" value={draft.status} onChange={(event) => onChange({
            status: parseExpenseRequestStatusFilter(event.target.value),
        })}>
              <option value="">Все статусы</option>
              {expenseRequestStatusCodes.map((status) => (<option key={status} value={status}>{expenseRequestStatusLabel(status)}</option>))}
            </select>
          </label>

          <label className="list-filter-field">
            <span>Планируемая оплата</span>
            <input aria-label="Планируемая оплата" type="date" value={draft.plannedPaymentDate} onChange={(event) => onChange({ plannedPaymentDate: event.target.value })}/>
          </label>
        </fieldset>

        <div className="list-filter-actions">
          <button className="primary-button compact" type="button" onClick={onApply}>
            Применить
          </button>
          <button className="secondary-button compact" type="button" onClick={onReset}>
            Сбросить
          </button>
        </div>

        {error !== null && (<p className="list-filter-error" role="alert">{error}</p>)}
      </div>
    </section>);
}
