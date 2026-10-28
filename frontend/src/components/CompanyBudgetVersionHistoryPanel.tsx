import { useEffect, useState } from 'react';
import { formatBusinessTimestamp } from '../utils/businessTime';
import { companyBudgetVersionErrorMessage, getCompanyBudgetVersionHistory, type CompanyBudgetVersionHistory } from '../api/companyBudgetVersions';
export function CompanyBudgetVersionHistoryPanel({ id, disabled, onOpenSource }: {
    id: string;
    disabled: boolean;
    onOpenSource: (id: string) => void;
}) {
    const [skip, setSkip] = useState(0);
    const [rows, setRows] = useState<CompanyBudgetVersionHistory[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [retry, setRetry] = useState(0);
    useEffect(() => {
        const controller = new AbortController();
        setRows(null);
        setError(null);
        void getCompanyBudgetVersionHistory(id, skip, 25, controller.signal)
            .then(value => {
            if (!controller.signal.aborted)
                setRows(value);
        })
            .catch(cause => {
            if (!controller.signal.aborted)
                setError(companyBudgetVersionErrorMessage(cause));
        });
        return () => controller.abort();
    }, [id, skip, retry]);
    const labels: Record<string, string> = { CompanyBudgetVersioncreate: 'Создание', CompanyBudgetVersionupdate: 'Изменение состава',
        CompanyBudgetVersionactivate: 'Утверждение', CompanyBudgetVersionRevisionCreated: 'Создание новой ревизии',
        CompanyBudgetVersionActiveCompositionUpdated: 'Изменение действующего состава и пересчёт лимитов' };
    return <section aria-label="История версии"><h2>История версии</h2>
    {error && <p role="alert">{error} <button type="button" disabled={disabled} onClick={() => setRetry(value => value + 1)}>Повторить историю</button></p>}
    {!rows && !error && <p role="status">Загрузка истории…</p>}
    {rows && <><div className="directory-table-wrap"><table className="directory-table"><caption>История версии бюджета</caption>
      <thead><tr><th scope="col">Дата</th><th scope="col">Действие</th><th scope="col">Исходная версия</th><th scope="col">Документов до / после</th></tr></thead>
      <tbody>{rows.map(row => <tr key={row.auditId}><td>{formatBusinessTimestamp(row.occurredAtUtc)}</td>
        <td>{labels[row.action] ?? row.action}</td><td>{row.sourceVersionId ? <button type="button" className="directory-item-link" disabled={disabled} onClick={() => onOpenSource(row.sourceVersionId!)}>{row.sourceVersionName}</button> : '—'}</td>
        <td className="numeric">{row.previousInstanceCount ?? '—'} / {row.instanceCount}</td></tr>)}</tbody></table></div>
      {rows.length === 0 && <p>Событий на этой странице нет.</p>}
      {(skip > 0 || rows.length === 25) && <><button type="button" disabled={disabled || skip === 0} onClick={() => setSkip(value => Math.max(0, value - 25))}>Предыдущая страница истории</button>
      <button type="button" disabled={disabled || rows.length < 25} onClick={() => setSkip(value => value + 25)}>Следующая страница истории</button></>}</>}
  </section>;
}
