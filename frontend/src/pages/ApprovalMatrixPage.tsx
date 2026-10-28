import { useState } from 'react';
import { approvalMatrixRoleCodes } from '../api/approvalMatrixPublicResponse';
import { approvalMatrixUnavailableMessage } from '../api/approvalMatrix';
import { approvalCommandLabel, approvalConditionLabels, approvalRoleLabels } from '../components/approval-matrix/approvalMatrixLabels';
import { expenseRequestStatusLabel } from '../expenseRequestStatus';
import { useApprovalMatrix } from '../hooks/useApprovalMatrix';
import '../styles/approval-matrix.css';
export function ApprovalMatrixPage() {
    const [version, setVersion] = useState(1);
    const { state, refresh } = useApprovalMatrix(version);
    const [statusIndex, setStatusIndex] = useState('');
    const [roleIndex, setRoleIndex] = useState('');
    const data = state.status === 'ready' ? state.data : null;
    const status = statusIndex === '' ? null : data?.statuses[Number(statusIndex)]?.code;
    const role = roleIndex === '' ? null : approvalMatrixRoleCodes[Number(roleIndex)];
    const rows = data?.transitions.filter((row) => (!status || row.statusCode === status)
        && (!role || row.roleCode === role)) ?? [];
    const threshold = data?.generalDirectorThresholdKzt.toLocaleString('ru-RU');
    const routeTitles = ['Без дефицита', `Дефицит до ${threshold} ₸ включительно`, `Дефицит более ${threshold} ₸`];
    return (<section className="approval-matrix-page" aria-labelledby="approval-matrix-title">
      <div className="approval-matrix-heading">
        <h1 id="approval-matrix-title">Матрица согласования</h1>
        <button type="button" onClick={refresh} disabled={state.status === 'loading'}>Обновить</button>
      </div>
      <label>Маршрут <select value={version} onChange={event => { setVersion(Number(event.target.value)); setRoleIndex(''); setStatusIndex(''); }}>
        <option value={1}>Заявка владельца ЦФО</option><option value={2}>Заявка инициатора ЦФО — через владельца</option>
      </select></label>
      {state.status === 'loading' && <p role="status">Загрузка матрицы согласования…</p>}
      {state.status === 'failed' && <div role="alert">
        <p>{approvalMatrixUnavailableMessage}</p>
        <button type="button" onClick={refresh}>Повторить загрузку</button>
      </div>}
      {data !== null && <>
        <h2>Маршрут по дефициту бюджета</h2>
        <div className="approval-matrix-routes">
          {data.routes.map((route, index) => <section key={route.deficitBand}>
            <h3>{routeTitles[index]}</h3>
            <ol>{route.stages.map((stage) => <li key={stage.number}>{approvalRoleLabels[stage.roleCode]}</li>)}</ol>
          </section>)}
        </div>
        <h2>Переходы согласования</h2>
        <div className="approval-matrix-filters">
          <label>Исходный статус
            <select value={statusIndex} onChange={(event) => setStatusIndex(event.target.value)}>
              <option value="">Все статусы</option>
              {data.statuses.map((item, index) => <option key={item.code} value={String(index)}>{item.name}</option>)}
            </select>
          </label>
          <label>Участник
            <select value={roleIndex} onChange={(event) => setRoleIndex(event.target.value)}>
              <option value="">Все участники</option>
              {approvalMatrixRoleCodes.map((code, index) => <option key={code} value={String(index)}>{approvalRoleLabels[code]}</option>)}
            </select>
          </label>
          <button type="button" onClick={() => { setStatusIndex(''); setRoleIndex(''); }} disabled={statusIndex === '' && roleIndex === ''}>Сбросить фильтры</button>
          <p role="status">Показано правил: {rows.length} из {data.transitions.length}</p>
        </div>
        {rows.length === 0 ? <p role="status">
          В выбранной области правил согласования нет. Платежи и закрытие заявки относятся к отдельным операциям.
        </p> : <div className="approval-matrix-table-scroll" role="region" aria-label="Таблица правил согласования" tabIndex={0}>
          <table>
            <caption>Действующие правила согласования, только для просмотра</caption>
            <thead><tr>
              {['Исходный статус', 'Этап', 'Участник', 'Команда', 'Условие', 'Результат', 'Основание'].map((name) => <th key={name} scope="col">{name}</th>)}
            </tr></thead>
            <tbody>{rows.map((row) => <tr key={JSON.stringify(row)}>
              <td>{expenseRequestStatusLabel(row.statusCode)}</td>
              <td>{row.stageNumber === 0 ? '—' : row.stageNumber}</td>
              <td>{approvalRoleLabels[row.roleCode]}</td>
              <td>{approvalCommandLabel(row)}</td>
              <td>{approvalConditionLabels[row.conditionCode]}</td>
              <td>{expenseRequestStatusLabel(row.targetStatusCode)}
                {row.targetStageNumber > 0 ? `, этап ${row.targetStageNumber}` : ''}</td>
              <td>{row.requiresReason ? 'Обязательно' : '—'}</td>
            </tr>)}</tbody>
          </table>
        </div>}
      </>}
    </section>);
}
