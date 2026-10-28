import { useWorkspaceRefreshRevision, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useEffect, useRef, useState } from 'react';
import { getAccrualNotifications, type AccrualNotificationPage } from '../api/accrualNotifications';
import { NotificationAdministrationPanel } from '../components/integration-monitor/NotificationAdministrationPanel';
import { ReturnedRequestNotices377 } from '../components/ReturnedRequestNotices377';
import { AdjustmentNotices448 } from '../components/AdjustmentNotices448';
import '../styles/accrual-notifications.css';
const pageSize = 25;
const date = (value: string) => value.split('-').reverse().join('.');
const money = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
export function AccrualNotificationsPage({ onOpenRequest, onOpenAdjustment, adjustmentRefreshKey = '', canViewRegister = false }: {
    onOpenRequest: (id: string) => void;
    onOpenAdjustment?: (id: string) => void;
    adjustmentRefreshKey?: string;
    canViewRegister?: boolean;
}) {
    const [mode, setMode] = useState<'personal' | 'register'>('personal');
    const showRegister = canViewRegister && mode === 'register';
    useEffect(() => {
        if (!canViewRegister)
            setMode('personal');
    }, [canViewRegister]);
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [skip, setSkip] = useState(0);
    const [reload, setReload] = useState(0);
    const [data, setData] = useState<AccrualNotificationPage | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(false);
    const tableRegion = useRef<HTMLDivElement | null>(null);
    const emptyState = useRef<HTMLParagraphElement | null>(null);
    const errorState = useRef<HTMLParagraphElement | null>(null);
    // Bind the focus request to the requested page. A passive effect left over
    // from the preceding page must not consume a later paging request.
    const focusAfterPageChange = useRef<number | null>(null);
    useWorkspaceRefreshBusy(loading);
    useEffect(() => {
        const controller = new AbortController();
        let current = true;
        setLoading(true);
        setError(false);
        setData(null);
        void getAccrualNotifications(skip, pageSize, controller.signal).then((page) => {
            if (current)
                setData(page);
        }).catch(() => {
            if (current)
                setError(true);
        }).finally(() => {
            if (current)
                setLoading(false);
        });
        return () => { current = false; controller.abort(); };
    }, [skip, reload, workspaceRefresh]);
    const visible = !loading && data?.skip === skip ? data : null;
    useEffect(() => {
        // A skip change renders once before the fetch effect sets `loading`; keep
        // the intent alive through that transient empty view.
        if (loading || (!visible && !error) || focusAfterPageChange.current !== skip)
            return;
        focusAfterPageChange.current = null;
        (tableRegion.current ?? emptyState.current ?? errorState.current)?.focus();
    }, [error, loading, skip, visible]);
    const changePage = (nextSkip: number) => {
        focusAfterPageChange.current = nextSkip;
        setSkip(nextSkip);
    };
    return (<section className="list-page accrual-notifications" aria-label="Мои уведомления о начислениях">
      <div className="list-heading"><div>
        <h1>{showRegister ? 'Реестр уведомлений' : 'Мои уведомления'}</h1>
        {!showRegister && <>
        <p className="muted">Красная зона: документы по полностью оплаченным заявкам ещё не закрыты.</p>
        <p className="muted">Сумма и возраст зафиксированы на дату уведомления. Текущее состояние — в карточке заявки.</p>
        </>}
      </div></div>
      {canViewRegister && <div className="list-toolbar" aria-label="Вид уведомлений">
        <button type="button" className="secondary-button compact" aria-pressed={!showRegister} onClick={() => setMode('personal')}>Мои уведомления</button>
        <button type="button" className="secondary-button compact" aria-pressed={showRegister} onClick={() => setMode('register')}>Реестр уведомлений</button>
      </div>}
      {showRegister ? <NotificationAdministrationPanel reloadToken={0} onOpenRequest={onOpenRequest}/> : <>
      {onOpenAdjustment && <AdjustmentNotices448 onOpen={onOpenAdjustment} revision={JSON.stringify([reload, workspaceRefresh, adjustmentRefreshKey])}/>}
      <ReturnedRequestNotices377 onOpenRequest={onOpenRequest} revision={JSON.stringify([reload, workspaceRefresh])}/>
      <div className="list-toolbar">
        <button type="button" className="secondary-button compact" disabled={loading} onClick={() => { setSkip(0); setReload((value) => value + 1); }}>Обновить уведомления</button>
        <span role="status" aria-live="polite">{loading ? 'Загрузка уведомлений…'
                : visible ? `Уведомлений: ${visible.total.toLocaleString('ru-RU')}` : ''}</span>
      </div>
      {error && <p ref={errorState} className="notice error" role="alert" tabIndex={-1}>Не удалось загрузить уведомления о начислениях.</p>}
      {visible?.total === 0 && <p ref={emptyState} tabIndex={-1}>Уведомлений о красной зоне пока нет.</p>}
      {visible && visible.items.length > 0 && <div ref={tableRegion} className="notification-table-scroll" tabIndex={0} role="region" aria-label="Таблица уведомлений, прокрутка">
        <table><caption>Уведомления красной зоны, только для текущего получателя</caption>
          <thead><tr>{['Организация', 'Полностью оплачена', 'Дата уведомления', 'Возраст, дней',
                    'Красный порог, дней', 'Незакрытый остаток, KZT', 'Заявка'].map((label) => <th key={label} scope="col">{label}</th>)}</tr></thead>
          <tbody>{visible.items.map((item, index) => <tr key={item.id}>
            <td>{item.organizationName}</td><td>{date(item.paymentBusinessDate)}</td>
            <td>{date(item.asOfDate)}</td><td>{item.ageDays.toLocaleString('ru-RU')}</td>
            <td>{item.redFromDays.toLocaleString('ru-RU')}</td><td>{money.format(item.remainingAmountKzt)}</td>
            <td><button type="button" className="secondary-button compact" aria-label={`Открыть заявку: строка ${index + 1}, ${item.organizationName}, уведомление от ${date(item.asOfDate)}`} onClick={() => onOpenRequest(item.expenseRequestId)}>Открыть заявку</button></td>
          </tr>)}</tbody></table>
      </div>}
      {visible && visible.items.length === 0 && visible.total > 0 &&
                <p ref={emptyState} tabIndex={-1}>На этой странице уведомлений больше нет. Вернитесь назад или обновите список.</p>}
      {(skip > 0 || visible && visible.total > pageSize) && <nav className="list-toolbar" aria-label="Страницы уведомлений">
        <button type="button" disabled={loading || skip === 0} onClick={() => changePage(Math.max(0, skip - pageSize))}>Назад</button>
        <span>Страница {Math.floor(skip / pageSize) + 1}</span>
        <button type="button" disabled={loading || !visible || skip + visible.items.length >= visible.total} onClick={() => changePage(skip + pageSize)}>Далее</button>
      </nav>}
      </>}
    </section>);
}
