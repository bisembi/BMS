import { RefreshCw } from 'lucide-react';
import { useState } from 'react';
import { AccrualNotificationMonitorPanel } from '../components/integration-monitor/AccrualNotificationMonitorPanel';
import { DatabaseReadinessPanel } from '../components/integration-monitor/DatabaseReadinessPanel';
import { OutboxBacklogSummaryPanel } from '../components/integration-monitor/OutboxBacklogSummaryPanel';
import { OutboxEventGrid } from '../components/integration-monitor/OutboxEventGrid';
import { OutboxPagination } from '../components/integration-monitor/OutboxPagination';
import { ReferenceProjectionInventoryPanel } from '../components/integration-monitor/ReferenceProjectionInventoryPanel';
import { useIntegrationMonitorBacklog } from '../hooks/useIntegrationMonitorBacklog';
import { useIntegrationMonitorOutbox } from '../hooks/useIntegrationMonitorOutbox';
import { useIntegrationMonitorProjections } from '../hooks/useIntegrationMonitorProjections';
import { useIntegrationMonitorReadiness } from '../hooks/useIntegrationMonitorReadiness';
// Legacy props cannot grant financial visibility: this monitor is metadata only.
export function IntegrationOutboxMonitorPage(_props: {
    canViewNotificationAdministration?: boolean;
    onOpenRequest?: (requestId: string) => void;
} = {}) {
    // Four independent readers. Each keeps its own request sequence, so a slow
    // response in one panel can never overwrite another panel's state.
    const outbox = useIntegrationMonitorOutbox();
    const backlog = useIntegrationMonitorBacklog();
    const readiness = useIntegrationMonitorReadiness();
    const projections = useIntegrationMonitorProjections();
    const [notificationReload, setNotificationReload] = useState(0);
    return (<div className="list-page" aria-label="Сервисный монитор Outbox">
      <div className="list-heading">
        <div>
          <h1>Сервисный монитор Outbox</h1>
          <p className="muted">Только технические метаданные. Содержимое сообщения и текст ошибки не отображаются.</p>
        </div>
      </div>

      <OutboxBacklogSummaryPanel summary={backlog.summary} loading={backlog.loading} error={backlog.error}/>

      <DatabaseReadinessPanel readiness={readiness.readiness} loading={readiness.loading} error={readiness.error}/>

      <ReferenceProjectionInventoryPanel inventory={projections.inventory} loading={projections.loading} error={projections.error}/>

      <AccrualNotificationMonitorPanel reloadToken={notificationReload}/>

      <div className="list-toolbar">
        <button className="secondary-button compact" type="button" onClick={() => {
            void outbox.reload();
            void backlog.reload();
            void readiness.reload();
            void projections.reload();
            setNotificationReload((value) => value + 1);
        }} disabled={outbox.loading || backlog.loading || readiness.loading || projections.loading}>
          <RefreshCw size={15}/>Обновить
        </button>
        <span className="list-toolbar-spacer"/>
        <OutboxPagination skip={outbox.skip} itemCount={outbox.items.length} hasMore={outbox.hasMore} loading={outbox.loading} onSkipChange={outbox.setSkip}/>
      </div>

      {outbox.error && <div className="notice error" role="alert">{outbox.error}</div>}
      <OutboxEventGrid items={outbox.items} loading={outbox.loading} error={outbox.error}/>
    </div>);
}
