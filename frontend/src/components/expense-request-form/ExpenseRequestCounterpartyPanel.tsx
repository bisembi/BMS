import type { CounterpartyResidencyCode, ExpenseRequestDraft, PaymentBasisCode, ReferenceCodeItem, ReferenceDataItem } from '../../types';
import { parseCounterpartyResidencyCode, parsePaymentBasisCode } from '../../utils/expenseRequestSelectValues';
import { opaqueReferenceOptionValue, sameOpaqueIdentifier } from '../../utils/expenseRequestDraft';
import { CounterpartyCombobox } from './CounterpartyCombobox';
import { ExpensePaymentOperationPanel } from './ExpensePaymentOperationPanel';
import { ExpenseReadField, expenseViewDate, expenseViewReference } from './ExpenseRequestReadView';
type Props = {
    requestNumber?: string;
    viewOnly?: boolean;
    /// The draft aggregate this panel edits. Only counterparty and payment-basis
    /// fields are read here; no page-level flags are passed in.
    draft: ExpenseRequestDraft;
    onChange: (patch: Partial<ExpenseRequestDraft>) => void;
    onPaymentBasisChange: (paymentBasisCode: PaymentBasisCode | '') => void;
    onNewCounterpartyChange: (isNewCounterparty: boolean) => void;
    onCounterpartyChange: (counterpartyId: string) => void;
    onResidencyChange: (candidateResidencyCode: CounterpartyResidencyCode | '') => void;
    onNewContractChange: (isNewContract: boolean) => void;
    counterparties: readonly ReferenceDataItem[];
    contracts: readonly ReferenceDataItem[];
    paymentBases: readonly ReferenceCodeItem[];
    counterpartyResidencies: readonly ReferenceCodeItem[];
};
export function ExpenseRequestCounterpartyPanel({ requestNumber, viewOnly = false, draft, onChange, onPaymentBasisChange, onNewCounterpartyChange, onCounterpartyChange, onResidencyChange, onNewContractChange, counterparties, contracts, paymentBases, counterpartyResidencies, }: Props) {
    if (draft.paymentOperation)
        return <ExpensePaymentOperationPanel operation={draft.paymentOperation} requestNumber={requestNumber} viewOnly={viewOnly} onChange={operation => onChange({ paymentOperation: operation })}/>;
    const selectedCounterparty = counterparties.find(item => sameOpaqueIdentifier(item.id, draft.counterpartyId));
    if (viewOnly)
        return <section id="expense-panel-counterparty" role="tabpanel" aria-labelledby="expense-tab-counterparty" className="form-card">
    <div className="section-title">Контрагент и основание оплаты</div>
    <dl className="expense-view-grid">
      <ExpenseReadField label="Контрагент" wide note={!draft.isNewCounterparty
                ? <small className="expense-view-bin">БИН: {selectedCounterparty?.bin || '—'}</small> : undefined}>{draft.isNewCounterparty
                ? 'Новый контрагент'
                : expenseViewReference(counterparties, draft.counterpartyId, 'Выбранный контрагент недоступен в справочнике')}
      </ExpenseReadField>
      {draft.isNewCounterparty && <>
        <ExpenseReadField label="Наименование" wide>{draft.candidateCounterpartyName}</ExpenseReadField>
        <ExpenseReadField label="Резидентность">{counterpartyResidencies.find(item => item.code === draft.candidateResidencyCode)?.name
                    ?? (draft.candidateResidencyCode ? 'Выбранная резидентность недоступна' : 'Не указано')}</ExpenseReadField>
        {draft.candidateResidencyCode === 'ResidentKz' && <ExpenseReadField label="БИН/ИИН">{draft.candidateBinIin}</ExpenseReadField>}
        {draft.candidateResidencyCode === 'NonResident' && <>
          <ExpenseReadField label="Страна">{draft.candidateCountry}</ExpenseReadField>
          <ExpenseReadField label="Регистрационный номер">{draft.candidateRegistrationNumberMissing ? 'Отсутствует' : draft.candidateForeignRegistrationNumber}</ExpenseReadField>
          {draft.candidateRegistrationNumberMissing && <ExpenseReadField label="Пояснение отсутствия номера" wide>{draft.candidateRegistrationNumberMissingExplanation}</ExpenseReadField>}
        </>}
      </>}
      <ExpenseReadField label="Основание оплаты" wide>{paymentBases.find(item => item.code === draft.paymentBasisCode)?.name
                ?? (draft.paymentBasisCode ? 'Выбранное основание недоступно' : 'Не указано')}</ExpenseReadField>
    </dl>
    {draft.paymentBasisCode === 'Contract' && <div className="expense-view-subsection">
      <h3>Договор</h3>
      <dl className="expense-view-grid">
        <ExpenseReadField label="Договор" wide>{draft.isNewContract ? 'Новый договор'
                    : expenseViewReference(contracts, draft.contractId, 'Выбранный договор недоступен в справочнике')}</ExpenseReadField>
        {draft.isNewContract && <>
          <ExpenseReadField label="Номер договора">{draft.candidateContractNumberMissing ? 'Без номера' : draft.candidateContractNumber}</ExpenseReadField>
          <ExpenseReadField label="Дата договора">{expenseViewDate(draft.candidateContractDate)}</ExpenseReadField>
          <ExpenseReadField label="Предмет договора" wide>{draft.candidateContractSubject}</ExpenseReadField>
          {draft.candidateContractTerms.trim() && <ExpenseReadField label="Условия оплаты" wide>{draft.candidateContractTerms}</ExpenseReadField>}
          {draft.newContractDocumentMissing && <>
            <ExpenseReadField label="Файл договора" wide>Отсутствует</ExpenseReadField>
            <ExpenseReadField label="Пояснение отсутствия файла" wide>{draft.newContractDocumentMissingExplanation}</ExpenseReadField>
          </>}
        </>}
      </dl>
    </div>}
    {draft.paymentBasisCode === 'InvoiceWithoutContract' && <div className="expense-view-subsection">
      <h3>Счёт без договора</h3><dl className="expense-view-grid">
        <ExpenseReadField label="Номер счёта">{draft.invoiceNumber}</ExpenseReadField>
        <ExpenseReadField label="Дата счёта">{expenseViewDate(draft.invoiceDate)}</ExpenseReadField>
      </dl>
    </div>}
    {draft.paymentBasisCode === 'PublicOffer' && <div className="expense-view-subsection">
      <h3>Публичная оферта</h3><dl className="expense-view-grid">
        <ExpenseReadField label="Наименование оферты" wide>{draft.publicOfferName}</ExpenseReadField>
        <ExpenseReadField label="Ссылка/идентификатор" wide>{draft.publicOfferIdentifier}</ExpenseReadField>
      </dl>
    </div>}
    {draft.paymentBasisCode === 'OtherBasis' && <div className="expense-view-subsection">
      <h3>Иное основание</h3><dl className="expense-view-grid"><ExpenseReadField label="Пояснение" wide>{draft.otherBasisExplanation}</ExpenseReadField></dl>
    </div>}
  </section>;
    const changeScenario = (patch: Partial<ExpenseRequestDraft>) => {
        const discardsInput = Object.entries(patch).some(([field, value]) => value === ''
            && typeof draft[field as keyof ExpenseRequestDraft] === 'string'
            && String(draft[field as keyof ExpenseRequestDraft]).trim() !== '');
        if (discardsInput && !window.confirm('При переключении будут очищены заполненные реквизиты предыдущего варианта. Продолжить?'))
            return;
        onChange(patch);
    };
    return (<section id="expense-panel-counterparty" role="tabpanel" aria-labelledby="expense-tab-counterparty" className="form-card rz-counterparty-card">
      <div className="section-title">Контрагент и основание оплаты</div>

      <div className="rz-party-row">
        {!draft.isNewCounterparty && (<label className="field required">
            <span>Контрагент</span>
            <CounterpartyCombobox value={draft.counterpartyId} items={counterparties} onChange={onCounterpartyChange}/>
            <small className="counterparty-bin" aria-label="БИН контрагента">БИН: {selectedCounterparty?.bin || '—'}</small>
          </label>)}
        {draft.isNewCounterparty && <div className="field"><span>Контрагент</span><span>Новый контрагент</span></div>}
        <label className="field checkbox-field">
          <input type="checkbox" checked={draft.isNewCounterparty} onChange={(event) => onNewCounterpartyChange(event.target.checked)}/>
          <span>Новый контрагент</span>
        </label>
      </div>

      {draft.isNewCounterparty && (<div className="conditional-section" aria-label="Кандидат нового контрагента">
          <div className="conditional-title">Кандидат нового контрагента</div>
          <div className="form-grid">
            <label className="field required"><span>Наименование</span><input maxLength={300} value={draft.candidateCounterpartyName} onChange={(event) => onChange({ candidateCounterpartyName: event.target.value })}/></label>
            <label className="field required">
              <span>Резидентность</span>
              <select value={draft.candidateResidencyCode} onChange={(event) => onResidencyChange(parseCounterpartyResidencyCode(event.target.value))}>
                <option value="">Не выбрана</option>
                {draft.candidateResidencyCode && !counterpartyResidencies.some((item) => item.code === draft.candidateResidencyCode) && <option value={draft.candidateResidencyCode}>Выбранная резидентность недоступна</option>}
                {counterpartyResidencies.map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}
              </select>
            </label>
            {draft.candidateResidencyCode === 'ResidentKz' && (<label className="field required"><span>БИН/ИИН</span><input maxLength={12} inputMode="numeric" value={draft.candidateBinIin} onChange={(event) => onChange({ candidateBinIin: event.target.value })}/></label>)}
            {draft.candidateResidencyCode === 'NonResident' && (<>
                <label className="field required"><span>Страна</span><input maxLength={100} value={draft.candidateCountry} onChange={(event) => onChange({ candidateCountry: event.target.value })}/></label>
                <label className={`field ${draft.candidateRegistrationNumberMissing ? '' : 'required'}`}><span>Регистрационный номер</span><input maxLength={100} disabled={draft.candidateRegistrationNumberMissing} value={draft.candidateForeignRegistrationNumber} onChange={(event) => onChange({ candidateForeignRegistrationNumber: event.target.value })}/></label>
                <label className="field checkbox-field"><input type="checkbox" checked={draft.candidateRegistrationNumberMissing} onChange={(event) => changeScenario({ candidateRegistrationNumberMissing: event.target.checked, candidateForeignRegistrationNumber: event.target.checked ? '' : draft.candidateForeignRegistrationNumber, candidateRegistrationNumberMissingExplanation: event.target.checked ? draft.candidateRegistrationNumberMissingExplanation : '' })}/><span>Регистрационный номер отсутствует</span></label>
                {draft.candidateRegistrationNumberMissing && <label className="field wide required"><span>Пояснение отсутствия номера</span><textarea rows={2} maxLength={1000} value={draft.candidateRegistrationNumberMissingExplanation} onChange={(event) => onChange({ candidateRegistrationNumberMissingExplanation: event.target.value })}/></label>}
              </>)}
          </div>
        </div>)}

      <div className="form-grid">
        <label className="field required">
          <span>Основание оплаты</span>
          <select value={draft.paymentBasisCode} onChange={(event) => onPaymentBasisChange(parsePaymentBasisCode(event.target.value))}>
            <option value="">Не выбрано</option>
            {draft.paymentBasisCode && !paymentBases.some((item) => item.code === draft.paymentBasisCode) && <option value={draft.paymentBasisCode}>Выбранное основание недоступно</option>}
            {paymentBases.map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}
          </select>
        </label>
      </div>
      {draft.paymentBasisCode === 'Contract' && (<div className="conditional-section" aria-label="Договор">
          <div className="conditional-title">Договор</div>
          <div className="rz-party-row">
            {!draft.isNewContract && <label className="field required"><span>Договор</span><select value={draft.contractId} onChange={(event) => onChange({ contractId: event.target.value })}><option value="">Не выбран</option>{draft.contractId && !contracts.some((item) => sameOpaqueIdentifier(item.id, draft.contractId)) && <option value={draft.contractId}>Выбранный договор недоступен в справочнике</option>}{contracts.map((item) => <option key={item.id} value={opaqueReferenceOptionValue(item.id, draft.contractId)}>{item.name}</option>)}</select></label>}
            {draft.isNewContract && <div className="field"><span>Договор</span><span>Новый договор</span></div>}
            <label className="field checkbox-field"><input type="checkbox" checked={draft.isNewContract} onChange={(event) => onNewContractChange(event.target.checked)}/><span>Новый договор</span></label>
          </div>
          <div className="form-grid">
            {draft.isNewContract && (<>
                <label className={`field ${draft.candidateContractNumberMissing ? '' : 'required'}`}><span>Номер договора</span><input maxLength={100} disabled={draft.candidateContractNumberMissing} value={draft.candidateContractNumber} onChange={(event) => onChange({ candidateContractNumber: event.target.value })}/></label>
                <label className="field checkbox-field"><input type="checkbox" checked={draft.candidateContractNumberMissing} onChange={(event) => changeScenario({ candidateContractNumberMissing: event.target.checked, candidateContractNumber: event.target.checked ? '' : draft.candidateContractNumber })}/><span>Договор без номера</span></label>
                <label className="field required"><span>Дата договора</span><input type="date" value={draft.candidateContractDate} onChange={(event) => onChange({ candidateContractDate: event.target.value })}/></label>
                <label className="field required"><span>Предмет договора</span><input maxLength={300} value={draft.candidateContractSubject} onChange={(event) => onChange({ candidateContractSubject: event.target.value })}/></label>
                <label className="field wide"><span>Условия оплаты</span><textarea rows={2} maxLength={1000} value={draft.candidateContractTerms} onChange={(event) => onChange({ candidateContractTerms: event.target.value })}/></label>
                <label className="field checkbox-field"><input type="checkbox" checked={draft.newContractDocumentMissing} onChange={(event) => changeScenario({ newContractDocumentMissing: event.target.checked, newContractDocumentMissingExplanation: event.target.checked ? draft.newContractDocumentMissingExplanation : '' })}/><span>Файл договора отсутствует</span></label>
                {draft.newContractDocumentMissing && <label className="field wide required"><span>Пояснение отсутствия файла</span><textarea rows={2} maxLength={1000} value={draft.newContractDocumentMissingExplanation} onChange={(event) => onChange({ newContractDocumentMissingExplanation: event.target.value })}/></label>}
              </>)}
          </div>
        </div>)}

      {draft.paymentBasisCode === 'InvoiceWithoutContract' && <div className="conditional-section"><div className="conditional-title">Счёт без договора</div><div className="form-grid"><label className="field required"><span>Номер счёта</span><input maxLength={100} value={draft.invoiceNumber} onChange={(event) => onChange({ invoiceNumber: event.target.value })}/></label><label className="field required"><span>Дата счёта</span><input type="date" value={draft.invoiceDate} onChange={(event) => onChange({ invoiceDate: event.target.value })}/></label></div></div>}
      {draft.paymentBasisCode === 'PublicOffer' && <div className="conditional-section"><div className="conditional-title">Публичная оферта</div><div className="form-grid"><label className="field required"><span>Наименование оферты</span><input maxLength={300} value={draft.publicOfferName} onChange={(event) => onChange({ publicOfferName: event.target.value })}/></label><label className="field required"><span>Ссылка/идентификатор</span><input maxLength={1000} value={draft.publicOfferIdentifier} onChange={(event) => onChange({ publicOfferIdentifier: event.target.value })}/></label></div></div>}
      {draft.paymentBasisCode === 'OtherBasis' && <div className="conditional-section"><div className="conditional-title">Иное основание</div><div className="form-grid"><label className="field wide required"><span>Пояснение</span><textarea rows={3} maxLength={1000} value={draft.otherBasisExplanation} onChange={(event) => onChange({ otherBasisExplanation: event.target.value })}/></label></div></div>}
    </section>);
}
