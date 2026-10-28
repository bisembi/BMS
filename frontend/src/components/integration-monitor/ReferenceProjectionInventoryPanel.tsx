import { formatUtc } from './integrationMonitorFormat';
import type { ReferenceProjectionInventory } from '../../types';
type Props = {
    inventory: ReferenceProjectionInventory | null;
    loading: boolean;
    error: string | null;
};
export function ReferenceProjectionInventoryPanel({ inventory, loading, error }: Props) {
    return (<section className="form-card" aria-label="Инвентаризация проекций сотрудников и ролей">
    <div className="lines-heading">
      <div>
        <div className="section-title">Инвентаризация проекций сотрудников и ролей</div>
        <div className="section-hint">
          Только агрегированные количества. Это не подтверждение импорта или свежести,
          не сопоставление web-актора с сотрудником и не готовность workflow.
        </div>
      </div>
    </div>
    {loading && (<div className="readiness-summary not-ready">Чтение инвентаризации…</div>)}
    {error && (<div className="notice error" role="alert">{error}</div>)}
    {inventory && !loading && (<div className="projection-inventory">
        <dl className="projection-inventory-grid">
          <div>
            <dt>Сотрудники</dt>
            <dd>
              {inventory.employees.activeCount.toLocaleString('ru-RU')}
              {' активных из '}
              {inventory.employees.totalCount.toLocaleString('ru-RU')}
            </dd>
          </div>
          <div>
            <dt>Функциональные роли</dt>
            <dd>
              {inventory.functionalRoles.activeCount.toLocaleString('ru-RU')}
              {' активных из '}
              {inventory.functionalRoles.totalCount.toLocaleString('ru-RU')}
            </dd>
          </div>
          <div>
            <dt>Назначения ролей</dt>
            <dd>
              {inventory.employeeFunctionalRoles.activeCount.toLocaleString('ru-RU')}
              {' активных из '}
              {inventory.employeeFunctionalRoles.totalCount.toLocaleString('ru-RU')}
            </dd>
          </div>
        </dl>
        <div className={`projection-inventory-summary ${inventory.activeAssignmentsWithInactiveEmployeeCount === 0
                && inventory.activeAssignmentsWithInactiveFunctionalRoleCount === 0
                ? 'consistent'
                : 'attention'}`}>
          <div>
            Активных назначений с отсутствующим или неактивным сотрудником:{' '}
            {inventory.activeAssignmentsWithInactiveEmployeeCount.toLocaleString('ru-RU')}.
          </div>
          <div>
            Активных назначений с отсутствующей или неактивной ролью:{' '}
            {inventory.activeAssignmentsWithInactiveFunctionalRoleCount.toLocaleString('ru-RU')}.
          </div>
          <div>Проверено: {formatUtc(inventory.checkedAtUtc)}.</div>
        </div>
      </div>)}
  </section>);
}
