import { useEffect, useState } from 'react';
import { getAccrualNotificationMonitor, type AccrualNotificationMonitor } from '../../api/accrualNotifications';
import { formatUtc } from './integrationMonitorFormat';
const labels = {
    disabled: 'Контроль выключен', neverRun: 'Ещё не запускался', running: 'Проверка выполняется',
    succeeded: 'Последняя проверка завершена', failed: 'Проверка не завершена, требуется повтор',
};
export function AccrualNotificationMonitorPanel({ reloadToken }: {
    reloadToken: number;
}) {
    const [data, setData] = useState<AccrualNotificationMonitor | null>(null);
    const [error, setError] = useState(false);
    useEffect(() => {
        const controller = new AbortController();
        let current = true;
        setData(null);
        setError(false);
        void getAccrualNotificationMonitor(controller.signal).then((result) => {
            if (current)
                setData(result);
        }).catch(() => {
            if (current)
                setError(true);
        });
        return () => { current = false; controller.abort(); };
    }, [reloadToken]);
    return (<section className="form-card" aria-label="Контроль уведомлений о начислениях">
      <h2 className="section-title">Контроль уведомлений о начислениях</h2>
      <p className="section-hint">Доставка внутри BMS. Внешняя рассылка не подключена. Счётчики запуска относятся к этому серверному процессу.</p>
      {error && <p className="notice error" role="alert">Не удалось проверить контроль уведомлений.</p>}
      {!data && !error && <p role="status">Загрузка состояния уведомлений…</p>}
      {data && <>
        <p role="status">{labels[data.lastRunStatus]}</p>
        <p>Последняя попытка: {formatUtc(data.lastAttemptAtUtc)}. Успешная проверка: {formatUtc(data.lastSuccessfulAtUtc)}.</p>
        <p>Проверено заявок: {data.checkedRequests.toLocaleString('ru-RU')}.
          {' '}Создано уведомлений: {data.createdNotifications.toLocaleString('ru-RU')}.
          {' '}Отказов проверки: {data.failedRequests.toLocaleString('ru-RU')}.</p>
        <p>Не сопоставлен инициатор: {data.missingInitiators.toLocaleString('ru-RU')}.
          {' '}Не найден ответственный бухгалтер: {data.missingAccountants.toLocaleString('ru-RU')}.</p>
        {(data.missingInitiators > 0 || data.missingAccountants > 0) &&
                <p className="notice" role="status">Проверьте активность сотрудников, сопоставления учётных записей, роли и доступ к организации. Ошибка настройки также сохраняется в бизнес-аудите.</p>}
      </>}
    </section>);
}
