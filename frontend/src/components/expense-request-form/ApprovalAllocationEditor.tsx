import { Copy } from 'lucide-react';
import type { ApprovalAllocationLine } from '../../api/approvalDetails';
import type { ReferenceDataItem } from '../../types';
import { canonicalMoneyText, exactMoneyNumber, formatEditableMoney } from '../../utils/moneyInput';
import { MoneyInput } from './MoneyInput';
export type AllocationRow = Omit<ApprovalAllocationLine, 'amount'> & {
    key: string;
    amount: string;
};
export const allocationRows = (lines: readonly ApprovalAllocationLine[]): AllocationRow[] => lines.map(l => ({ ...l, key: l.lineId!, amount: String(l.amount) }));
export function copyAllocationRow(rows: AllocationRow[], index: number): AllocationRow[] {
    const row = rows[index];
    if (!row || rows.length >= 10000)
        return rows;
    return [...rows.slice(0, index + 1), { ...row, lineId: null, key: crypto.randomUUID() }, ...rows.slice(index + 1)];
}
export function allocationCents(text: string): bigint | null {
    const canonical = canonicalMoneyText(text);
    if (canonical === null || !/^\d{1,16}(?:\.\d{1,2})?$/u.test(canonical))
        return null;
    const [whole, fraction = ''] = canonical.split('.');
    return BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
}
export function formatAllocationCents(value: bigint): string {
    const sign = value < 0n ? '-' : '';
    const v = value < 0n ? -value : value;
    return formatEditableMoney(`${sign}${v / 100n}.${String(v % 100n).padStart(2, '0')}`);
}
export function allocationRemainder(rows: readonly AllocationRow[], source: readonly ApprovalAllocationLine[], sourceId?: string): bigint {
    const total = source.filter(l => !sourceId || l.sourceGroupId === sourceId).reduce((n, l) => n + allocationCents(String(l.amount))!, 0n);
    return total - rows.filter(l => !sourceId || l.sourceGroupId === sourceId).reduce((n, l) => n + (allocationCents(l.amount) ?? 0n), 0n);
}
export function prepareAllocationSave(rows: readonly AllocationRow[], source: readonly ApprovalAllocationLine[]): ApprovalAllocationLine[] {
    if (rows.some(l => exactMoneyNumber(l.amount, false) === null))
        throw new Error('Заполните положительную сумму в каждой строке. Серый остаток — только подсказка.');
    if (allocationRemainder(rows, source) !== 0n)
        throw new Error('Сумма строк не равна сумме заявки. Сохранение не выполнено.');
    if (source.some(l => allocationRemainder(rows, source, l.sourceGroupId) !== 0n))
        throw new Error('Сумма частей по каждой статье ДДС должна сохраняться. Статья ДДС и бюджет не меняются.');
    return rows.map(({ key: _, amount, ...l }) => ({ ...l, amount: exactMoneyNumber(amount, false)! }));
}
type Props = {
    rows: AllocationRow[];
    source: readonly ApprovalAllocationLine[];
    departments: readonly ReferenceDataItem[];
    currencyCode: string;
    departmentsAvailable?: boolean;
    onChange: (rows: AllocationRow[]) => void;
};
export function ApprovalAllocationEditor({ rows, source, departments, currencyCode, departmentsAvailable = true, onChange }: Props) {
    const patch = (index: number, change: Partial<AllocationRow>) => onChange(rows.map((r, i) => i === index ? { ...r, ...change } : r));
    const remainder = allocationRemainder(rows, source);
    const total = source.reduce((n, l) => n + allocationCents(String(l.amount))!, 0n);
    const hintIndex = rows.findIndex(l => l.lineId === null && l.amount.trim() === '');
    return <div className="approval-allocation-editor">
  <p className="section-hint">Распределите сумму по подразделениям затрат. Итог заявки и суммы по статьям ДДС неизменны. Серый остаток — подсказка, введите сумму явно.</p>
  <div className="expense-table-wrap"><table className="expense-table approval-accrual-table" aria-label="Распределение суммы при согласовании">
   <thead><tr><th scope="col">Строка</th><th scope="col">Подразделение затрат</th><th scope="col">Период начисления</th><th scope="col">Ставка НДС</th><th scope="col">Сумма с НДС, {currencyCode}</th><th scope="col">Действия</th></tr></thead>
   <tbody>{rows.map((l, i) => {
            const number = i + 1;
            const origin = source.findIndex(x => x.lineId === l.sourceLineId) + 1;
            const remaining = allocationRemainder(rows, source, l.sourceGroupId);
            return <tr key={l.key}><td>{number}{source.length > 1 && <small> (исх. {origin})</small>}</td>
     <td><select disabled={!departmentsAvailable} aria-label={`Подразделение начисления, строка ${number}`} value={l.accrualCostCenterId} onChange={e => {
                    if (departmentsAvailable)
                        patch(i, { accrualCostCenterId: e.target.value });
                }}>
      {!departments.some(x => x.id.toLowerCase() === l.accrualCostCenterId.toLowerCase()) && <option value={l.accrualCostCenterId}>Текущее подразделение</option>}
      {departments.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></td>
     <td><input aria-label={`Период начисления при согласовании, строка ${number}`} type="month" value={l.accrualPeriod.slice(0, 7)} onChange={e => {
                    if (e.target.value)
                        patch(i, { accrualPeriod: `${e.target.value}-01` });
                }}/></td>
     <td><select aria-label={`Ставка НДС при согласовании, строка ${number}`} value={l.vatRateCode} onChange={e => patch(i, { vatRateCode: e.target.value as 'Vat16' | 'WithoutVat' })}><option value="Vat16">16%</option><option value="WithoutVat">Без НДС</option></select></td>
     <td><MoneyInput aria-label={`Сумма распределения, строка ${number}`} value={l.amount} placeholder={i === hintIndex ? formatAllocationCents(remaining) : undefined} onValueChange={amount => patch(i, { amount })}/></td>
     <td><button type="button" className="secondary-button" aria-label={`Разбить строку ${number}`} disabled={rows.length >= 10000} onClick={() => onChange([...rows, { ...l, lineId: null, key: crypto.randomUUID(), amount: '' }])}>Добавить строку</button>
      <button type="button" className="secondary-button row-action neutral allocation-copy-button" aria-label={`Копировать строку ${number}`} title="Копировать строку" disabled={rows.length >= 10000} onClick={() => onChange(copyAllocationRow(rows, i))}><Copy size={14} aria-hidden="true"/></button>
      {l.lineId === null && <button type="button" className="secondary-button" aria-label={`Убрать новую строку ${number}`} onClick={() => onChange(rows.filter((_, n) => n !== i))}>Убрать</button>}</td></tr>;
        })}</tbody>
   <tfoot><tr><td colSpan={4}>Сумма заявки: {formatAllocationCents(total)} {currencyCode}</td><td colSpan={2}>Распределено: {formatAllocationCents(total - remainder)} · Остаток: {formatAllocationCents(remainder)}</td></tr></tfoot>
  </table></div>
 </div>;
}
