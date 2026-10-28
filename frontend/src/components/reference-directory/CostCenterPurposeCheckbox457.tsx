import { useEffect, useState } from 'react';
import { useFrozenCreate } from '../../hooks/useFrozenCreate';
import { budgetDirectoryErrorMessage, isBudgetDirectoryResultUncertain, setCostCenterPurpose457, type BudgetDirectoryOption, type SetCostCenterPurpose457Input } from '../../api/budgetDirectory';
export function CostCenterPurposeCheckbox457({ option, disabled, canEdit, onSaved, onLock }: {
    option: BudgetDirectoryOption;
    disabled: boolean;
    canEdit: boolean;
    onSaved: () => void;
    onLock: (id: string, locked: boolean) => void;
}) {
    const command = useFrozenCreate<SetCostCenterPurpose457Input>('cost-center-purpose:' + option.id, isBudgetDirectoryResultUncertain);
    const [confirmed, setConfirmed] = useState(option.onlyCostDepartment ?? false);
    const [error, setError] = useState<string | null>(null);
    useEffect(() => { setConfirmed(option.onlyCostDepartment ?? false); }, [option.onlyCostDepartment]);
    useEffect(() => { onLock(option.id, command.blocked); return () => onLock(option.id, false); }, [option.id, command.blocked, onLock]);
    async function save(checked: boolean) {
        const result = await command.run({ expectedConcurrencyStamp: option.purposeConcurrencyStamp ?? null, onlyCostDepartment: checked }, (input, key) => setCostCenterPurpose457(option.id, input, key));
        if (result.kind === 'confirmed') {
            setConfirmed(result.value.onlyCostDepartment ?? false);
            setError(null);
            onSaved();
        }
        else if (result.kind !== 'inactive')
            setError(budgetDirectoryErrorMessage(result.error));
    }
    return <>
    <input type="checkbox" aria-label={`Только подразделение затрат: ${option.code}`} checked={confirmed} disabled={!canEdit || disabled || command.blocked} onChange={e => void save(e.target.checked)}/>
    {command.unknown && <button type="button" disabled={!canEdit || command.busy} onClick={() => void save(confirmed)}>
      Повторить ту же попытку</button>}
    {error && <span role="alert">{error}</span>}
  </>;
}
