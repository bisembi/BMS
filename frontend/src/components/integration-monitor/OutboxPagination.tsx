import { ChevronLeft, ChevronRight } from 'lucide-react';
import { pageSize } from './integrationMonitorFormat';
type Props = {
    skip: number;
    itemCount: number;
    hasMore: boolean;
    loading: boolean;
    /// The Outbox offset stays owned by the page hook.
    onSkipChange: (skip: number) => void;
};
export function OutboxPagination({ skip, itemCount, hasMore, loading, onSkipChange }: Props) {
    if (skip === 0 && !hasMore)
        return null;
    return (<>
      <span className="list-range">{itemCount === 0 ? '0' : `${skip + 1}–${skip + itemCount}`}</span>
      <button className="icon-command" type="button" aria-label="Предыдущая страница" disabled={loading || skip === 0} onClick={() => onSkipChange(Math.max(0, skip - pageSize))}><ChevronLeft size={16}/></button>
      <button className="icon-command" type="button" aria-label="Следующая страница" disabled={loading || !hasMore} onClick={() => onSkipChange(skip + pageSize)}><ChevronRight size={16}/></button>
    </>);
}
