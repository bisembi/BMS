import { useEffect, useState } from 'react';
import { listAdjustmentNotices448 } from '../api/adjustmentNotices448';
import type { MyApprovalTaskPage355 } from '../api/myTasks355';
import { formatBusinessTimestamp } from '../utils/businessTime';
import { roleLabels } from '../utils/activeRole';
export function AdjustmentNotices448({ onOpen, revision }: {
    onOpen: (id: string) => void;
    revision: string;
}) {
    const [skip, setSkip] = useState(0), [page, setPage] = useState<MyApprovalTaskPage355 | null>(null), [error, setError] = useState(false);
    useEffect(() => {
        const abort = new AbortController();
        setPage(null);
        setError(false);
        void listAdjustmentNotices448(skip, 25, abort.signal).then(value => {
            if (abort.signal.aborted)
                return;
            const last = Math.max(0, Math.ceil(value.total / 25) - 1) * 25;
            if (skip > last) {
                setSkip(last);
                return;
            }
            setPage(value);
        }).catch(() => {
            if (!abort.signal.aborted)
                setError(true);
        });
        return () => abort.abort();
    }, [skip, revision]);
    return <section className="form-card" aria-label="Согласование корректировок бюджета">
    <h2>Корректировки бюджета на согласовании</h2>
    {error ? <p role="alert">Не удалось загрузить корректировки на согласование. Обновите уведомления.</p>
            : !page ? <p role="status">Загрузка корректировок…</p> : page.total === 0 ? <p>Корректировок, ожидающих вашего согласования, нет.</p> : <>
        <div className="notification-table-scroll"><table><caption>Только ваш текущий этап и выбранная роль</caption>
          <thead><tr><th scope="col">Корректировка</th><th scope="col">Исходный бюджет</th><th scope="col">ЦФО</th><th scope="col">Этап согласования</th><th scope="col">Дата</th></tr></thead>
          <tbody>{page.items.map(item => <tr key={item.id}><td><a className="request-link" href={`?budgetAdjustment=${encodeURIComponent(item.id)}`} onClick={event => {
                        if (event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) {
                            event.preventDefault();
                            onOpen(item.id);
                        }
                    }}>{item.number}</a></td>
            <td>{item.title}</td><td>{item.costCenterName}</td><td>{roleLabels[item.roleCode]}</td><td>{formatBusinessTimestamp(item.receivedAtUtc)}</td></tr>)}</tbody>
        </table></div>
        {(skip > 0 || page.total > 25) && <nav className="list-toolbar" aria-label="Страницы корректировок в уведомлениях">
          <button type="button" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - 25))}>Назад</button><span>{skip + 1}–{skip + page.items.length} из {page.total}</span>
          <button type="button" disabled={skip + page.items.length >= page.total} onClick={() => setSkip(skip + 25)}>Вперёд</button>
        </nav>}
      </>}
  </section>;
}
