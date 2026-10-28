import { useId } from 'react';
import './departments475.css';
export function DepartmentKind475({ costOnly, disabled, onChange }: {
    costOnly: boolean;
    disabled: boolean;
    onChange: (costOnly: boolean) => void;
}) {
    const name = useId();
    return <fieldset className="department-kind475" disabled={disabled}>
    <legend className="sr-only">Вид подразделений</legend>
    <label><input type="radio" name={name} checked={!costOnly} onChange={() => onChange(false)}/>ЦФО</label>
    <label><input type="radio" name={name} checked={costOnly} onChange={() => onChange(true)}/>Подразделения затрат</label>
  </fieldset>;
}
