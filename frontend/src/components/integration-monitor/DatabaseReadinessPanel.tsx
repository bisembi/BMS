import { formatUtc, readinessLabels } from './integrationMonitorFormat';
import type { DatabaseReadinessMonitor } from '../../types';
type Props = {
    readiness: DatabaseReadinessMonitor | null;
    loading: boolean;
    error: string | null;
};
export function DatabaseReadinessPanel({ readiness, loading, error }: Props) {
    return (<section className="form-card" aria-label="Готовность базы данных">
    <div className="lines-heading">
      <div>
        <div className="section-title">Готовность базы данных</div>
        <div className="section-hint">
          Состояние схемы SQL001–SQL043, SQL045–SQL054 и необходимых хранилищ данных.
        </div>
      </div>
      {readiness && (<span className={`readiness-state ${readiness.databaseReady ? 'ready' : 'not-ready'}`}>
          {readiness.databaseReady ? 'Схема готова' : 'Требует настройки'}
        </span>)}
    </div>
    {loading && <div className="readiness-summary not-ready">Проверка схемы…</div>}
    {error && <div className="notice error" role="alert">{error}</div>}
    {readiness && !loading && (<div className={`readiness-summary ${readiness.databaseReady ? 'ready' : 'not-ready'}`}>
        <div>
          Миграции: {readiness.appliedMigrationCount}/{readiness.requiredMigrationCount}.
          {' '}Проверено: {formatUtc(readiness.checkedAtUtc)}.
        </div>
        {!readiness.databaseReady && (<details>
            <summary>Не готово компонентов: {readiness.missingComponents.length}</summary>
            <ul>
              {readiness.missingComponents.map((component) => (<li key={component}>{readinessLabels[component]}</li>))}
            </ul>
          </details>)}
      </div>)}
  </section>);
}
