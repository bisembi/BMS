import type { BudgetOwnerCostCenterAssignment } from '../../types';
export function BudgetOwnerCostCenterAssignmentGrid({ items, onEdit, onDelete, editable = false, }: {
    items: readonly BudgetOwnerCostCenterAssignment[];
    onEdit?: (item: BudgetOwnerCostCenterAssignment) => void;
    onDelete?: (item: BudgetOwnerCostCenterAssignment) => void;
    editable?: boolean;
}) {
    const caption = editable ? 'Владельцы бюджета по ЦФО' : 'Владельцы бюджета по ЦФО, только для просмотра';
    return (<div className="directory-table-wrap">
      <table className="directory-table" aria-label={caption}>
        <caption>{caption}</caption>
        <thead>
          <tr>
            <th scope="col">Код сотрудника</th>
            <th scope="col">Сотрудник</th>
            <th scope="col">Функциональная роль</th>
            <th scope="col">Код ЦФО</th>
            <th scope="col">ЦФО</th>
            {(onEdit || onDelete) && <th scope="col">Действие</th>}
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (<tr key={`${item.employeeCode}\u0000${item.costCenterCode}`}>
              <td>{item.employeeCode}</td>
              <td>{item.employeeName}</td>
              <td>Владелец бюджета</td>
              <td>{item.costCenterCode}</td>
              <td>{item.costCenterName}</td>
              {(onEdit || onDelete) && <td>
                {onEdit && <button type="button" onClick={() => onEdit(item)}>Редактировать</button>}
                {onDelete && <button type="button" onClick={() => onDelete(item)}>Удалить</button>}
              </td>}
            </tr>))}
        </tbody>
      </table>
    </div>);
}
