import type { ManualDeficitPage } from '../../api/manualDeficitTypes';
import { deficitDate, deficitMoney } from './manualDeficitFormat';
type Props = {
    page: ManualDeficitPage;
    disabled: boolean;
    selected: string | null;
    onOpen: (id: string, button: HTMLButtonElement) => void;
};
export function ManualDeficitList({ page, disabled, selected, onOpen }: Props) {
    if (page.items.length === 0)
        return <p role="status">В выбранной области дефицитов не найдено.</p>;
    return <div className="manual-deficit-table-wrap" role="region" aria-label="Таблица дефицитов" tabIndex={0}>
    <p>Суммы разных валют не складываются между собой.</p>
    <table>
      <caption>Дефициты переплаты — суммы не складываются между валютами</caption>
      <thead><tr><th scope="col">Возникновение, UTC+5</th><th scope="col">Организация</th>
        <th scope="col">Контрагент</th><th scope="col">Остаток</th><th scope="col">Остаток KZT</th>
        <th scope="col">Корректировки</th><th scope="col">Действие</th></tr></thead>
      <tbody>{page.items.map((item) => <tr key={item.id} className={selected?.toLowerCase() === item.id.toLowerCase() ? 'selected' : undefined}>
        <td>{deficitDate(item.occurredAtUtc)}</td><td>{item.organizationName}</td><td>{item.counterpartyName}</td>
        <td className="money">{deficitMoney(item.currentAmount, item.currencyCode)}</td>
        <td className="money">{deficitMoney(item.currentAmountKzt, 'KZT')}</td>
        <td>{item.hasPendingCorrection ? 'Ожидает решения' : item.currentAmount === 0 ? 'Дефицит погашен' : `Предложений: ${item.correctionCount}`}</td>
        <td><button type="button" disabled={disabled} aria-label={`Открыть дефицит: ${item.counterpartyName}, ${item.currencyCode}`} onClick={(event) => onOpen(item.id, event.currentTarget)}>Открыть</button></td>
      </tr>)}</tbody>
    </table>
  </div>;
}
