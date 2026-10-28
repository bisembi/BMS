import { Plus, Trash2 } from 'lucide-react';
import { maximumBusinessTripEmployees } from '../../utils/expensePaymentOperation';
import './BusinessTripEmployees399.css';
/** Names are requisites only: never split or change the request's amounts. */
export function BusinessTripEmployees399({ names, onChange, viewOnly = false }: {
    names?: readonly string[] | null;
    onChange?: (names: readonly string[]) => void;
    viewOnly?: boolean;
}) {
    const employees = names?.length ? names : [''];
    return <div className="business-trip-employees">
    <ol aria-label="Сотрудники" className="business-trip-employee-list">
      {employees.map((name, index) => <li key={index}>
        {viewOnly ? <div className="expense-view-value" aria-label={`ФИО сотрудника, строка ${index + 1}`}>{name || 'Не указано'}</div>
                : <div className="business-trip-employee-editor">
            <label className="field required"><span>ФИО сотрудника</span>
              <input aria-label={`ФИО сотрудника, строка ${index + 1}`} value={name} maxLength={150} autoComplete="off" onChange={event => onChange?.(employees.map((value, i) => i === index ? event.target.value : value))}/>
            </label>
            <button type="button" className="secondary-button compact" disabled={employees.length === 1} aria-label={`Удалить сотрудника, строка ${index + 1}`} title="Удалить сотрудника" onClick={() => onChange?.(employees.filter((_, i) => i !== index))}><Trash2 size={15} aria-hidden="true"/></button>
          </div>}
      </li>)}
    </ol>
    {!viewOnly && <button type="button" className="secondary-button compact" disabled={employees.length >= maximumBusinessTripEmployees} onClick={() => onChange?.([...employees, ''])}>
      <Plus size={15} aria-hidden="true"/>Добавить сотрудника
    </button>}
  </div>;
}
