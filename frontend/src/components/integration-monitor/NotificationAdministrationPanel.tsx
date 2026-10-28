import { getAccrualNotificationIssues, getAdministrativeAccrualNotifications, type AdministrativeAccrualNotification, type NotificationIssueCode } from '../../api/notificationAdministration';
import { useCallback, useRef } from 'react';
import { useNotificationAdministrationPage } from '../../hooks/useNotificationAdministrationPage';
import { formatUtc } from './integrationMonitorFormat';
import '../../styles/notification-administration.css';
const money = new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const date = (value: string) => value.split('-').reverse().join('.');
const issueLabels: Record<NotificationIssueCode, string> = {
    NO_RESPONSIBLE_ACCOUNTANT: 'Не найден ответственный бухгалтер с доступом к организации',
    INITIATOR_MAPPING_UNAVAILABLE: 'Инициатор не сопоставлен с активным сотрудником',
};
const recipientRoles = (row: AdministrativeAccrualNotification) => [
    row.recipientWasInitiator ? 'Инициатор' : '',
    row.recipientWasResponsibleAccountant ? 'Ответственный бухгалтер' : '',
].filter(Boolean).join(', ');
type PageControls = ReturnType<typeof useNotificationAdministrationPage>;
function Controls({ page, label }: {
    page: PageControls;
    label: string;
}) {
    if (!page.canGoBack && !page.canGoNext)
        return null;
    return <nav className="notification-admin-controls" aria-label={`Страницы: ${label}`}>
    <button type="button" className="secondary-button compact" disabled={page.loading || !page.canGoBack} onClick={page.previous}>Назад</button>
    <span>Страница {page.pageNumber}</span>
    <button type="button" className="secondary-button compact" disabled={page.loading || !page.canGoNext} onClick={page.next}>Далее</button>
  </nav>;
}
export function NotificationAdministrationPanel({ reloadToken, onOpenRequest }: {
    reloadToken: number;
    onOpenRequest: (requestId: string) => void;
}) {
    const queueTable = useRef<HTMLDivElement>(null);
    const queueEmpty = useRef<HTMLParagraphElement>(null);
    const queueError = useRef<HTMLParagraphElement>(null);
    const issuesTable = useRef<HTMLDivElement>(null);
    const issuesEmpty = useRef<HTMLParagraphElement>(null);
    const issuesError = useRef<HTMLParagraphElement>(null);
    const restoreQueueFocus = useCallback(() => {
        (queueTable.current ?? queueEmpty.current ?? queueError.current)?.focus();
    }, []);
    const restoreIssuesFocus = useCallback(() => {
        (issuesTable.current ?? issuesEmpty.current ?? issuesError.current)?.focus();
    }, []);
    const queue = useNotificationAdministrationPage(getAdministrativeAccrualNotifications, reloadToken, restoreQueueFocus);
    const issues = useNotificationAdministrationPage(getAccrualNotificationIssues, reloadToken, restoreIssuesFocus);
    return <section className="notification-administration" aria-label="Служебные уведомления о начислениях">
    <h2 className="section-title">История и ошибки настройки</h2>
    <p className="section-hint">Просмотр для финансового контролёра и финансового директора.
      Постановка в очередь не означает внешнюю доставку.</p>
    <section className="form-card" aria-label="История постановки уведомлений">
      <h3>История постановки уведомлений</h3>
      <p className="section-hint">Сумма, возраст и основания получателя зафиксированы при постановке.
        Код, имя и активность сотрудника — текущие данные справочника. Суммы разных уведомлений не складываются.</p>
      <div className="notification-admin-controls">
        <button type="button" className="secondary-button compact" disabled={queue.loading} onClick={queue.reload}>
          Обновить служебные уведомления</button>
        <span role="status">{queue.loading ? 'Загрузка служебных уведомлений…'
            : queue.visible ? `Записей: ${queue.visible.total.toLocaleString('ru-RU')}` : ''}</span>
      </div>
      {queue.error && <p ref={queueError} tabIndex={-1} role="alert" className="notice error">
        Не удалось загрузить служебные уведомления.</p>}
      {queue.visible?.total === 0 && <p ref={queueEmpty} tabIndex={-1}>Созданных уведомлений пока нет.</p>}
      {queue.visible && queue.visible.items.length > 0 && <div ref={queueTable} className="notification-admin-scroll" tabIndex={0} role="region" aria-label="История уведомлений, прокрутка">
        <table><caption>История постановки уведомлений красной зоны</caption>
          <thead><tr>{['Организация', 'Получатель — справочник', 'Активность — сейчас', 'Основание — при постановке',
                'Полностью оплачена', 'Создано (UTC+5)', 'Дата снимка', 'Возраст / красный порог, дней',
                'Снимок остатка, KZT', 'Заявка'].map((label) => <th key={label} scope="col">{label}</th>)}</tr></thead>
          <tbody>{queue.visible.items.map((row) => <tr key={row.id}>
            <td>{row.organizationName}</td><td>{row.recipientName}<small>Код: {row.recipientCode}</small></td>
            <td>{row.recipientIsActive ? 'Активен' : 'Неактивен'}</td><td>{recipientRoles(row)}</td>
            <td>{date(row.paymentBusinessDate)}</td><td>{formatUtc(row.createdAtUtc)}</td><td>{date(row.asOfDate)}</td>
            <td>{row.ageDays.toLocaleString('ru-RU')} / {row.redFromDays.toLocaleString('ru-RU')}</td>
            <td className="notification-admin-money">{money.format(row.remainingAmountKzt)}</td>
            <td><button type="button" className="secondary-button compact" onClick={() => onOpenRequest(row.expenseRequestId)} aria-label={`Открыть заявку: ${row.organizationName}, получатель ${row.recipientName}`}>Открыть заявку</button></td>
          </tr>)}</tbody></table>
      </div>}
      {queue.visible && queue.visible.total > 0 && queue.visible.items.length === 0 &&
            <p ref={queueEmpty} tabIndex={-1}>На этой странице записей больше нет. Вернитесь назад или обновите список.</p>}
      <Controls page={queue} label="История уведомлений"/>
    </section>
    <section className="form-card" aria-label="Незакрытые ошибки настройки уведомлений">
      <h3>Незакрытые ошибки настройки</h3>
      <p className="section-hint">Проверьте активность сотрудников, сопоставления учётных записей, роли и доступ к организации.
        После исправления настройки повторная штатная проверка закрывает ошибку. Команд изменения на этом экране нет.</p>
      <div className="notification-admin-controls">
        <button type="button" className="secondary-button compact" disabled={issues.loading} onClick={issues.reload}>
          Обновить ошибки настройки</button>
        <span role="status">{issues.loading ? 'Загрузка ошибок настройки…'
            : issues.visible ? `Записей: ${issues.visible.total.toLocaleString('ru-RU')}` : ''}</span>
      </div>
      {issues.error && <p ref={issuesError} tabIndex={-1} role="alert" className="notice error">
        Не удалось загрузить ошибки настройки уведомлений.</p>}
      {issues.visible?.total === 0 && <p ref={issuesEmpty} tabIndex={-1}>Незакрытых ошибок настройки нет.</p>}
      {issues.visible && issues.visible.items.length > 0 && <div ref={issuesTable} className="notification-admin-scroll" tabIndex={0} role="region" aria-label="Ошибки настройки, прокрутка">
        <table><caption>Незакрытые ошибки настройки уведомлений</caption>
          <thead><tr>{['Организация', 'Ошибка настройки', 'Впервые обнаружена (UTC+5)', 'Последняя проверка (UTC+5)', 'Заявка']
                .map((label) => <th key={label} scope="col">{label}</th>)}</tr></thead>
          <tbody>{issues.visible.items.map((row) => <tr key={`${row.expenseRequestId}:${row.code}`}>
            <td>{row.organizationName}</td><td>{issueLabels[row.code]}</td>
            <td>{formatUtc(row.firstDetectedAtUtc)}</td><td>{formatUtc(row.lastDetectedAtUtc)}</td>
            <td><button type="button" className="secondary-button compact" onClick={() => onOpenRequest(row.expenseRequestId)} aria-label={`Открыть заявку: ${row.organizationName}, ${issueLabels[row.code]}`}>Открыть заявку</button></td>
          </tr>)}</tbody></table>
      </div>}
      {issues.visible && issues.visible.total > 0 && issues.visible.items.length === 0 &&
            <p ref={issuesEmpty} tabIndex={-1}>
          На этой странице ошибок больше нет. Вернитесь назад или обновите список.</p>}
      <Controls page={issues} label="Ошибки настройки"/>
    </section>
  </section>;
}
