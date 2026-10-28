import { useEffect, useState } from 'react';
import { listReturnedRequestNotices377, type ReturnedRequestNoticePage377 } from '../api/returnedRequestNotices377';
export function ReturnedRequestNotices377({ onOpenRequest, revision }: {
    onOpenRequest: (id: string) => void;
    revision: string;
}) {
    const [skip, setSkip] = useState(0);
    const [page, setPage] = useState<ReturnedRequestNoticePage377 | null>(null);
    const [error, setError] = useState(false);
    useEffect(() => {
        const abort = new AbortController();
        setPage(null);
        setError(false);
        void listReturnedRequestNotices377(skip, 25, abort.signal).then(p => {
            if (!abort.signal.aborted)
                setPage(p);
        })
            .catch(() => {
            if (!abort.signal.aborted)
                setError(true);
        });
        return () => abort.abort();
    }, [skip, revision]);
    return <section aria-label="Возвращённые заявки" className="form-card">
    <h2>Возвращены на доработку</h2>
    <p className="muted">Только ваши заявки. Уведомление исчезнет после повторной подачи или отмены заявки.</p>
    {error ? <p role="alert">Не удалось загрузить возвраты на доработку. Обновите уведомления.</p> : !page ? <p role="status">Загрузка возвратов…</p> :
            page.total === 0 ? <p>Заявок на доработке нет.</p> : <>
      <div className="notification-table-scroll"><table><caption>Возвраты автору заявки</caption><thead><tr>
        <th scope="col">Заявка</th><th scope="col">Организация</th><th scope="col">ЦФО</th><th scope="col">Дата возврата</th><th scope="col">Причина возврата</th>
      </tr></thead><tbody>{page.items.map(row => <tr key={row.id}>
        <td><button type="button" className="request-link" onClick={() => onOpenRequest(row.expenseRequestId)}>{row.number}</button></td>
        <td>{row.organizationName}</td><td>{row.costCenterName}</td>
        <td>{new Intl.DateTimeFormat('ru-RU', { timeZone: 'Asia/Qyzylorda', dateStyle: 'short', timeStyle: 'short' }).format(new Date(row.returnedAtUtc))}</td>
        <td style={{ whiteSpace: 'pre-wrap', color: '#111' }}>{row.reason ?? 'Причина доступна в истории заявки.'}</td>
      </tr>)}</tbody></table></div>
      {(skip > 0 || page.total > 25) && <nav aria-label="Страницы возвратов" className="list-toolbar">
        <button type="button" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - 25))}>Назад</button>
        <span>{skip + 1}–{skip + page.items.length} из {page.total}</span>
        <button type="button" disabled={skip + page.items.length >= page.total} onClick={() => setSkip(skip + 25)}>Вперёд</button>
      </nav>}
    </>}
  </section>;
}
