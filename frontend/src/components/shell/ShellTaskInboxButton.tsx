import { ListChecks } from 'lucide-react';
import type { ExpenseRequestTaskSummary } from '../../hooks/useExpenseRequestTaskSummary';
type Props = {
    summary: ExpenseRequestTaskSummary;
    onOpen: () => void;
};
function accessibleLabel(summary: Exclude<ExpenseRequestTaskSummary, {
    status: 'unavailable';
}>) {
    if (summary.status === 'ready') {
        return `Мои задачи: ${summary.count.toLocaleString('ru-RU')}`;
    }
    if (summary.status === 'loading')
        return 'Мои задачи: количество обновляется';
    return 'Мои задачи: количество недоступно';
}
function tooltip(summary: Exclude<ExpenseRequestTaskSummary, {
    status: 'unavailable';
}>) {
    if (summary.status === 'ready') {
        return summary.count === 0
            ? 'Открыть «Мои задачи»: новых задач нет'
            : `Открыть «Мои задачи»: ${summary.count.toLocaleString('ru-RU')}`;
    }
    if (summary.status === 'loading')
        return 'Открыть «Мои задачи»: количество обновляется';
    return 'Открыть «Мои задачи»: количество временно недоступно';
}
export function ShellTaskInboxButton({ summary, onOpen }: Props) {
    if (summary.status === 'unavailable')
        return null;
    return (<button className="icon-button task-inbox-button" type="button" aria-label={accessibleLabel(summary)} aria-busy={summary.status === 'loading' ? 'true' : undefined} title={tooltip(summary)} onClick={onOpen}>
      <ListChecks aria-hidden="true"/>
      {summary.status === 'ready' && summary.count > 0 && (<span className="task-inbox-count" aria-hidden="true">
          {summary.count > 99 ? '99+' : summary.count}
        </span>)}
    </button>);
}
