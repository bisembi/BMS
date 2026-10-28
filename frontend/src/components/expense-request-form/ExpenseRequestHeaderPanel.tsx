import type { CSSProperties, PropsWithChildren } from 'react';
import { operationNames, switchOperation } from '../../utils/expensePaymentOperation';
import { opaqueReferenceOptionValue, sameOpaqueIdentifier } from '../../utils/expenseRequestDraft';
import type { ExpenseRequestSystemFields } from '../../utils/expenseRequestSystemFields';
import { CompactRequestDate } from './CompactRequestDate';
import { AutoGrowTextarea } from './AutoGrowTextarea';
import { expenseCurrencyPreview } from '../../utils/expenseCurrencyPreview';
import './ExpenseRequestCompactHeader.css';
import type { CurrencyRateRow } from '../../api/currencyRates';
import { ExpenseReadField, expenseViewDate, expenseViewReference } from './ExpenseRequestReadView';
import type { ExpenseRequestDraft, PreliminaryCurrencyCalculation, ReferenceDataItem, SubmissionReadiness } from '../../types';
type Props = PropsWithChildren<{
    /// The draft aggregate this panel edits. Page-level flags stay on the page.
    draft: ExpenseRequestDraft;
    onChange: (patch: Partial<ExpenseRequestDraft>) => void;
    onOrganizationChange: (organizationId: string) => void;
    onDefaultCostCenterChange: (defaultCostCenterId: string) => void;
    onPlannedPaymentDateChange: (plannedPaymentDate: string) => void;
    organizations: readonly ReferenceDataItem[];
    costCenters: readonly ReferenceDataItem[];
    currencies: readonly ReferenceDataItem[];
    savedId: string | null;
    preliminaryCurrencyCalculation: PreliminaryCurrencyCalculation | null;
    submissionReadiness: SubmissionReadiness | null;
    onNavigateToIssue?: (code: string, lineNumber?: number | null) => void;
    hasUnsavedChanges: boolean;
    canEditReturnedPaymentPurpose?: boolean;
    viewOnly?: boolean;
    systemFields: ExpenseRequestSystemFields;
    latestRate?: CurrencyRateRow | null;
    rateLoading?: boolean;
    rateError?: string | null;
}>;
function formatRate(value: number): string {
    return new Intl.NumberFormat('ru-RU', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 6,
    }).format(value);
}
function formatRateDate(value: string): string {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/u.exec(value);
    return match ? `${match[3]}.${match[2]}.${match[1]}` : 'дата недоступна';
}
function isCanonicalRateDate(value: string): boolean {
    if (!/^\d{4}-\d{2}-\d{2}$/u.test(value))
        return false;
    const parsed = new Date(`${value}T00:00:00Z`);
    return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value;
}
function isValidPreliminaryCalculation(calculation: PreliminaryCurrencyCalculation | null, currencyCode: string): calculation is PreliminaryCurrencyCalculation & {
    rateDate: string;
    rate: number;
    multiplicity: number;
    totalAmountKzt: number;
} {
    return calculation !== null
        && calculation.currencyCode === currencyCode
        && typeof calculation.rateDate === 'string'
        && isCanonicalRateDate(calculation.rateDate)
        && typeof calculation.rate === 'number'
        && Number.isFinite(calculation.rate)
        && calculation.rate > 0
        && typeof calculation.multiplicity === 'number'
        && Number.isFinite(calculation.multiplicity)
        && calculation.multiplicity > 0
        && typeof calculation.totalAmountKzt === 'number'
        && Number.isFinite(calculation.totalAmountKzt)
        && calculation.totalAmountKzt >= 0;
}
export function ExpenseRequestHeaderPanel({ draft, onChange, onOrganizationChange, onDefaultCostCenterChange, onPlannedPaymentDateChange, organizations, costCenters, currencies, savedId, preliminaryCurrencyCalculation, systemFields, canEditReturnedPaymentPurpose = false, viewOnly = false, latestRate, rateLoading = false, rateError = null, children, }: Props) {
    const isPayrollAdvance = draft.paymentOperation?.kind === 'PayrollAdvance';
    const isForeignCurrency = draft.currencyCode !== 'KZT';
    const validCalculation = isValidPreliminaryCalculation(preliminaryCurrencyCalculation, draft.currencyCode) ? preliminaryCurrencyCalculation : null;
    const displayRate = latestRate === undefined ? validCalculation : latestRate;
    const previewAmountKzt = expenseCurrencyPreview(draft, latestRate ?? null);
    const rateFieldValue = !isForeignCurrency ? '1' : rateLoading ? 'Загрузка…'
        : displayRate?.rate && displayRate.multiplicity
            ? formatRate(displayRate.rate / displayRate.multiplicity) : 'Не определён';
    const rateFieldStyle = { '--rz-rate-width': `${Math.max(96, (rateFieldValue.length + 2) * 8)}px` } as CSSProperties;
    const persistedPaymentPurpose = draft.purpose.trim();
    const paymentPurposeValue = canEditReturnedPaymentPurpose
        ? draft.purpose : persistedPaymentPurpose || draft.justification;
    const paymentPurposeLocked = persistedPaymentPurpose !== '' && !canEditReturnedPaymentPurpose;
    if (viewOnly)
        return (<section id="expense-panel-main" role="tabpanel" aria-labelledby="expense-tab-main" className="form-card">
      <div className="section-title">Основные данные</div>
      <dl className="expense-view-grid">
        <ExpenseReadField label="Операция" wide>{operationNames[draft.paymentOperation?.kind ?? 'Supplier']}</ExpenseReadField>
        <ExpenseReadField label="Организация">{expenseViewReference(organizations, draft.organizationId, 'Выбранная организация недоступна в справочнике')}</ExpenseReadField>
        <ExpenseReadField label="ЦФО">{expenseViewReference(costCenters, draft.defaultCostCenterId, 'Выбранное ЦФО недоступно в справочнике')}</ExpenseReadField>
        <ExpenseReadField label="Дата оплаты">{expenseViewDate(draft.plannedPaymentDate)}</ExpenseReadField>
        <ExpenseReadField label="Валюта">{draft.currencyCode}</ExpenseReadField>
        {isForeignCurrency && <ExpenseReadField label="Предварительный курс">{rateFieldValue}</ExpenseReadField>}
        {!isPayrollAdvance && <>
          <ExpenseReadField label="Срочность" wide>{draft.isUrgent ? 'Срочная заявка' : 'Обычная'}</ExpenseReadField>
          {draft.isUrgent && <ExpenseReadField label="Причина срочности" wide>{draft.urgencyReason}</ExpenseReadField>}
          <ExpenseReadField label="Назначение платежа" wide>{paymentPurposeValue}</ExpenseReadField>
        </>}
        {draft.comment.trim() && <ExpenseReadField label="Комментарий инициатора" wide>{draft.comment}</ExpenseReadField>}
      </dl>
      {isForeignCurrency && <div className="rz-rate-note" role="status" aria-label="Предварительный валютный расчёт">
        {rateLoading ? 'Загружаем последний курс из справочника…' : rateError ?? (displayRate
                    ? `Последний загруженный курс от ${formatRateDate(displayRate.rateDate!)}. Предварительный расчёт, не фактический курс оплаты.`
                    : 'Курс не загружен.')}
        {!rateLoading && !rateError && previewAmountKzt !== null && <span> Предварительно: {previewAmountKzt.toLocaleString('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} KZT.</span>}
      </div>}
      {children}
      <div className="rz-author">Автор: {systemFields.initiatorLabel}</div>
    </section>);
    return (<section id="expense-panel-main" role="tabpanel" aria-labelledby="expense-tab-main" className="form-card">
    <div className="section-title" id="main-fields-title">Основные данные</div>
    <div className="form-grid rz-primary-fields" style={{ marginBottom: 8 }}><label className="field required"><span>Операция</span>
      <select value={draft.paymentOperation?.kind ?? 'Supplier'} onChange={e => {
            if (e.target.value === (draft.paymentOperation?.kind ?? 'Supplier'))
                return;
            if (!window.confirm('Реквизиты предыдущей операции будут очищены. Суммы и бюджетные статьи сохранятся; для новой операции будет установлен режим без НДС. Продолжить?'))
                return;
            onChange(switchOperation(draft, e.target.value as keyof typeof operationNames));
        }}>{Object.entries(operationNames).filter(([id]) => id !== 'Payroll' || draft.paymentOperation?.kind === 'Payroll').map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select>
    </label></div>
    <div className="form-grid rz-primary-fields">
      <label className="field required rz-organization-field">
        <span>Организация</span>
        <select value={draft.organizationId} onChange={(event) => onOrganizationChange(event.target.value)}>
          <option value="">Не выбрана</option>
          {draft.organizationId && !organizations.some((item) => sameOpaqueIdentifier(item.id, draft.organizationId)) && (<option value={draft.organizationId}>Выбранная организация недоступна в справочнике</option>)}
          {organizations.map((item) => <option key={item.id} value={opaqueReferenceOptionValue(item.id, draft.organizationId)}>{item.name}</option>)}
        </select>
      </label>
      <label className="field required">
        <span>ЦФО</span>
        <select value={draft.defaultCostCenterId} onChange={(event) => onDefaultCostCenterChange(event.target.value)}>
          <option value="">Не выбрано</option>
          {draft.defaultCostCenterId && !costCenters.some((item) => sameOpaqueIdentifier(item.id, draft.defaultCostCenterId)) && (<option value={draft.defaultCostCenterId}>Выбранное ЦФО недоступно в справочнике</option>)}
          {costCenters.map((item) => <option key={item.id} value={opaqueReferenceOptionValue(item.id, draft.defaultCostCenterId)}>{item.name}</option>)}
        </select>
      </label>
    <div className="rz-header-dates">
      <label className="field required rz-payment-date-field"><span>Дата оплаты</span><CompactRequestDate key={savedId ?? 'new'} value={draft.plannedPaymentDate} onChange={onPlannedPaymentDateChange}/></label>
      <label className="field required rz-currency-field">
        <span>Валюта</span>
        <select value={draft.currencyCode} aria-label="Валюта расходной заявки" onChange={(event) => onChange({ currencyCode: event.target.value })}>
          {draft.currencyCode && !currencies.some((item) => item.code === draft.currencyCode) && (<option value={draft.currencyCode}>{draft.currencyCode} · выбранная валюта недоступна</option>)}
          {currencies.map((item) => (<option key={item.id} value={item.code}>{item.code}</option>))}
        </select>
      </label>
    </div>
    {isForeignCurrency && <label className="field rz-preliminary-rate" style={rateFieldStyle}><span>Предварительный курс</span><input aria-label="Предварительный курс черновика" title={rateFieldValue} value={rateFieldValue} disabled/></label>}
    </div>
    <div className="form-grid rz-main-text-fields">
      {!isPayrollAdvance && <>
      <label className="field checkbox-field"><input type="checkbox" checked={draft.isUrgent} onChange={(event) => onChange({ isUrgent: event.target.checked, urgencyReason: event.target.checked ? draft.urgencyReason : '' })}/><span>Срочная заявка</span></label>
      {draft.isUrgent && <label className="field wide required"><span>Причина срочности</span><AutoGrowTextarea className="rz-single-row-textarea" rows={1} maxLength={1000} value={draft.urgencyReason} onChange={(event) => onChange({ urgencyReason: event.target.value })}/></label>}
      <label className="field wide required">
        <span>Назначение платежа</span>
        <AutoGrowTextarea id="payment-purpose" rows={2} maxLength={1000} value={paymentPurposeValue} readOnly={paymentPurposeLocked} aria-readonly={paymentPurposeLocked ? 'true' : undefined} onChange={(event) => {
                if (canEditReturnedPaymentPurpose) {
                    // This single authoring field feeds both the saved bank purpose
                    // and submission justification. Clearing it must not resurrect
                    // the old purpose through the first-submission fallback.
                    onChange({ purpose: event.target.value, justification: event.target.value });
                }
                else if (!paymentPurposeLocked)
                    onChange({ justification: event.target.value });
            }} placeholder="Укажите назначение платежа"/>
      </label>
      </>}
      <label className="field wide"><span>Комментарий инициатора</span><AutoGrowTextarea className="rz-single-row-textarea" rows={1} value={draft.comment} onChange={(event) => onChange({ comment: event.target.value })}/></label>
    </div>

    {isForeignCurrency && <div className="rz-rate-note" role="status" aria-label="Предварительный валютный расчёт">
      {rateLoading ? 'Загружаем последний курс из справочника…' : rateError ?? (displayRate
                ? `Последний загруженный курс от ${formatRateDate(displayRate.rateDate!)}. При подаче сервер проверит его актуальность.`
                : 'Курс не загружен. Загрузите его или введите вручную в справочнике «Курсы валют».')}
      {!rateLoading && !rateError && previewAmountKzt !== null && <span> Предварительно: {previewAmountKzt.toLocaleString('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} KZT.</span>}
    </div>}

    {children}
    <div className="rz-author">Автор: {systemFields.initiatorLabel}</div>
    </section>);
}
