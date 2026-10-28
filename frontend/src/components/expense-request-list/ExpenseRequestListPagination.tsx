import { ChevronLeft, ChevronRight } from 'lucide-react';
export const expenseRequestPageSizes = [50, 100, 200] as const;
export type ExpenseRequestPageSize = typeof expenseRequestPageSizes[number];
type Props = {
    skip: number;
    take: number;
    total: number;
    itemCount: number;
    loading: boolean;
    totalPending?: boolean;
    /// Paging stays owned by the page: this only reports the requested offset.
    onSkipChange: (skip: number) => void;
    onTakeChange?: (take: ExpenseRequestPageSize) => void;
    sizeDisabled?: boolean;
};
export function ExpenseRequestListPagination({ skip, take, total, itemCount, loading, totalPending = false, onSkipChange, onTakeChange, sizeDisabled = false, }: Props) {
    const size = onTakeChange && <label className="request-page-size460">Показывать заявок на странице
    <select aria-label="Показывать заявок на странице" value={take} disabled={sizeDisabled} onChange={event => {
            const next = Number(event.target.value);
            if (expenseRequestPageSizes.includes(next as ExpenseRequestPageSize))
                onTakeChange(next as ExpenseRequestPageSize);
        }}>{expenseRequestPageSizes.map(value => <option key={value} value={value}>{value}</option>)}</select>
  </label>;
    if (totalPending)
        return <>{size}<span className="list-range" role="status">Уточнение количества…</span></>;
    // A single page needs neither a repeated count nor inactive navigation.
    // Preserve a way back if a refreshed/filtered later page is now empty.
    if (skip === 0 && total <= take)
        return size || null;
    return (<>
      {size}
      {itemCount > 0 && skip < total && <span className="list-range">{skip + 1}–{Math.min(skip + itemCount, total)} из {total}</span>}
      <button className="icon-command" type="button" aria-label="Предыдущая страница" disabled={loading || skip === 0} onClick={() => onSkipChange(Math.max(0, skip - take))}><ChevronLeft size={16}/></button>
      <button className="icon-command" type="button" aria-label="Следующая страница" disabled={loading || skip + itemCount >= total} onClick={() => onSkipChange(skip + take)}><ChevronRight size={16}/></button>
    </>);
}
