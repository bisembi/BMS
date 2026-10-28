import { CheckCircle2, Download, FileCheck2, LockKeyhole } from 'lucide-react';
import { useId, useState } from 'react';
import { FINANCE_WORKSPACES, type FinanceWorkspaceId } from '../financeWorkspaces';
import type { FinanceCatalogs } from '../hooks/useFinanceWorkspaceData';
import { financePreparationDocument, validateFinancePreparation, type FinancePreparation } from '../utils/financeWorkspaceDraft';
import { formatMoneyInput, normalizeMoneyInput } from '../utils/expenseRequestDraft';
type Props = {
    workspaceId: FinanceWorkspaceId;
    value: FinancePreparation;
    onChange: (value: FinancePreparation) => void;
    catalogs: FinanceCatalogs;
    catalogsUnavailable: boolean;
    basis: string | null;
    onChooseBasis: () => void;
};
export function FinancePreparationForm({ workspaceId, value, onChange, catalogs, catalogsUnavailable, basis, onChooseBasis }: Props) {
    const definition = FINANCE_WORKSPACES[workspaceId];
    const [errors, setErrors] = useState<string[]>([]);
    const [checked, setChecked] = useState(false);
    const [downloaded, setDownloaded] = useState(false);
    const [preview, setPreview] = useState<string | null>(null);
    const boundaryId = useId();
    const change = (field: keyof FinancePreparation, text: string) => {
        onChange({ ...value, [field]: text });
        setChecked(false);
        setDownloaded(false);
        setErrors([]);
        setPreview(null);
    };
    const validate = () => {
        const next = validateFinancePreparation(value, workspaceId);
        if (catalogsUnavailable)
            next.push('Дождитесь загрузки справочников.');
        if (value.organizationId && !catalogs.organizations.some(item => item.id === value.organizationId))
            next.push('Выбранная организация отсутствует в доступном справочнике.');
        if (value.counterpartyId && !catalogs.counterparties.some(item => item.id === value.counterpartyId))
            next.push('Выбранный контрагент отсутствует в доступном справочнике.');
        if (value.currency && !catalogs.currencies.some(item => item.code === value.currency))
            next.push('Выбранная валюта отсутствует в доступном справочнике.');
        setErrors(next);
        setChecked(next.length === 0);
        setDownloaded(false);
        return next.length === 0;
    };
    const document = () => financePreparationDocument(workspaceId, value, {
        organization: catalogs.organizations.find(item => item.id === value.organizationId)?.name ?? '',
        counterparty: catalogs.counterparties.find(item => item.id === value.counterpartyId)?.name ?? '',
    }, basis);
    const download = () => {
        if (!validate())
            return;
        try {
            const blob = new Blob([JSON.stringify(document(), null, 2)], { type: 'application/json;charset=utf-8' });
            const url = URL.createObjectURL(blob);
            const anchor = window.document.createElement('a');
            anchor.href = url;
            anchor.download = `bms-${workspaceId}-unposted-draft.json`;
            window.document.body.append(anchor);
            try {
                anchor.click();
            }
            finally {
                anchor.remove();
                window.setTimeout(() => URL.revokeObjectURL(url), 1000);
            }
            setDownloaded(true);
        }
        catch {
            setErrors(['Не удалось подготовить файл. Черновик остаётся в форме.']);
        }
    };
    return <section className="finance-preparation" aria-label="Подготовка документа">
    <div className="finance-card-heading"><FileCheck2 size={20}/><div><h2>Подготовка документа</h2><p>Локальный черновик — без записи в BMS</p></div></div>
    <ol className="finance-steps" aria-label="Этапы подготовки">
      <li className="is-current"><span>1</span>Реквизиты</li><li><span>2</span>Проверка заполнения</li><li><LockKeyhole size={15}/>Проведение не подключено</li>
    </ol>
    <form onSubmit={event => { event.preventDefault(); validate(); }} noValidate>
      <div className="finance-form-grid">
        <label>Дата документа<input type="date" value={value.operationDate} onChange={event => change('operationDate', event.target.value)} required/></label>
        <label>Организация<select value={value.organizationId} disabled={catalogsUnavailable} onChange={event => change('organizationId', event.target.value)} required><option value="">Выберите организацию</option>{catalogs.organizations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Контрагент<select value={value.counterpartyId} disabled={catalogsUnavailable} onChange={event => change('counterpartyId', event.target.value)} required><option value="">Выберите контрагента</option>{catalogs.counterparties.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label>Валюта<select value={value.currency} disabled={catalogsUnavailable} onChange={event => change('currency', event.target.value)} required><option value="">Выберите валюту</option>{catalogs.currencies.map(item => <option key={item.id} value={item.code}>{item.code} — {item.name}</option>)}</select></label>
        <label>{definition.draft === 'reconciliation' ? 'Контрольная сумма документа' : 'Сумма документа'}<input inputMode="decimal" maxLength={24} value={formatMoneyInput(value.amount)} onChange={event => change('amount', normalizeMoneyInput(event.target.value))} placeholder="Введите сумму" required/></label>
        <label>Номер документа-основания<input maxLength={100} value={value.sourceNumber} onChange={event => change('sourceNumber', event.target.value)} placeholder="Номер из первичного документа"/></label>
        {definition.draft === 'reconciliation' && <>
          <label>Начало периода сверки<input type="date" value={value.periodFrom} onChange={event => change('periodFrom', event.target.value)} required/></label>
          <label>Конец периода сверки<input type="date" value={value.periodTo} onChange={event => change('periodTo', event.target.value)} required/></label>
        </>}
        {definition.draft === 'offset' && <label className="finance-span-two">Встречное основание<input maxLength={500} value={value.secondBasis} onChange={event => change('secondBasis', event.target.value)} placeholder="Документ и сторона встречного обязательства" required/></label>}
        <label className="finance-span-two">Основание или пояснение<textarea rows={4} maxLength={2000} value={value.reason} onChange={event => change('reason', event.target.value)} placeholder="Опишите основание операции. Не указывайте пароли или секреты." required/></label>
      </div>
      <div className="finance-basis"><div><strong>Связанная заявка</strong><p>{basis ?? 'Не выбрана. При необходимости выберите заявку в списке оснований.'}</p></div><button type="button" onClick={onChooseBasis}>Выбрать основание</button></div>
      {errors.length > 0 && <div className="finance-error" role="alert"><strong>Проверьте заполнение</strong><ul>{errors.map(error => <li key={error}>{error}</li>)}</ul></div>}
      {checked && <p className="finance-success" role="status"><CheckCircle2 size={17}/>{downloaded ? 'Файл подготовлен для скачивания. Документ не сохранён в BMS и не проведён.' : 'Поля заполнены. Полномочия, остатки и допустимость операции сервером не проверялись.'}</p>}
      <div className="finance-form-actions">
        <button className="finance-primary" type="submit"><CheckCircle2 size={16}/>Проверить заполнение</button>
        <button type="button" onClick={() => {
            if (validate())
                setPreview(JSON.stringify(document(), null, 2));
        }}>Предпросмотр черновика</button>
        <button type="button" onClick={download}><Download size={16}/>Скачать черновик</button>
        <button type="button" disabled aria-describedby={boundaryId}><LockKeyhole size={16}/>Провести в BMS</button>
      </div>
      <p className="finance-footnote" id={boundaryId}>Проведение недоступно: серверная команда не подключена. Черновики сохраняются только в памяти этой вкладки при переходах между разделами; обновление страницы их очистит. Для сохранения скачайте файл.</p>
      {preview && <section className="finance-preview" aria-label="Предпросмотр неподписанного черновика"><h3>Неподписанный черновик</h3><textarea aria-label="Содержимое черновика" readOnly rows={12} value={preview}/><button type="button" onClick={() => setPreview(null)}>Закрыть предпросмотр</button></section>}
    </form>
  </section>;
}
