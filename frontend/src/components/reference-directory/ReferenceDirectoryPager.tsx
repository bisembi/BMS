import { ChevronLeft, ChevronRight } from 'lucide-react';
import type { CashFlowItemPageSize } from '../../hooks/cashFlowItemCatalogueWindow';
type Props = {
    skip: number;
    take: number;
    total: number;
    loading: boolean;
    hasPreviousPage: boolean;
    hasNextPage: boolean;
    onPreviousPage: () => void;
    onNextPage: () => void;
    pageSize?: CashFlowItemPageSize;
    onPageSizeChange?: (value: CashFlowItemPageSize) => void;
};
/// Which rows of the whole answer are on screen, and the two steps that move
/// the window.
///
/// The range is stated rather than implied: a reader looking at fifty rows of
/// seventy-three thousand needs to know that is what they are looking at.
export function ReferenceDirectoryPager({ skip, take, total, loading, hasPreviousPage, hasNextPage, onPreviousPage, onNextPage, pageSize, onPageSizeChange, }: Props) {
    const first = total === 0 ? 0 : skip + 1;
    const last = Math.min(skip + take, total);
    const resizable = pageSize !== undefined && onPageSizeChange !== undefined;
    const pageCount = total === 0 ? 0 : Math.ceil(total / take);
    const currentPage = total === 0 ? 0 : Math.floor(skip / take) + 1;
    return (<div className={`directory-pager${resizable ? ' directory-pager-resizable' : ''}`} role={resizable ? 'navigation' : undefined} aria-label={resizable ? 'Пагинация справочника' : undefined}>
      <span className="directory-pager-range" role="status">
        {total === 0
            ? 'Строки не найдены'
            : `Строки ${first}–${last} из ${total}`}
      </span>
      {resizable && <label className="directory-pager-size">Строк на странице
        <select value={pageSize} disabled={loading} onChange={event => {
                const value = event.target.value;
                if (value === 'all')
                    onPageSizeChange('all');
                else if (value === '50' || value === '100' || value === '500')
                    onPageSizeChange(Number(value) as 50 | 100 | 500);
            }}>
          <option value="50">50</option><option value="100">100</option>
          <option value="500">500</option><option value="all">Все</option>
        </select>
      </label>}
      {resizable && pageCount > 1 && <span className="directory-pager-pages">Страница {currentPage} из {pageCount}</span>}
      {(hasPreviousPage || hasNextPage) && <>
      <button className="secondary-button" type="button" disabled={!hasPreviousPage || loading} onClick={onPreviousPage}>
        <ChevronLeft size={15} aria-hidden="true"/>
        <span>Предыдущая страница</span>
      </button>
      <button className="secondary-button" type="button" disabled={!hasNextPage || loading} onClick={onNextPage}>
        <span>Следующая страница</span>
        <ChevronRight size={15} aria-hidden="true"/>
      </button>
      </>}
    </div>);
}
