import { useRef } from 'react';
import '../../styles/list-columns.css';
export const businessColumnDefinitions = [
    { key: 'contract', label: 'Договор' },
    { key: 'basis', label: 'Основание оплаты' },
    { key: 'paid', label: 'Погашено в валюте РЗ' },
    { key: 'remaining', label: 'Осталось оплатить в валюте РЗ' },
    { key: 'fullyPaidDate', label: 'Дата полной оплаты' },
    { key: 'closing', label: 'Состояние закрытия' },
] as const;
export type BusinessColumnKey = typeof businessColumnDefinitions[number]['key'];
/** Local presentation choices; no business payload or identity is persisted. */
export function ExpenseRequestListColumnPicker({ selected, onChange }: {
    selected: readonly BusinessColumnKey[];
    onChange: (columns: BusinessColumnKey[]) => void;
}) {
    const details = useRef<HTMLDetailsElement>(null);
    return (<details className="list-column-picker" ref={details} onKeyDown={(event) => {
            if (event.key !== 'Escape' || !details.current?.open)
                return;
            event.preventDefault();
            details.current.open = false;
            details.current.querySelector('summary')?.focus();
        }}>
      <summary>Колонки{selected.length > 0 ? `: +${selected.length}` : ''}</summary>
      <fieldset>
        <legend>Дополнительные колонки</legend>
        <div className="list-column-options">
          {businessColumnDefinitions.map(({ key, label }) => (<label key={key}>
              <input type="checkbox" checked={selected.includes(key)} onChange={(event) => onChange(event.target.checked ? [...selected, key] : selected.filter((item) => item !== key))}/>
              <span>{label}</span>
            </label>))}
        </div>
        <div className="list-column-buttons">
          <button className="secondary-button compact" type="button" onClick={() => onChange(businessColumnDefinitions.map((item) => item.key))}>Показать все</button>
          <button className="secondary-button compact" type="button" disabled={selected.length === 0} onClick={() => onChange([])}>Сбросить колонки</button>
        </div>
      </fieldset>
    </details>);
}
