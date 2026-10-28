import { useEffect, useRef, useState } from 'react';
import { getCurrencyRates, importCurrencyRates, writeManualCurrencyRate, type CurrencyRatePage, type CurrencyRateRow } from '../api/currencyRates';
import { useCommandLock } from '../components/WorkspaceCommandLockContext';
import { useWorkspaceRefreshGuard, useWorkspaceRefreshRevision } from '../components/WorkspaceRefreshContext';
import { ReferenceDirectoryPager } from '../components/reference-directory/ReferenceDirectoryPager';
import { currencyRatesErrorMessage } from '../utils/uiErrorMessages';
export function CurrencyRatesPage({ canEdit, onNavigationLockChange }: {
    canEdit: boolean;
    onNavigationLockChange?: (locked: boolean) => void;
}) {
    const [date, setDate] = useState('');
    const [skip, setSkip] = useState(0);
    const [revision, setRevision] = useState(0);
    const [dateDraft, setDateDraft] = useState<string | null>(null);
    const [page, setPage] = useState<CurrencyRatePage | null>(null);
    const [loading, setLoading] = useState(true);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const [editing, setEditing] = useState<CurrencyRateRow | null>(null);
    const [rate, setRate] = useState('');
    const [quantity, setQuantity] = useState('1');
    const [reason, setReason] = useState('');
    const commands = useCommandLock('currency-rates');
    const refresh = useWorkspaceRefreshRevision();
    const generation = useRef(0);
    const lockNotice = useRef(onNavigationLockChange);
    lockNotice.current = onNavigationLockChange;
    useWorkspaceRefreshGuard(() => busy || editing ? 'Сначала сохраните или отмените ручной ввод курса.' : null);
    useEffect(() => { lockNotice.current?.(busy || editing !== null); }, [busy, editing]);
    useEffect(() => () => lockNotice.current?.(false), []);
    useEffect(() => {
        const controller = new AbortController();
        const current = ++generation.current;
        setLoading(true);
        setError(null);
        void getCurrencyRates(date, skip, controller.signal).then(result => {
            if (controller.signal.aborted || current !== generation.current)
                return;
            setPage(result);
            setLoading(false);
        }).catch(failure => {
            if (controller.signal.aborted || current !== generation.current)
                return;
            setError(currencyRatesErrorMessage(failure, 'Не удалось загрузить курсы.'));
            setLoading(false);
        });
        return () => controller.abort();
    }, [date, skip, revision, refresh]);
    async function write(manual: boolean) {
        if (!canEdit || busy || loading || (manual && !editing))
            return;
        const ticket = commands.acquire();
        if (!ticket)
            return;
        setBusy(true);
        setError(null);
        setNotice(null);
        try {
            if (manual) {
                await writeManualCurrencyRate({ currencyId: editing!.currencyId, date: page!.businessDate,
                    rate: Number(rate.replace(',', '.')), multiplicity: Number(quantity), expectedStamp: editing!.stamp, reason: reason.trim() });
                if (!commands.isCurrent(ticket))
                    return;
                setEditing(null);
                setNotice('Ручной курс сохранён.');
            }
            else {
                const succeeded = await importCurrencyRates();
                if (!commands.isCurrent(ticket))
                    return;
                setNotice(succeeded ? 'Курсы Нацбанка загружены.' : 'Загрузка не выполнена. Введите курс вручную.');
            }
            setRevision(value => value + 1);
        }
        catch (failure) {
            if (commands.isCurrent(ticket))
                setError(currencyRatesErrorMessage(failure, 'Не удалось подтвердить сохранение.'));
        }
        finally {
            if (commands.isCurrent(ticket))
                setBusy(false);
            commands.release(ticket);
        }
    }
    const locked = busy || editing !== null;
    const rateNotice = page?.notice?.startsWith('Загрузка курсов Нацбанка не выполнена.')
        ? 'Загрузка курсов Нацбанка не выполнена. Можно повторить загрузку или ввести курс вручную. Если курс уже загружен, заявка использует последний доступный.'
        : page?.notice === 'Для части валют нет актуального курса. Введите курс вручную или загрузите курсы Нацбанка.'
            ? 'На текущую дату курсы есть не для всех валют. Для заявки используется последний загруженный курс. Если его нет, загрузите курсы Нацбанка или введите курс вручную.'
            : page?.notice;
    return <div className="currency-rates-page"><h1>Курсы валют</h1>
    <form className="directory-filters currency-rate-filters" onSubmit={event => {
            event.preventDefault();
            if (locked)
                return;
            setDate(dateDraft ?? date);
            setSkip(0);
            setRevision(value => value + 1);
        }}>
      <label>Дата курса<input type="date" value={dateDraft ?? (date || page?.date || '')} max={page?.businessDate} disabled={locked} onChange={event => setDateDraft(event.target.value)}/></label>
      <button type="submit" disabled={locked}>Применить</button>
      <button type="button" disabled={locked || loading} onClick={() => { setDateDraft(null); setDate(''); setSkip(0); setRevision(value => value + 1); }}>Сбросить</button>
      <button type="button" title="Обновить курсы по применённому отбору" disabled={locked || loading} onClick={() => setRevision(value => value + 1)}>Обновить</button>
      {canEdit && <button type="button" disabled={locked || loading} onClick={() => void write(false)}>Загрузить из Нацбанка</button>}
    </form>
    <p>Автозагрузка — ежедневно начиная с 08:00 (UTC+5). Для валютной заявки используется последний загруженный курс с датой не позже текущей. Дата используемого курса показана в заявке.</p>
    {rateNotice && <p role="status">{rateNotice}</p>}{notice && <p role="status">{notice}</p>}
    {error && <p role="alert">{error}</p>}{loading && <p role="status">Загрузка…</p>}
    {editing && <form className="directory-create" onSubmit={event => { event.preventDefault(); void write(true); }}>
      <h2>Ручной курс: {editing.code}, {page?.businessDate}</h2><fieldset disabled={busy}>
        <label>Курс в тенге<input value={rate} onChange={event => setRate(event.target.value)} required inputMode="decimal"/></label>
        <label>За единиц валюты<input value={quantity} onChange={event => setQuantity(event.target.value)} type="number" min="1" step="1" required/></label>
        <label>Причина ручного ввода<input value={reason} onChange={event => setReason(event.target.value)} maxLength={500} required/></label>
        <button type="submit">Сохранить курс</button><button type="button" onClick={() => setEditing(null)}>Отмена</button>
      </fieldset></form>}
    {page && <><div className="directory-table-wrap"><table className="directory-table" aria-label="Курсы валют">
      <thead><tr><th scope="col">Валюта</th><th scope="col">Наименование</th><th scope="col">Дата курса</th><th scope="col">Курс, ₸</th><th scope="col">Кратность</th><th scope="col">Источник</th>{canEdit && <th scope="col">Действие</th>}</tr></thead>
      <tbody>{page.items.map(item => <tr key={item.currencyId}><td>{item.code}</td><td>{item.name}</td><td>{item.rate === null || item.code === 'KZT' ? '—' : item.rateDate.split('-').reverse().join('.')}</td>
        <td>{item.rate === null ? 'Не задан' : item.rate.toLocaleString('ru-RU', { maximumFractionDigits: 6 })}</td>
        <td>{item.multiplicity?.toLocaleString('ru-RU') ?? '—'}</td><td>{item.sourceName === 'Manual' ? 'Ручной ввод' : item.sourceName ?? '—'}</td>
        {canEdit && <td><button disabled={locked || loading || item.code === 'KZT' || page.date !== page.businessDate} onClick={() => {
                        setEditing(item);
                        setRate(item.rate?.toString() ?? '');
                        setQuantity(item.multiplicity?.toString() ?? '1');
                        setReason('');
                    }}>Ввести вручную</button></td>}</tr>)}</tbody></table></div>
      <ReferenceDirectoryPager skip={page.skip} take={page.take} total={page.total} loading={loading || locked} hasPreviousPage={page.skip > 0} hasNextPage={page.skip + page.take < page.total} onPreviousPage={() => setSkip(value => Math.max(0, value - 25))} onNextPage={() => setSkip(value => value + 25)}/></>}
  </div>;
}
