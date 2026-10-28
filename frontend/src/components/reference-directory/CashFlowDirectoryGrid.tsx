import { useEffect, useId, useRef, useState } from 'react';
import { GripVertical, Trash2 } from 'lucide-react';
import { CASH_FLOW_ACTIVITY_TYPES, CASH_FLOW_ACTIVITY_TYPE_LABELS, type ReferenceDataItem } from '../../types';
import type { useCashFlowDirectoryEditor } from '../../hooks/useCashFlowDirectoryEditor';
type Props = {
    items: readonly ReferenceDataItem[];
    editing: boolean;
    disabled: boolean;
    choicesLoading: boolean;
    centers: readonly ReferenceDataItem[];
    expenses: readonly ReferenceDataItem[];
    editor: ReturnType<typeof useCashFlowDirectoryEditor>;
    onDelete: (item: ReferenceDataItem) => void;
    offset?: number;
    positions?: ReadonlyMap<string, number>;
    reorder?: {
        blocked: boolean;
        move: (source: string, target: string, placement: 'Before' | 'After') => void;
        total: number;
        moveToPosition: (source: string, position: number) => void;
    };
};
const missingClass = (missing: boolean) => missing ? 'cash-flow-directory-missing-cell' : undefined;
function OrderPositionInput({ item, position, reorder }: {
    item: ReferenceDataItem;
    position: number;
    reorder: NonNullable<Props['reorder']>;
}) {
    const [value, setValue] = useState('');
    const [error, setError] = useState(false);
    const errorId = useId();
    const maximum = Math.min(999, reorder.total);
    useEffect(() => { setValue(''); setError(false); }, [position]);
    return <span className="cash-flow-order-position-editor"><input type="text" inputMode="numeric" maxLength={3} className="cash-flow-order-position-input" placeholder="№" value={value} disabled={reorder.blocked} aria-label={`Переместить на позицию: ${item.name}`} aria-invalid={error || undefined} aria-describedby={error ? errorId : undefined} title={`Номер от 1 до ${maximum}; Enter — переместить`} onChange={event => {
            if (/^\d{0,3}$/.test(event.target.value)) {
                setValue(event.target.value);
                setError(false);
            }
        }} onKeyDown={event => {
            if (event.key === 'Escape') {
                event.preventDefault();
                setValue('');
                setError(false);
                return;
            }
            if (event.key !== 'Enter' || event.nativeEvent.isComposing)
                return;
            event.preventDefault();
            event.stopPropagation();
            if (event.repeat || reorder.blocked)
                return;
            const target = Number(value);
            if (!/^\d{1,3}$/.test(value) || target < 1 || target > maximum) {
                setError(true);
                return;
            }
            if (target === position) {
                setValue('');
                setError(false);
                return;
            }
            reorder.moveToPosition(item.id, target);
        }}/>{error && <span id={errorId} role="alert" className="cash-flow-order-position-error">1–{maximum}</span>}</span>;
}
export function CashFlowDirectoryGrid({ items, editing, disabled, choicesLoading, centers, expenses, editor, onDelete, offset = 0, positions, reorder }: Props) {
    const [pendingDeletion, setPendingDeletion] = useState<string | null>(null);
    const dragSource = useRef<string | null>(null);
    const [drop, setDrop] = useState<{
        id: string;
        placement: 'Before' | 'After';
    } | null>(null);
    useEffect(() => { dragSource.current = null; setDrop(null); }, [items, reorder?.blocked]);
    useEffect(() => { setPendingDeletion(null); }, [items, editing, disabled, editor.count]);
    const caption = `Справочник статей движения денежных средств${editing ? '' : ', только для просмотра'}`;
    return <div className="directory-table-wrap"><table key={reorder ? 'reordering' : 'catalogue'} className={`directory-table cash-flow-directory-table${editing ? ' cash-flow-directory-table--editing' : ''}${reorder ? ' cash-flow-directory-table--reordering' : ''}`} aria-label={caption}>
    <caption>{caption}</caption>
    <colgroup><col className="cash-flow-directory-position-column"/><col className="cash-flow-directory-activity-column"/><col className="cash-flow-directory-group-column"/><col className="cash-flow-directory-name-column"/><col className="cash-flow-directory-direction-column"/>
      <col className="cash-flow-directory-cfo-column"/><col className="cash-flow-directory-accrual-column"/>
      <col className="cash-flow-directory-expense-column"/>{editing && <col className="cash-flow-directory-inline-actions-column"/>}</colgroup>
    <thead><tr>{['№ п/п', 'Вид деятельности', 'Группа', 'Наименование', 'Направление', 'ЦФО', 'Акрул', 'Статья затрат', ...(editing ? ['Действие'] : [])]
            .map(label => <th key={label} scope="col" data-column-min={label === '№ п/п' ? (reorder ? 112 : 48) : label === 'Наименование' ? 160 : label === 'Направление' ? 112 : undefined} data-column-default={label === '№ п/п' ? (reorder ? 112 : 48) : label === 'Наименование' ? 380 : label === 'Направление' ? 125 : undefined}>{label}</th>)}</tr></thead>
    <tbody>{items.map((item, index) => {
            const entry = editor.entries.get(item.id.toLowerCase());
            const selected = !!entry;
            const draft = entry?.draft;
            const blocked = disabled || choicesLoading || editor.frozen || (selected && entry.phase !== 'editing');
            const enabled = draft?.enabled ?? item.formsManagerialAccrual ?? false;
            const activityType = draft?.activityType ?? item.activityType ?? '';
            const groupName = draft?.groupName ?? item.parentName ?? '';
            const centerId = draft?.costCenterId ?? item.costCenterId ?? '';
            const displayCenterId = centers.find(center => center.id.toLowerCase() === centerId.toLowerCase())?.id ?? centerId;
            const expenseId = draft ? (entry.baseline ? draft.expenseId ?? '' : item.accrualExpenseItemName ? '__current__' : '')
                : item.accrualExpenseItemName ? '__current__' : '';
            const selectedExpense = entry?.baseline?.expenseItem;
            return <tr key={item.id} className={[selected ? 'cash-flow-directory-dirty-row' : '', drop?.id === item.id ? `cash-flow-drop-${drop.placement.toLowerCase()}` : ''].filter(Boolean).join(' ') || undefined} onDragOver={event => {
                    if (!reorder || reorder.blocked || !dragSource.current || dragSource.current === item.id)
                        return;
                    event.preventDefault();
                    event.dataTransfer.dropEffect = 'move';
                    const rect = event.currentTarget.getBoundingClientRect();
                    setDrop({ id: item.id, placement: event.clientY < rect.top + rect.height / 2 ? 'Before' : 'After' });
                }} onDrop={event => {
                    event.preventDefault();
                    const source = dragSource.current;
                    dragSource.current = null;
                    const rect = event.currentTarget.getBoundingClientRect();
                    const placement = event.clientY < rect.top + rect.height / 2 ? 'Before' : 'After';
                    setDrop(null);
                    if (reorder && !reorder.blocked && source && source !== item.id)
                        reorder.move(source, item.id, placement);
                }}>
        <td className="cash-flow-directory-position-cell"><span>{positions?.get(item.id) ?? offset + index + 1}</span>{reorder && <span className="cash-flow-directory-move-controls">
          <button type="button" draggable={!reorder.blocked} disabled={reorder.blocked} aria-label={`Перетащить: ${item.name}`} title="Переместить статью в общем порядке, не меняя её группу" onDragStart={event => {
                        if (reorder.blocked) {
                            event.preventDefault();
                            return;
                        }
                        dragSource.current = item.id;
                        event.dataTransfer.effectAllowed = 'move';
                        event.dataTransfer.setData('text/plain', item.id);
                    }} onDragEnd={() => { dragSource.current = null; setDrop(null); }}><GripVertical size={16} aria-hidden="true"/></button>
          <OrderPositionInput item={item} position={positions?.get(item.id) ?? offset + index + 1} reorder={reorder}/>
        </span>}</td>
        <td className={missingClass(editing ? !activityType : !item.activityType)}>{editing ? <select aria-label={`Вид деятельности: ${item.name}`} disabled={blocked} value={activityType} onChange={event => editor.change(item, { activityType: event.target.value as typeof CASH_FLOW_ACTIVITY_TYPES[number]['value'] })}>
          <option value="" disabled>Не выбран</option>
          {CASH_FLOW_ACTIVITY_TYPES.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
        </select> : item.activityType ? CASH_FLOW_ACTIVITY_TYPE_LABELS[item.activityType] : '—'}</td>
        <td className={missingClass(editing ? !groupName.trim() : item.parentName != null ? !item.parentName.trim() : !item.parentId)}>{editing ? <input aria-label={`Группа: ${item.name}`} value={groupName} maxLength={150} disabled={blocked && !(selected && entry.phase === 'loading' && !disabled && !choicesLoading && !editor.frozen)} onChange={event => editor.change(item, { groupName: event.target.value })}/>
                    : item.parentName ?? (item.parentId ? 'Родитель не загружен' : '—')}</td>
        <td className={missingClass(editing ? !(draft?.name ?? item.name).trim() : !item.name.trim())}>{editing ?
                    <input aria-label={`Наименование статьи ДДС: ${item.name}`} value={draft?.name ?? item.name} title={draft?.name ?? item.name} maxLength={150} disabled={blocked && !(selected && entry.phase === 'loading' && !disabled && !choicesLoading && !editor.frozen)} onChange={event => editor.change(item, { name: event.target.value })}/> : item.name}</td>
        <td className={missingClass(editing ? !(draft?.flowDirection ?? item.flowDirection) : !item.flowDirection)}>{editing ?
                    <select aria-label={`Направление: ${item.name}`} disabled={blocked} value={draft?.flowDirection ?? item.flowDirection ?? ''} onChange={event => editor.change(item, { flowDirection: event.target.value as 'Payment' | 'Receipt' | '' })}>
            <option value="">Не задано</option><option value="Payment">Выплата</option><option value="Receipt">Поступление</option>
          </select> : item.flowDirection === 'Payment' ? 'Выплата' : item.flowDirection === 'Receipt' ? 'Поступление' : 'Не задано'}</td>
        <td className={missingClass(editing ? !centerId.trim() : !item.costCenterName?.trim())}>{editing ? <select aria-label={`ЦФО: ${item.name}`} disabled={blocked} value={displayCenterId} onChange={event => editor.change(item, { costCenterId: event.target.value })}>
          <option value="">Не назначено</option>
          {centerId && !centers.some(center => center.id.toLowerCase() === centerId.toLowerCase())
                        && <option value={centerId} disabled>{item.costCenterName ?? 'Недоступный ЦФО'}</option>}
          {centers.map(center => <option key={center.id} value={center.id}>{center.name}</option>)}
        </select> : item.costCenterName ?? '—'}</td>
        <td className="directory-accrual-cell">{editing ? <input type="checkbox" checked={enabled} disabled={blocked} aria-label={`Начисляется акрул: ${item.name}`} onChange={event => editor.change(item, { enabled: event.target.checked })}/>
                    : typeof item.formsManagerialAccrual === 'boolean' ? item.formsManagerialAccrual ? 'Да' : 'Нет' : '—'}</td>
        <td className={missingClass(editing ? !expenseId.trim() : !item.accrualExpenseItemName?.trim())}>{editing ? <select aria-label={`Статья затрат: ${item.name}`} disabled={blocked || !enabled} value={expenseId} onChange={event => editor.change(item, { expenseId: event.target.value || null })}>
          <option value="">Не назначена</option>
          {expenseId === '__current__' && <option value="__current__" disabled>{item.accrualExpenseItemName}</option>}
          {draft && expenseId && expenseId !== '__current__' && !expenses.some(expense => expense.id === expenseId)
                        && <option value={expenseId} disabled>{selectedExpense?.name ?? 'Недоступная статья затрат'}</option>}
          {expenses.map(expense => <option key={expense.id} value={expense.id}>{expense.name}</option>)}
        </select> : item.accrualExpenseItemName ?? '—'}</td>
        {editing && <td className="cash-flow-directory-inline-actions">
          <div><button type="button" className={`line-delete-button${pendingDeletion === item.id ? ' is-pending' : ''}`} aria-label={pendingDeletion === item.id ? `Подтвердить удаление «${item.name}»` : `Удалить «${item.name}»`} title={pendingDeletion === item.id ? 'Нажмите ещё раз для удаления статьи' : 'Удалить статью — два нажатия'} disabled={disabled || editor.count > 0 || editor.saving} onBlur={() => setPendingDeletion(null)} onKeyDown={event => {
                        if (event.key === 'Escape')
                            setPendingDeletion(null);
                    }} onClick={() => {
                        if (disabled || editor.count > 0 || editor.saving)
                            return;
                        if (pendingDeletion === item.id) {
                            setPendingDeletion(null);
                            onDelete(item);
                        }
                        else
                            setPendingDeletion(item.id);
                    }}>
            <Trash2 size={17} aria-hidden="true"/></button></div>
        </td>}
      </tr>;
        })}</tbody>
  </table></div>;
}
