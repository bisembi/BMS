import { useEffect, useState } from 'react';
import { pendingStaff471, staffNews471, type StaffDoc471, type StaffNews471, staffStatus471 } from '../api/staffSubstitution471';
import { staffPeriod471 } from '../utils/staffSubstitution471';
export function StaffNews471({ revision }: {
    revision: string;
}) {
    const [items, setItems] = useState<StaffNews471[] | null>(null), [error, setError] = useState(false);
    useEffect(() => {
        const abort = new AbortController();
        setError(false);
        void staffNews471(abort.signal).then(v => {
            if (!abort.signal.aborted)
                setItems(v);
        }).catch(() => {
            if (!abort.signal.aborted)
                setError(true);
        });
        return () => abort.abort();
    }, [revision]);
    if (!items?.length && !error)
        return null;
    return <section className="form-card staff471-news" aria-label="Новости о замещениях"><h2>Замещения сотрудников</h2>
  {error ? <p role="alert">Не удалось обновить новости о замещениях.</p> : <ul>{items?.map(n => <li key={n.id}><span>{n.principalName} → {n.deputyName}</span> — {n.roleName}, {staffPeriod471(n.from, n.through)}{n.status === 'REVOKED' ? ' · отозвано' : n.status === 'EXPIRED' ? ' · срок завершён' : ''}.</li>)}</ul>}</section>;
}
export function StaffPending471({ revision, onOpen }: {
    revision: string;
    onOpen: (id: string) => void;
}) {
    const [items, setItems] = useState<StaffDoc471[] | null>(null), [error, setError] = useState(false);
    useEffect(() => {
        const abort = new AbortController();
        setItems(null);
        setError(false);
        void pendingStaff471(abort.signal).then(v => {
            if (!abort.signal.aborted)
                setItems(v);
        }).catch(() => {
            if (!abort.signal.aborted)
                setError(true);
        });
        return () => abort.abort();
    }, [revision]);
    return <section className="form-card" aria-label="Замещения на подтверждение"><h2>Замещения на подтверждение</h2>
  {error ? <p role="alert">Не удалось загрузить документы замещения.</p> : items === null ? <p role="status">Загрузка замещений…</p> : items.length === 0 ? <p>Замещений, ожидающих подтверждения, нет.</p> :
            <table className="request-grid" aria-label="Документы замещения"><thead><tr><th scope="col">Документ</th><th scope="col">Сотрудник</th><th scope="col">Роль</th><th scope="col">Период</th><th scope="col">Статус</th></tr></thead><tbody>{items.map(d => <tr key={d.id}>
    <td><button type="button" className="request-link" onClick={() => onOpen(d.id)}>{d.number}</button></td><td>{d.principalName}</td><td>{d.roleName}</td><td>{staffPeriod471(d.from, d.through)}</td><td>{staffStatus471[d.status]}</td></tr>)}</tbody></table>}
 </section>;
}
