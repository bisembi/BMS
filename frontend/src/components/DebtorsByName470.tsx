import { useEffect, useMemo, useState } from 'react';
import { RefreshCw, Search } from 'lucide-react';
import { debtorCents470, getDebtors470, type DebtorsFilters470, type DebtorsReport470 } from '../api/debtors470';
import { useWorkspaceRefreshBusy, useWorkspaceRefreshRevision } from './WorkspaceRefreshContext';
const money = (cents: bigint) => ((cents + 50n) / 100n).toLocaleString('ru-RU');
export function DebtorsByName470({ filters, generation }: {
    filters: DebtorsFilters470;
    generation: number;
}) {
    const refresh = useWorkspaceRefreshRevision();
    const [revision, setRevision] = useState(0), [search, setSearch] = useState('');
    const [state, setState] = useState<{
        key: string;
        data?: DebtorsReport470;
        failed?: boolean;
    } | null>(null);
    const key = JSON.stringify([filters, generation, revision, refresh]);
    useEffect(() => {
        const c = new AbortController();
        setState({ key });
        void getDebtors470(filters, c.signal).then(data => {
            if (!c.signal.aborted)
                setState({ key, data });
        })
            .catch(() => {
            if (!c.signal.aborted)
                setState({ key, failed: true });
        });
        return () => c.abort();
    }, [key]);
    const current = state?.key === key ? state : null;
    const data = current?.data;
    const loading = !current?.data && !current?.failed;
    useWorkspaceRefreshBusy(loading);
    const rows = useMemo(() => data?.items.filter(r => r.name.toLocaleLowerCase('ru-RU').includes(search.trim().toLocaleLowerCase('ru-RU'))) ?? [], [data, search]);
    const totals = rows.reduce((s, r) => ({ count: s.count + r.requestCount, unknown: s.unknown + r.unknownAmountCount, known: s.known + debtorCents470(r.knownAmountKzt)! }), { count: 0, unknown: 0, known: 0n });
    return <section className="finance-register debtors-by-name470" aria-label="Долги по именам">
  <div className="finance-register-tools"><div><h2>По именам</h2></div>
   <label className="finance-search"><Search size={16}/><span className="sr-only">Поиск по имени</span><input value={search} onChange={e => setSearch(e.target.value)} maxLength={256} placeholder="Поиск по имени"/></label>
   <button type="button" disabled={loading} onClick={() => setRevision(v => v + 1)}><RefreshCw size={16}/>Обновить</button>
  </div>
  {loading && <p className="finance-empty" role="status">Загрузка сумм по именам…</p>}
  {current?.failed && <div className="finance-error" role="alert">Не удалось загрузить суммы по именам. <button type="button" onClick={() => setRevision(v => v + 1)}>Повторить загрузку</button></div>}
  {data && <div className="finance-table-scroll"><table className="debtors-person-table470"><thead><tr><th scope="col">ФИО</th><th scope="col">Заявок</th><th scope="col">Сумма, ₸</th></tr></thead>
   <tbody>{rows.map(r => <tr key={r.key}><td>{r.name}</td><td className="debtors-count470">{r.requestCount}</td><td className="finance-money" title={r.amountKzt === null ? `Без курса: ${r.unknownAmountCount}` : undefined}>{r.amountKzt === null ? 'Нет курса' : money(debtorCents470(r.amountKzt)!)}</td></tr>)}</tbody>
   <tfoot><tr><th scope="row">Итого</th><td className="debtors-count470">{totals.count}</td><td className="finance-money">{totals.unknown ? 'Нет курса' : money(totals.known)}</td></tr></tfoot>
  </table>{rows.length === 0 && <p className="finance-empty" role="status">{search ? 'Совпадений нет' : 'Незакрытых оплаченных заявок нет'}</p>}</div>}
 </section>;
}
