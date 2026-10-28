import { formatUtc } from './integrationMonitorFormat';
import type { OutboxMonitorItem } from '../../types';
type Props = {
    items: readonly OutboxMonitorItem[];
    loading: boolean;
    error: string | null;
};
export function OutboxEventGrid({ items, loading, error }: Props) {
    return (<div className="request-grid-wrap">
    <table className="request-grid" aria-label="События Outbox">
      <thead><tr><th scope="col">Получено, UTC+5</th><th scope="col">Тип сообщения</th><th scope="col">Обработано, UTC+5</th><th scope="col">Попыток</th><th scope="col">Ошибка</th><th scope="col">ID сообщения</th></tr></thead>
      <tbody>
        {items.map((item) => (<tr key={item.messageId}>
            <td>{formatUtc(item.occurredAtUtc)}</td>
            <td>{item.messageType}</td>
            <td>{formatUtc(item.processedAtUtc)}</td>
            <td>{item.retryCount}</td>
            <td>{item.hasLastError ? 'Есть' : 'Нет'}</td>
            <td><code>{item.messageId}</code></td>
          </tr>))}
        {!loading && items.length === 0 && !error && (<tr><td colSpan={6} className="empty-grid">Сообщений пока нет.</td></tr>)}
        {loading && <tr><td colSpan={6} className="empty-grid">Загрузка…</td></tr>}
      </tbody>
    </table>
  </div>);
}
