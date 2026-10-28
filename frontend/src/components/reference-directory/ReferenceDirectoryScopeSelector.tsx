import { ReferenceDirectoryError } from './ReferenceDirectoryError';
import type { ReferenceDataItem } from '../../types';
import { CashFlowItemCombobox } from './CashFlowItemCombobox';
import { matchesCounterpartyNameMask } from '../expense-request-form/CounterpartyCombobox';
const counterpartyName = (item: ReferenceDataItem) => item.name;
type Props = {
    organizations: readonly ReferenceDataItem[];
    counterparties: readonly ReferenceDataItem[];
    organizationIndex: number;
    counterpartyIndex: number;
    loading: boolean;
    error: string | null;
    onOrganizationIndexChange: (index: number) => void;
    onCounterpartyIndexChange: (index: number) => void;
    onRetry: () => void;
};
/// Scope callbacks use original-array indexes, never filtered-result indexes.
export function ReferenceDirectoryScopeSelector({ organizations, counterparties, organizationIndex, counterpartyIndex, loading, error, onOrganizationIndexChange, onCounterpartyIndexChange, onRetry, }: Props) {
    return (<div className="directory-scope">
      {loading && <p role="status">Загрузка организаций и контрагентов…</p>}
      {error !== null && <ReferenceDirectoryError message={error} onRetry={onRetry}/>}
      {!loading && error === null && (<div className="directory-scope-fields">
          <label className="field">
            <span>Организация</span>
            <select value={organizationIndex} onChange={(event) => onOrganizationIndexChange(Number(event.target.value))}>
              <option value={-1}>Не выбрана</option>
              {organizations.map((organization, index) => (<option key={organization.id} value={index}>
                  {organization.code} · {organization.name}
                </option>))}
            </select>
          </label>
          <label className="field">
            <span>Контрагент</span>
            <CashFlowItemCombobox ariaLabel="Контрагент" value={counterparties[counterpartyIndex]?.id ?? ''} items={counterparties} emptyLabel="Не выбран" unavailableLabel="Выбранный контрагент недоступен в справочнике" matchesItem={matchesCounterpartyNameMask} formatItem={counterpartyName} searchPlaceholder="Название или маска * ?" noMatchesLabel="Контрагенты не найдены" searchSelectsFirstMatch onChange={id => onCounterpartyIndexChange(id === '' ? -1
                : counterparties.findIndex(item => item.id.toLowerCase() === id.toLowerCase()))}/>
          </label>
        </div>)}
    </div>);
}
