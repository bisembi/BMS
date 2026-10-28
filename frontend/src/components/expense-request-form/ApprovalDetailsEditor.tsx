import { useCallback, useEffect, useRef, useState } from 'react';
import { getContracts } from '../../api';
import { getApprovalDetails, saveApprovalDetails, ApprovalDetailsRefusedError, type ApprovalDetails, type ApprovalDetailsState } from '../../api/approvalDetails';
import { RecoveredPendingSaveError } from '../../api/expenseRequestErrors';
import type { ReferenceDataItem, SavedExpenseRequest, ExpenseLineDraft } from '../../types';
import { useAccrualDepartments485 } from '../../hooks/useAccrualDepartments485';
import { AccrualDepartmentsNotice485 } from './AccrualDepartmentsNotice485';
import { useWorkspaceRefreshGuard, useWorkspaceRefreshRevision } from '../WorkspaceRefreshContext';
import { CashFlowItemCombobox } from '../reference-directory/CashFlowItemCombobox';
import { matchesCounterpartyNameMask } from './CounterpartyCombobox';
import { approvalDetailsErrorMessage } from '../../utils/uiErrorMessages';
import { AutoGrowTextarea } from './AutoGrowTextarea';
import { ApprovalAllocationEditor, allocationRows, prepareAllocationSave, type AllocationRow } from './ApprovalAllocationEditor';
type Props = {
    id: string;
    version: string;
    locked: boolean;
    organizationId: string;
    counterpartyId: string;
    currencyCode: string;
    lines?: readonly ExpenseLineDraft[];
    onSaved: (r: SavedExpenseRequest) => void;
    onBusyChange: (v: boolean) => void;
};
export function ApprovalDetailsEditor({ id, version, locked, organizationId, counterpartyId, currencyCode, lines, onSaved, onBusyChange }: Props) {
    const revision = useWorkspaceRefreshRevision();
    const [state, setState] = useState<ApprovalDetailsState | null>(null);
    const [draft, setDraft] = useState<ApprovalDetails | null>(null);
    const [open, setOpen] = useState(false);
    const [busy, setBusy] = useState(false);
    const [uncertain, setUncertain] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [contracts, setContracts] = useState<ReferenceDataItem[]>([]);
    const needsDepartments = !!(draft?.allocationLines?.length || draft?.accrualLines?.length);
    const departmentCatalog = useAccrualDepartments485(JSON.stringify([id, version]), open && needsDepartments);
    const departments = departmentCatalog.items;
    const departmentsAvailable = !needsDepartments || departmentCatalog.status === 'ready' && departments.length > 0;
    const [allocations, setAllocations] = useState<AllocationRow[]>([]);
    const sequence = useRef(0);
    const identity = useRef({ id, version });
    identity.current = { id, version };
    useWorkspaceRefreshGuard(open ? 'Сохраните реквизиты заявки или отмените редактирование.' : null);
    useEffect(() => { onBusyChange(open || busy); return () => onBusyChange(false); }, [open, busy, onBusyChange]);
    useEffect(() => {
        const mine = ++sequence.current;
        const abort = new AbortController();
        setState(null);
        setOpen(false);
        setBusy(false);
        setDraft(null);
        setError(null);
        setUncertain(false);
        void getApprovalDetails(id, abort.signal).then(v => {
            if (mine !== sequence.current)
                return;
            if (v && v.requestVersion.toLowerCase() === version.toLowerCase()) {
                setState(v);
                setDraft(v.details);
            }
        })
            .catch(() => { });
        return () => { abort.abort(); ++sequence.current; };
    }, [id, version, revision]);
    useEffect(() => {
        let active = true;
        setContracts([]);
        if (!open || state?.paymentBasisCode !== 'Contract' || state.isNewContract)
            return;
        void getContracts(counterpartyId, organizationId).then(c => {
            if (active)
                setContracts(c.items.filter(x => !x.currencyCode || x.currencyCode === currencyCode));
        })
            .catch(() => {
            if (active)
                setError('Не удалось загрузить договоры. Существующий договор сохранён; повторите открытие редактора.');
        });
        return () => { active = false; };
    }, [open, state?.paymentBasisCode, state?.isNewContract, counterpartyId, organizationId, currencyCode]);
    const patch = useCallback((p: Partial<ApprovalDetails>) => setDraft(d => d ? { ...d, ...p } : d), []);
    const save = async () => {
        if (!draft || locked || busy || !state?.canEdit || !departmentsAvailable)
            return;
        const mine = sequence.current;
        setBusy(true);
        setError(null);
        try {
            let input = draft;
            if (draft.allocationLines) {
                try {
                    input = { ...draft, allocationLines: prepareAllocationSave(allocations, state.details!.allocationLines!) };
                }
                catch (e) {
                    throw new ApprovalDetailsRefusedError(approvalDetailsErrorMessage(e));
                }
            }
            const saved = await saveApprovalDetails(id, version, input);
            if (mine !== sequence.current || identity.current.id !== id || identity.current.version !== version)
                return;
            setBusy(false);
            setUncertain(false);
            setOpen(false);
            onSaved(saved);
        }
        catch (e) {
            if (mine !== sequence.current)
                return;
            if (e instanceof RecoveredPendingSaveError) {
                setBusy(false);
                setOpen(false);
                setUncertain(false);
                onSaved(e.recoveredRequest);
            }
            else {
                setUncertain(!(e instanceof ApprovalDetailsRefusedError));
                setError(approvalDetailsErrorMessage(e));
            }
        }
        finally {
            if (mine === sequence.current)
                setBusy(false);
        }
    };
    if (!state?.canEdit || !draft)
        return null;
    const text = (key: keyof ApprovalDetails, label: string, max = 1000, multiline = false) => <label key={key} className="field"><span>{label}</span>{multiline
            ? <AutoGrowTextarea aria-label={label} rows={key === 'urgencyReason' ? 1 : 2} maxLength={max} value={String(draft[key] ?? '')} onChange={e => patch({ [key]: e.target.value })}/>
            : <input aria-label={label} maxLength={max} value={String(draft[key] ?? '')} onChange={e => patch({ [key]: e.target.value })}/>}</label>;
    return <section className="panel approval-details-editor" aria-label="Редактирование реквизитов при согласовании">
  {!open ? <button type="button" className="secondary-button" disabled={locked || busy} onClick={() => { setDraft(state.details); setAllocations(allocationRows(state.details?.allocationLines ?? [])); setOpen(true); setError(null); }}>Редактировать</button> : <>
   <div className="section-title">Редактирование реквизитов</div>
   <p className="section-hint">Статья ДДС, общая сумма с НДС, бюджетный ЦФО, организация, контрагент, валюта, дата оплаты и основание оплаты заблокированы. Для их исправления верните заявку. Сумму можно разбить по подразделениям затрат. Подразделение, период начисления и НДС — только аналитика акрулов. Согласования и общий резерв сохраняются. Признак акрула, сохранённый при подаче, не меняется.</p>
   <fieldset disabled={locked || busy || uncertain} onKeyDown={e => {
                if (e.key === 'Enter' && e.target instanceof HTMLInputElement)
                    e.preventDefault();
            }}>
    <div className="approval-details-fields">
     {text('purpose', 'Назначение платежа', 1000, true)}{text('comment', 'Комментарий', 1000, true)}{text('businessJustification', 'Обоснование', 1000, true)}
     <div className="approval-details-urgency" role="group" aria-label="Срочность оплаты">
      <label className="approval-details-urgency-toggle"><input type="checkbox" checked={draft.isUrgent} onChange={e => patch({ isUrgent: e.target.checked })}/><span>Срочная заявка</span></label>
      {draft.isUrgent && text('urgencyReason', 'Причина срочности', 1000, true)}
     </div>
     {state.paymentBasisCode === 'InvoiceWithoutContract' && <>{text('invoiceNumber', 'Номер счёта', 100)}<label className="field"><span>Дата счёта</span><input type="date" value={draft.invoiceDate ?? ''} onChange={e => patch({ invoiceDate: e.target.value || null })}/></label></>}
     {state.paymentBasisCode === 'Contract' && (state.isNewContract ? <>
      {text('candidateContractNumber', 'Номер договора', 100)}<label><input type="checkbox" checked={draft.candidateContractNumberMissing} onChange={e => patch({ candidateContractNumberMissing: e.target.checked })}/>Договор без номера</label>
      <label className="field"><span>Дата договора</span><input type="date" value={draft.candidateContractDate ?? ''} onChange={e => patch({ candidateContractDate: e.target.value || null })}/></label>
      {text('candidateContractSubject', 'Предмет договора', 300)}{text('candidateContractTerms', 'Условия оплаты', 1000, true)}
     </> : <div className="field"><span>Договор</span><CashFlowItemCombobox ariaLabel="Договор при редактировании" value={draft.contractId ?? ''} items={contracts} emptyLabel="Не выбран" unavailableLabel="Текущий договор сохранён" formatItem={i => i.name} matchesItem={matchesCounterpartyNameMask} searchPlaceholder="Номер, название или маска * ?" searchSelectsFirstMatch onChange={v => patch({ contractId: v || null })}/><small>Выбор договора этого контрагента. Общий справочник договоров не изменяется.</small></div>)}
     {state.paymentBasisCode === 'PublicOffer' && <>{text('publicOfferName', 'Наименование оферты', 300)}{text('publicOfferIdentifier', 'Идентификатор оферты')}</>}
     {state.paymentBasisCode === 'OtherBasis' && text('otherBasisExplanation', 'Иное основание', 1000, true)}
    </div>
    {draft.allocationLines && draft.allocationLines.length > 0 && <ApprovalAllocationEditor rows={allocations} source={state.details!.allocationLines!} departments={departments} departmentsAvailable={departmentsAvailable} currencyCode={currencyCode} onChange={setAllocations}/>}
    {draft.accrualLines && draft.accrualLines.length > 0 && <div className="expense-table-wrap"><table className="expense-table approval-accrual-table" aria-label="Аналитика начислений при согласовании">
     <colgroup><col style={{ width: '6%' }}/><col style={{ width: '50%' }}/><col style={{ width: '26%' }}/><col style={{ width: '18%' }}/></colgroup>
     <thead><tr><th scope="col">Строка</th><th scope="col">Подразделение затрат</th><th scope="col">Период начисления</th><th scope="col">Ставка НДС</th></tr></thead>
     <tbody>{draft.accrualLines.map((l, i) => {
                    const number = (lines?.findIndex(x => x.id?.toLowerCase() === l.lineId.toLowerCase()) ?? i) + 1;
                    const change = (p: Partial<typeof l>) => patch({ accrualLines: draft.accrualLines!.map(x => x.lineId === l.lineId ? { ...x, ...p } : x) });
                    return <tr key={l.lineId}><td>{number}</td><td><select disabled={!departmentsAvailable} aria-label={`Подразделение начисления, строка ${number}`} value={l.accrualCostCenterId} onChange={e => {
                            if (departmentsAvailable)
                                change({ accrualCostCenterId: e.target.value });
                        }}>
       {!departments.some(x => x.id.toLowerCase() === l.accrualCostCenterId.toLowerCase()) && <option value={l.accrualCostCenterId}>Текущее подразделение</option>}
       {departments.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></td>
       <td><input aria-label={`Период начисления при согласовании, строка ${number}`} type="month" value={l.accrualPeriod.slice(0, 7)} onChange={e => {
                            if (e.target.value)
                                change({ accrualPeriod: `${e.target.value}-01` });
                        }}/></td>
       <td><select aria-label={`Ставка НДС при согласовании, строка ${number}`} value={l.vatRateCode} onChange={e => change({ vatRateCode: e.target.value as 'Vat16' | 'WithoutVat' })}><option value="Vat16">16%</option><option value="WithoutVat">Без НДС</option></select></td></tr>;
                })}</tbody></table></div>}
   </fieldset>
   {needsDepartments && <AccrualDepartmentsNotice485 catalog={departmentCatalog}/>}
   {error && <div className="notice error" role="status">{error}</div>}
   <div className="approval-details-actions"><button type="button" className="primary-button" disabled={locked || busy || !departmentsAvailable} onClick={() => void save()}>{busy ? 'Сохранение…' : uncertain ? 'Повторить сохранение' : 'Сохранить реквизиты'}</button>
    <button type="button" className="secondary-button" disabled={busy || uncertain} onClick={() => { setDraft(state.details); setOpen(false); setError(null); }}>Отменить правки</button></div>
  </>}
 </section>;
}
