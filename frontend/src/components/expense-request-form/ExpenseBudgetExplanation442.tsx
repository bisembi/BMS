import { useEffect, useRef, useState } from 'react';
import { budgetCents442, getRequestBudgetExplanation442, RequestBudgetExplanationHttpError442, type RequestBudgetContext442, type RequestBudgetExplanation442 } from '../../api/requestBudgetExplanation442';
import { useWorkspaceRefreshBusy, useWorkspaceRefreshRevision } from '../WorkspaceRefreshContext';
import './ExpenseBudgetDetail440.css';
const money = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const date = (v: string) => v.slice(0, 10).split('-').reverse().join('.');
export function ExpenseBudgetExplanation442({ context, onClose, onRefreshRequest }: {
    context: RequestBudgetContext442;
    onClose: () => void;
    onRefreshRequest?: () => Promise<void>;
}) {
    const refresh = useWorkspaceRefreshRevision(), [reload, setReload] = useState(0);
    const generation = JSON.stringify([context, refresh, reload]);
    const [paging, setPaging] = useState({ generation, fact: 0, reserve: 0 });
    const fact = paging.generation === generation ? paging.fact : 0, reserve = paging.generation === generation ? paging.reserve : 0;
    const key = `${generation}/${fact}/${reserve}`;
    const [capture, setCapture] = useState<{
        key: string;
        data?: RequestBudgetExplanation442;
        failed?: true;
        stale?: boolean;
    } | null>(null);
    const [refreshing, setRefreshing] = useState(false);
    const refreshLock = useRef(false);
    const pinned = useRef<{
        generation: string;
        revision: number;
    } | null>(null);
    const panel = useRef<HTMLElement>(null);
    useEffect(() => { panel.current?.focus(); }, []);
    useEffect(() => {
        const controller = new AbortController();
        void getRequestBudgetExplanation442(context, fact, reserve, controller.signal, pinned.current?.generation === generation ? pinned.current.revision : undefined)
            .then(data => {
            if (!controller.signal.aborted) {
                pinned.current = { generation, revision: data.revision };
                setCapture({ key, data });
            }
        })
            .catch(error => {
            if (!controller.signal.aborted)
                setCapture({ key, failed: true, stale: error instanceof RequestBudgetExplanationHttpError442 && error.status === 409 });
        });
        return () => controller.abort();
    }, [key]);
    const current = capture?.key === key ? capture : null, data = current?.data;
    useWorkspaceRefreshBusy(current === null || refreshing);
    const retry = async () => {
        if (refreshLock.current)
            return;
        if (!current?.stale || !onRefreshRequest) {
            setReload(v => v + 1);
            return;
        }
        refreshLock.current = true;
        setRefreshing(true);
        try {
            await onRefreshRequest();
        }
        catch { /* The parent owns the safe card-load error and locks stale mutations. */ }
        finally {
            refreshLock.current = false;
            setRefreshing(false);
        }
    };
    const move = (part: 'fact' | 'reserve', skip: number) => setPaging({ generation, fact, reserve, [part]: skip });
    const pager = (part: 'fact' | 'reserve', skip: number, total: number) => total > 50 && <nav aria-label={part === 'fact' ? 'Страницы фактических выплат' : 'Страницы резервов'}>
    <button type="button" disabled={skip === 0 || !current} onClick={() => move(part, Math.max(0, skip - 50))}>Назад</button>
    <span>{skip + 1}–{Math.min(skip + 50, total)} из {total}</span>
    <button type="button" disabled={skip + 50 >= total || !current} onClick={() => move(part, skip + 50)}>Вперёд</button></nav>;
    const matches = data && context.expectedAfter !== null ? budgetCents442(data.availableAfterKzt) === budgetCents442(context.expectedAfter) : null;
    const sourcePaid = data ? -data.facts.totalKzt : null;
    return <section id="expense-panel-explanation" role="tabpanel" aria-labelledby="expense-tab-explanation" className="form-card expense-budget-detail440 expense-budget-explanation444-panel" tabIndex={-1} ref={panel}>
    <div className="expense-budget-detail440-heading"><h2>Расшифровка бюджета</h2><div>
      <button type="button" disabled={!current || refreshing} onClick={() => void retry()}>Обновить расшифровку</button>
      <button type="button" onClick={onClose}>К расходам</button></div></div>
    <p>{context.article} · {context.costCenter} · {date(context.key.budgetMonth).slice(3)}</p>
    {!current && <p role="status">Загружаем расчёт заявки…</p>}
    {current?.failed && <p role="alert">{current.stale && onRefreshRequest ? 'Заявка или расчёт изменились. Нажмите «Обновить расшифровку»: заявка будет перечитана с проверкой доступа.' : 'Не удалось подтвердить расчёт бюджета. Обновите заявку и повторите попытку.'} Данные не заменены нулями.</p>}
    {data && <>
      {matches === false && <p role="alert">Бюджет изменился после открытия заявки. Обновите заявку: текущий расчёт ниже не выдаётся за прежний остаток.</p>}
      <div className="expense-budget-detail440-scroll"><table className="expense-budget-explanation444-table" aria-label="Расшифровка расчёта бюджета">
        <colgroup><col className="expense-budget-date444"/><col className="expense-budget-number444"/><col /><col /><col className="expense-budget-money444"/></colgroup>
        <thead><tr><th colSpan={4} scope="col">Экземпляр бюджета</th><th scope="col">Сумма бюджета, ₸</th></tr></thead>
        <tbody>
          <tr><td colSpan={4}>{data.budgetInstanceNumber ?? 'Утверждённого экземпляра нет'}</td><td className="money">{money.format(data.limitKzt)}</td></tr>
          <tr><th colSpan={5} scope="colgroup">Фактические выплаты</th></tr>
          <tr><th scope="col">Дата</th><th scope="col">Номер платёжки</th><th scope="col">Контрагент</th><th scope="col">Комментарий</th><th scope="col">Сумма платежа, ₸</th></tr>
          {data.facts.items.map((row, i) => <tr key={`fact-${fact + i}`}><td>{date(row.entryDateLocal)}</td><td title={`${row.document} · ${row.company} · ${row.documentType} · строка ${row.lineNo}`}>{row.document}</td><td title={row.counterparty}>{row.counterparty}</td><td title={row.comment}>{row.comment.trim() ? row.comment : '—'}</td><td className="money">{money.format(-row.amountKzt || 0)}</td></tr>)}
          {data.facts.total === 0 && <tr><td colSpan={5}>Выплат в снимке 1С по статье за месяц нет.</td></tr>}
          <tr className="expense-budget-total444"><th colSpan={4} scope="row">Итого фактические выплаты по статье</th><td className="money">{money.format(sourcePaid!)}</td></tr>
          {budgetCents442(sourcePaid) !== budgetCents442(data.paidIncludedKzt) && <tr className="expense-budget-total444"><th colSpan={4} scope="row">Оплаты, учитываемые в расчёте этой заявки</th><td className="money">{money.format(data.paidIncludedKzt)}</td></tr>}
          <tr><th colSpan={5} scope="colgroup">Резервы до заявки и текущая заявка</th></tr>
          <tr><th scope="col">Дата планируемая</th><th scope="col">Номер заявки</th><th scope="col">Контрагент</th><th scope="col">Комментарий</th><th scope="col">Сумма, ₸</th></tr>
          {data.reservations.map(row => <tr key={row.requestId} className={row.isCurrent ? 'expense-budget-current442' : undefined}><td>{date(row.plannedPaymentDate)}</td><td title={`${row.requestNumber ?? 'Номер не назначен'}${row.isCurrent ? ' (текущая)' : ''}`}>{row.requestNumber ?? 'Номер не назначен'}{row.isCurrent ? ' (текущая)' : ''}</td><td title={row.counterparty ?? 'Не указан'}>{row.counterparty ?? 'Не указан'}</td><td title={row.comment ?? undefined}>{row.comment?.trim() ? row.comment : '—'}</td><td className="money">{money.format(row.amountKzt)}</td></tr>)}
          {data.hiddenReserveKzt > 0 && <tr><td colSpan={4}>Резервы без права просмотра реквизитов</td><td className="money">{money.format(data.hiddenReserveKzt)}</td></tr>}
          {data.reserveTotal === 0 && data.hiddenReserveKzt === 0 && <tr><td colSpan={5}>Резервов в расчёте этой заявки нет.</td></tr>}
          <tr className="expense-budget-total444"><th colSpan={4} scope="row">Итого резервы в расчёте</th><td className="money">{money.format((budgetCents442(data.reservedBeforeKzt) + budgetCents442(data.ownReservedKzt)) / 100)}</td></tr>
        </tbody><tfoot><tr><th colSpan={4} scope="row">Остаток бюджета после заявки</th><td className="money"><output aria-label="Остаток бюджета после заявки в расшифровке">{money.format(data.availableAfterKzt)}</output></td></tr></tfoot>
      </table></div>
      {pager('fact', fact, data.facts.total)}{pager('reserve', reserve, data.reserveTotal)}
      <p className="section-hint">Расчёт по месту этой заявки в очереди. Резервы последующих заявок сюда не включены. Текущая заявка учтена один раз. Возвраты и сторно уменьшают выплаты.</p>
      {budgetCents442(sourcePaid) !== budgetCents442(data.paidIncludedKzt) && <p role="status">Список 1С показывает движения по статье за месяц. В расчёте очереди учитываются оплаты, отнесённые регистром к этой позиции заявки: {money.format(data.paidIncludedKzt)} ₸. Оплаты последующих заявок: {money.format(data.laterPaidKzt)} ₸. Периоды и атрибуция оплат могут отличаться; суммы не выравнивались.</p>}
    </>}
  </section>;
}
