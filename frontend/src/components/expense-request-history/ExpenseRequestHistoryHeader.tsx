type Props = {
    showCount: boolean;
    shown: number;
    total: number;
};
export function ExpenseRequestHistoryHeader({ showCount, shown, total }: Props) {
    return (<div className="lines-heading">
      <div>
        <div className="section-title">История изменений</div>
        <div className="section-hint">События показаны от новых к старым. Время указано в UTC+5.</div>
        <div className="section-hint">Значения и суммы — на момент события; названия — по текущим справочникам.</div>
      </div>
      {showCount && <span className="history-count">Показано {shown} из {total}</span>}
    </div>);
}
