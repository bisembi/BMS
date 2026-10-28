import { formatUtc } from './integrationMonitorFormat';
import type { OutboxBacklogSummary } from '../../types';
type Props = {
    summary: OutboxBacklogSummary | null;
    loading: boolean;
    error: string | null;
};
export function OutboxBacklogSummaryPanel({ summary, loading, error }: Props) {
    return (<section className="form-card" aria-label="Сводка очереди Outbox">
    <div className="lines-heading">
      <div>
        <div className="section-title">Сводка очереди Outbox</div>
        <div className="section-hint">
          Только агрегированные метаданные backlog. Это не подтверждение работающего
          consumer, доставки, retry-политики, retention или SLA.
        </div>
      </div>
    </div>
    {loading && (<div className="readiness-summary not-ready">Чтение сводки Outbox…</div>)}
    {error && (<div className="notice error" role="alert">{error}</div>)}
    {summary && !loading && (<div className="projection-inventory">
        <dl className="projection-inventory-grid">
          <div>
            <dt>Всего сообщений</dt>
            <dd>{summary.totalCount.toLocaleString('ru-RU')}</dd>
          </div>
          <div>
            <dt>Ожидают обработки</dt>
            <dd>{summary.pendingCount.toLocaleString('ru-RU')}</dd>
          </div>
          <div>
            <dt>Обработано</dt>
            <dd>{summary.processedCount.toLocaleString('ru-RU')}</dd>
          </div>
        </dl>
        <div className="projection-inventory-summary neutral">
          <div>
            Pending после одной или нескольких попыток:{' '}
            {summary.retriedPendingCount.toLocaleString('ru-RU')}.
          </div>
          <div>
            Pending с признаком скрытой ошибки:{' '}
            {summary.pendingWithLastErrorCount.toLocaleString('ru-RU')}.
          </div>
          <div>
            Максимум попыток среди pending:{' '}
            {summary.maxPendingRetryCount.toLocaleString('ru-RU')}.
          </div>
          <div>
            Самое раннее pending: {formatUtc(summary.oldestPendingOccurredAtUtc)}.
            {' '}Проверено: {formatUtc(summary.checkedAtUtc)}.
          </div>
        </div>
      </div>)}
  </section>);
}
