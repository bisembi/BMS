import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { Trash2 } from 'lucide-react';
import { parentLabel } from './referenceDirectoryText';
import { CASH_FLOW_ACTIVITY_TYPE_LABELS } from '../../types';
import type { CashFlowActivityType, ReferenceDataItem } from '../../types';
type Props = {
    items: readonly ReferenceDataItem[];
    caption: string;
    showsHierarchy: boolean;
    showsParentCode?: boolean;
    showsCode?: boolean;
    showsBin?: boolean;
    showsSources?: boolean;
    showsNumericCode?: boolean;
    showsActivityType?: boolean;
    showsAccrualSettings?: boolean;
    showsRelatedCashFlowItems?: boolean;
    renderCostCenter?: (item: ReferenceDataItem) => ReactNode;
    renderOwner?: (item: ReferenceDataItem) => ReactNode;
    renderGrouping?: (item: ReferenceDataItem) => ReactNode;
    compactDelete?: boolean;
    actionsDisabled?: boolean;
    hierarchyHeading?: string;
    byId: Map<string, ReferenceDataItem>;
    onOpenItem?: (item: ReferenceDataItem, trigger: HTMLButtonElement) => void;
    onEditItem?: (item: ReferenceDataItem) => void;
    onDeleteItem?: (item: ReferenceDataItem) => void;
};
export function ReferenceDirectoryGrid({ items, caption, showsHierarchy, showsParentCode = true, showsCode, showsBin = false, showsSources = false, showsNumericCode = false, showsActivityType = false, showsAccrualSettings = false, showsRelatedCashFlowItems = false, renderCostCenter, renderOwner, renderGrouping, compactDelete = false, actionsDisabled = false, hierarchyHeading = 'Входит в', byId, onOpenItem, onEditItem, onDeleteItem, }: Props) {
    const displayCode = showsCode ?? (!showsBin && !showsNumericCode);
    const [pendingDeletionId, setPendingDeletionId] = useState<string | null>(null);
    useEffect(() => { setPendingDeletionId(null); }, [items, actionsDisabled]);
    return (<div className="directory-table-wrap">
      <table className={`directory-table${showsAccrualSettings ? ' cash-flow-directory-table' : ''}`} aria-label={caption}>
        <caption>{caption}</caption>
        {showsAccrualSettings && <colgroup>
          {displayCode && <col />}
          <col />
          {showsBin && <col />}
          {showsSources && <col />}
          {showsNumericCode && <col />}
          {showsActivityType && <col className="cash-flow-directory-activity-column"/>}
          {showsHierarchy && <col className="cash-flow-directory-group-column"/>}
          {renderCostCenter && <col className="cash-flow-directory-cfo-column"/>}
          <col className="cash-flow-directory-accrual-column"/>
          <col className="cash-flow-directory-expense-column"/>
          {(onEditItem || (!compactDelete && onDeleteItem)) && <col className="directory-edit-column"/>}
          {compactDelete && onDeleteItem && <col className="directory-delete-column"/>}
        </colgroup>}
        <thead>
          <tr>
            {displayCode && <th scope="col">Код</th>}
            <th scope="col">Наименование</th>
            {showsBin && <th scope="col">БИН</th>}
            {showsSources && <th scope="col">Источник</th>}
            {showsNumericCode && <th scope="col">Цифровой код валюты</th>}
            {showsActivityType && <th scope="col">Вид деятельности</th>}
            {showsHierarchy && <th scope="col">{hierarchyHeading}</th>}
            {renderOwner && <th scope="col">Владелец</th>}
            {renderCostCenter && <th scope="col">ЦФО</th>}
            {showsAccrualSettings && <><th scope="col">Акрул</th><th scope="col">Статья затрат</th></>}
            {showsRelatedCashFlowItems && <th scope="col">Соотносится со статьей ДДС</th>}
            {(onEditItem || (!compactDelete && onDeleteItem)) && <th scope="col">Действие</th>}
            {compactDelete && onDeleteItem && <th scope="col" className="directory-delete-cell" aria-label="Удаление"/>}
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (<tr key={item.id}>
              {displayCode && <td>{item.code}</td>}
              <td>{onOpenItem ? <button type="button" className="directory-item-link" disabled={actionsDisabled} onClick={event => onOpenItem(item, event.currentTarget)}>{item.name}</button> : item.name}</td>
              {showsBin && <td>{item.bin ?? '—'}</td>}
              {showsSources && <td>{item.sourceNames?.length ? item.sourceNames.join(', ') : '—'}</td>}
              {showsNumericCode && <td>{item.numericCode ?? '—'}</td>}
              {showsActivityType && <td>{item.activityType
                    ? CASH_FLOW_ACTIVITY_TYPE_LABELS[item.activityType as CashFlowActivityType] ?? '—'
                    : '—'}</td>}
              {showsHierarchy && <td>{renderGrouping ? renderGrouping(item) : parentLabel(item, byId, showsParentCode)}</td>}
              {renderOwner && <td>{renderOwner(item)}</td>}
              {renderCostCenter && <td>{renderCostCenter(item)}</td>}
              {showsAccrualSettings && <>
                <td className="directory-accrual-cell">{typeof item.formsManagerialAccrual === 'boolean'
                    ? <input type="checkbox" checked={item.formsManagerialAccrual} disabled aria-label={`Начисляется акрул: ${item.name}`}/> : '—'}</td>
                <td>{item.accrualExpenseItemName ?? '—'}</td>
              </>}
              {showsRelatedCashFlowItems && <td className="directory-expense-links">
                {item.relatedCashFlowItems === undefined ? 'Данные связей недоступны'
                    : item.relatedCashFlowItems.length === 0 ? '—'
                        : item.relatedCashFlowItems.map(link => <div key={link.code}>{link.code} {link.name}</div>)}
              </td>}
              {(onEditItem || (!compactDelete && onDeleteItem)) && <td className={compactDelete ? 'directory-edit-cell' : undefined}>
                {onEditItem && <button type="button" disabled={actionsDisabled} onClick={() => onEditItem(item)}>
                  Редактировать
                </button>}
                {!compactDelete && onDeleteItem && <button type="button" disabled={actionsDisabled} onClick={() => onDeleteItem(item)}>
                  Удалить
                </button>}
              </td>}
              {compactDelete && onDeleteItem && <td className="directory-delete-cell">
                <button type="button" className={`line-delete-button${pendingDeletionId === item.id ? ' is-pending' : ''}`} disabled={actionsDisabled} aria-label={pendingDeletionId === item.id
                    ? `Подтвердить удаление «${item.name}»` : `Удалить «${item.name}»`} title={pendingDeletionId === item.id
                    ? 'Нажмите ещё раз для удаления статьи' : 'Удалить статью — два нажатия'} onBlur={() => setPendingDeletionId(null)} onKeyDown={event => {
                    if (event.key === 'Escape')
                        setPendingDeletionId(null);
                }} onClick={() => {
                    if (actionsDisabled)
                        return;
                    if (pendingDeletionId === item.id) {
                        setPendingDeletionId(null);
                        onDeleteItem(item);
                    }
                    else
                        setPendingDeletionId(item.id);
                }}>
                  <Trash2 size={17} aria-hidden="true"/>
                </button>
              </td>}
            </tr>))}
        </tbody>
      </table>
    </div>);
}
