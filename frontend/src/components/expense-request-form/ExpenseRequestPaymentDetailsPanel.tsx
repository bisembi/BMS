import { useId } from 'react';
import { useExpenseRequestPaymentDetails } from '../../hooks/useExpenseRequestPaymentDetails';
import '../../styles/payment-details.css';
const amount = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
// Use the source payment's calendar date, not browser timezone or processing time.
export function paymentDetailDate(value: string): string {
    const [year, month, day] = value.slice(0, 10).split('-');
    return `${day}.${month}.${year}`;
}
export function ExpenseRequestPaymentDetailsPanel({ requestId }: {
    requestId: string | null;
}) {
    const details = useExpenseRequestPaymentDetails(requestId);
    const contentId = useId();
    if (!requestId)
        return null;
    const page = details.data;
    return <section className="payment-details-panel" aria-label="Строки платежей">
    <h3>Строки платежей</h3>
    <p>Платежи и сторно по заявке. Просмотр — в пределах ваших прав доступа.</p>
    <button type="button" aria-expanded={details.expanded} aria-controls={contentId} onClick={details.toggle}>
      {details.expanded ? 'Скрыть строки платежей' : 'Показать строки платежей'}
    </button>
    <div id={contentId} hidden={!details.expanded}>
      {details.loading && <p role="status">Загрузка строк платежей…</p>}
      {details.error && <p role="alert">{details.error}</p>}
      {details.expanded && <button type="button" disabled={details.loading} onClick={details.reload}>Обновить строки платежей</button>}
      {page && <>
        {page.total > 0 && <p className="payment-details-note">Итоги всего денежного реестра, включая переплату. Это не сумма погашенного обязательства; зачёты переплаты здесь не отражаются.</p>}
        {(page.total > 0 || page.totals.originalAmount !== 0 || page.totals.reversedAmount !== 0 || page.totals.netAmount !== 0) && <>
        <dl className="payment-details-totals">
          <div><dt>Платежи до сторно</dt><dd>{amount.format(page.totals.originalAmount)} {page.currencyCode}</dd><dd>Банк: {amount.format(page.totals.originalAmountKzt)} KZT</dd></div>
          <div><dt>Сторнировано</dt><dd>{amount.format(page.totals.reversedAmount)} {page.currencyCode}</dd><dd>Банк: {amount.format(page.totals.reversedAmountKzt)} KZT</dd></div>
          <div><dt>Чистая сумма платежей</dt><dd>{amount.format(page.totals.netAmount)} {page.currencyCode}</dd><dd>Банк: {amount.format(page.totals.netAmountKzt)} KZT</dd></div>
        </dl></>}
        {page.total === 0 ? <p role="status">Денежных платежей пока нет.</p> : <>
          <div className="payment-details-table-scroll" role="region" aria-label="Таблица строк платежей" tabIndex={0}>
            <table>
              <caption>Денежные платежи и сторно</caption>
              <thead><tr><th scope="col">№</th><th scope="col">Дата платежа</th><th scope="col">Номер платежа</th><th scope="col">Вид</th><th scope="col">Сумма</th><th scope="col">Валюта</th><th scope="col">Банковский эквивалент, KZT</th></tr></thead>
              <tbody>{page.items.map(item => <tr key={item.position}>
                <td>{item.position}</td><td>{paymentDetailDate(item.paidAt)}</td><td>{item.paymentNumber ?? 'Не указан'}</td>
                <td>{item.isReversal ? 'Сторно' : 'Платёж'}</td>
                <td>{item.isReversal ? '−' : ''}{amount.format(item.amount)}</td><td>{page.currencyCode}</td>
                <td>{item.isReversal ? '−' : ''}{amount.format(item.amountKzt)}</td>
              </tr>)}</tbody>
            </table>
          </div>
          {page.items.length === 0 && <p role="status">На этой странице строк нет. Вернитесь на предыдущую страницу.</p>}
          <nav className="payment-details-pagination" aria-label="Страницы платежей">
            <button type="button" disabled={details.loading || page.skip === 0} onClick={details.previous}>Предыдущие платежи</button>
            <span role="status">{page.items.length > 0 ? `${page.skip + 1}–${page.skip + page.items.length} из ${page.total}` : `Всего: ${page.total}`}</span>
            <button type="button" disabled={details.loading || page.skip + page.take >= page.total} onClick={details.next}>Следующие платежи</button>
          </nav>
        </>}
      </>}
    </div>
  </section>;
}
