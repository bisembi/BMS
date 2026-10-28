import { useWorkspaceRefreshGuard } from '../WorkspaceRefreshContext';
import { Ban, Check, CircleX, Database, RotateCcw, WalletCards } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useExpenseRequestNsiContracts } from '../../hooks/useExpenseRequestNsiContracts';
import type { ReferenceDataItem } from '../../types';
import { sameOpaqueIdentifier } from '../../utils/expenseRequestDraft';
import type { ExpenseRequestWorkflowCommandAvailability, ExpenseRequestWorkflowCommandCode } from '../../api/expenseRequestWorkflowCommandPublicResponse';
type Props = {
    commands: readonly ExpenseRequestWorkflowCommandAvailability[];
    loading: boolean;
    mutationLocked?: boolean;
    executingCode: ExpenseRequestWorkflowCommandCode | null;
    error: string | null;
    cancelButtonTarget?: HTMLElement | null;
    counterparties: readonly ReferenceDataItem[];
    initialCounterpartyId?: string | null;
    initialContractId?: string | null;
    /**
     * W562. What the request itself claims is new. The server accepts a link only
     * for a claim the request actually made, so these decide which link the
     * confirmation asks for and sends. They default to both, which is what the
     * bar assumed before it was told.
     */
    isNewCounterparty?: boolean;
    isNewContract?: boolean;
    /**
     * W562-R. The scope the imported contracts are read in: the organisation of
     * the request being confirmed, and - when only its contract is new - the
     * counterparty it already has.
     */
    organizationId?: string | null;
    requestCounterpartyId?: string | null;
    /**
     * W562-R2. Which request this panel belongs to. Two cards can arrive with the
     * same absent links, and without this the choice a person made on one of them
     * would still be on screen - and in the command - on the other.
     */
    requestId?: string | null;
    onExecute: (code: ExpenseRequestWorkflowCommandCode, reason?: string, counterpartyId?: string | null, contractId?: string | null) => Promise<unknown>;
};
const icons: Record<ExpenseRequestWorkflowCommandCode, typeof Check> = {
    APPROVE: Check,
    RETURN: RotateCcw,
    REJECT: CircleX,
    CANCEL: Ban,
    CONFIRM_NSI: Database,
    SEND_TO_PAYMENT: WalletCards,
};
function safeIndex(items: readonly ReferenceDataItem[], id: string | null): string {
    if (id === null)
        return '';
    const index = items.findIndex((item) => sameOpaqueIdentifier(item.id, id));
    return index < 0 ? '' : String(index);
}
/**
 * W562-R2. The option value of a real choice, or nothing at all.
 *
 * The placeholder carries an empty value, and `Number('')` is 0 - so reading
 * the value as a number turned "Выберите…" into "the first one in the list".
 * Returning to the placeholder is a person taking their choice back, and it
 * must clear the selection rather than quietly make another one.
 */
function chosenId(items: readonly ReferenceDataItem[], value: string): string | null {
    if (value === '')
        return null;
    const index = Number(value);
    if (!Number.isInteger(index) || index < 0 || index >= items.length)
        return null;
    return items[index]?.id ?? null;
}
/** The identifier only while it is one of the items currently on offer. */
function selectionInCatalogue(items: readonly ReferenceDataItem[], id: string | null): string | null {
    if (id === null)
        return null;
    return items.some((item) => sameOpaqueIdentifier(item.id, id)) ? id : null;
}
/// The server-provided command projection is already allowlisted by the API
/// parser. This component only renders those fixed labels and never reads a
/// server error or diagnostic string directly.
export function ExpenseRequestWorkflowCommandBar({ commands, loading, mutationLocked = false, executingCode, error, cancelButtonTarget = null, counterparties, initialCounterpartyId = null, initialContractId = null, isNewCounterparty = true, isNewContract = true, organizationId = null, requestCounterpartyId = null, requestId = null, onExecute, }: Props) {
    const [reasonCode, setReasonCode] = useState<ExpenseRequestWorkflowCommandCode | null>(null);
    const [reason, setReason] = useState('');
    const [approvalComment, setApprovalComment] = useState('');
    const [counterpartyId, setCounterpartyId] = useState<string | null>(initialCounterpartyId);
    const [contractId, setContractId] = useState<string | null>(initialContractId);
    useWorkspaceRefreshGuard(reasonCode !== null || reason !== '' || approvalComment !== '' || executingCode !== null
        || counterpartyId !== initialCounterpartyId || contractId !== initialContractId
        ? 'Сначала завершите команду заявки или отмените ввод в её панели.' : null);
    // The panel belongs to one request. A different card starts from that card's
    // own links, even when both of them start from none.
    useEffect(() => {
        setCounterpartyId(initialCounterpartyId);
        setContractId(initialContractId);
    }, [initialContractId, initialCounterpartyId, requestId]);
    useEffect(() => { setApprovalComment(''); setReason(''); setReasonCode(null); }, [requestId]);
    // W562-R. The contracts of exactly one organisation and one counterparty:
    // the imported counterparty the specialist has just chosen, or the one the
    // request already has when only its contract is new. The draft form still
    // loads nothing for a candidate - that rule is about editing a request that
    // names an object which does not exist yet, and it is untouched.
    const nsiContractCounterpartyId = (isNewCounterparty
        ? counterpartyId
        : requestCounterpartyId) ?? '';
    const nsiContracts = useExpenseRequestNsiContracts({
        organizationId: organizationId ?? '',
        counterpartyId: nsiContractCounterpartyId,
        enabled: isNewContract && commands.some((command) => command.code === 'CONFIRM_NSI'),
    });
    const contracts = nsiContracts.contracts;
    // A contract belongs to the organisation and the counterparty it was read
    // for. When either changes, the previous choice is not merely stale - it may
    // belong to someone else - so it goes immediately, without waiting for the
    // new catalogue to arrive. Only a change: on the first render the card's own
    // contract is the choice, and clearing it here would throw away what the
    // request already has.
    const contractScope = `${organizationId ?? ''}|${nsiContractCounterpartyId}`;
    const readContractsFor = useRef(contractScope);
    useEffect(() => {
        if (readContractsFor.current === contractScope)
            return;
        readContractsFor.current = contractScope;
        setContractId(null);
    }, [contractScope]);
    useEffect(() => {
        if (reasonCode !== null && !commands.some((command) => command.code === reasonCode)) {
            setReasonCode(null);
            setReason('');
        }
    }, [commands, reasonCode]);
    const counterpartyIndex = useMemo(() => safeIndex(counterparties, counterpartyId), [counterparties, counterpartyId]);
    const contractIndex = useMemo(() => safeIndex(contracts, contractId), [contracts, contractId]);
    // W562-R2. What may actually be sent. A choice counts only while it is in the
    // catalogue this panel is showing right now: a list that is still loading, a
    // list that failed to load, and a list that no longer holds the chosen object
    // are all "nothing chosen", not "the old one".
    const selectedCounterpartyId = selectionInCatalogue(counterparties, counterpartyId);
    const contractCatalogueReady = !nsiContracts.loading && nsiContracts.error === null;
    const selectedContractId = contractCatalogueReady
        ? selectionInCatalogue(contracts, contractId)
        : null;
    if (!loading && commands.length === 0 && error === null)
        return null;
    /**
     * W561. Only the command that confirms reference data carries reference
     * identifiers. Sending them with anything else is refused by the transport -
     * correctly, since they mean nothing there - and until this was narrowed,
     * every request that named a counterparty could not be approved, returned or
     * rejected from the card at all.
     */
    const nsiLinksFor = (command: ExpenseRequestWorkflowCommandAvailability): [
        string | null,
        string | null
    ] => command.code === 'CONFIRM_NSI'
        ? [
            // W562. And only the identifier the request is waiting for. A link for
            // something it never claimed is refused by the server, so sending both
            // by habit closed the path for every request that claimed one.
            isNewCounterparty ? selectedCounterpartyId : null,
            isNewContract ? selectedContractId : null,
        ]
        : [null, null];
    const submitReason = (command: ExpenseRequestWorkflowCommandAvailability) => {
        if (mutationLocked || executingCode !== null || reason.trim() === '')
            return;
        const [linkedCounterpartyId, linkedContractId] = nsiLinksFor(command);
        void onExecute(command.code, reason.trim(), linkedCounterpartyId, linkedContractId)
            .then((result) => {
            if (result !== null && result !== undefined) {
                setReasonCode(null);
                setReason('');
                setApprovalComment('');
            }
        });
    };
    return (<section className="workflow-command-panel" aria-label="Бизнес-команды заявки" hidden={cancelButtonTarget !== null && !loading && error === null && reasonCode === null
            && commands.every(command => command.code === 'CANCEL')}>
      <div className="workflow-command-heading">
        <div>
          <div className="section-title">Доступные действия</div>
          <div className="section-hint">Права и текущий этап проверяются сервером повторно.</div>
        </div>
        {loading && <span className="muted">Загрузка…</span>}
      </div>

      {error !== null && <div className="notice error" role="alert">{error}</div>}

      {commands.length > 0 && (<div className="workflow-command-list">
          {commands.map((command) => {
                const Icon = icons[command.code];
                const waitingForReason = reasonCode === command.code;
                const waitingForNsiLinks = command.requiresNsiLinks
                    && ((isNewCounterparty && selectedCounterpartyId === null)
                        || (isNewContract && selectedContractId === null));
                // W562/W562-R. The confirmation is the one command whose readiness
                // depends on a payload: the server evaluates availability without
                // one, so the condition it waits for - a link to an imported object
                // - can never be satisfied there, and it answers "not enabled"
                // every time. That answer is taken as "no link yet" for this
                // command alone, and only while the request actually claims
                // something and the choice for it is complete. Every other command,
                // and every other reason to be disabled, is left to the server.
                const readyToConfirm = command.code === 'CONFIRM_NSI'
                    && command.requiresNsiLinks
                    && (isNewCounterparty || isNewContract)
                    && !waitingForNsiLinks;
                const enabledForNow = command.isEnabled || readyToConfirm;
                const disabled = mutationLocked || !enabledForNow
                    || executingCode !== null
                    || (command.requiresReason && waitingForReason && reason.trim() === '')
                    || waitingForNsiLinks;
                const inToolbar = command.code === 'CANCEL' && cancelButtonTarget !== null;
                const commandButton = <button className="secondary-button" data-workflow-command={command.code} type="button" disabled={disabled} title={waitingForNsiLinks
                        ? 'Выберите активные объекты НСИ'
                        : enabledForNow
                            ? undefined
                            : 'Команда недоступна на текущем этапе'} onClick={() => {
                        if (command.requiresReason && !waitingForReason) {
                            setReasonCode(command.code);
                            setReason(command.code === 'CANCEL' ? '' : approvalComment);
                            return;
                        }
                        if (command.requiresReason) {
                            submitReason(command);
                            return;
                        }
                        const [linkedCounterpartyId, linkedContractId] = nsiLinksFor(command);
                        void onExecute(command.code, approvalComment.trim() || undefined, linkedCounterpartyId, linkedContractId).then(result => {
                            if (result !== null && result !== undefined)
                                setApprovalComment('');
                        });
                    }}>
                  <Icon size={16}/>
                  {executingCode === command.code ? 'Выполнение…' : command.code === 'CANCEL' ? 'Отменить заявку' : command.label}
                </button>;
                return (<div className="workflow-command" key={command.code} hidden={inToolbar && !waitingForReason}>
                {inToolbar ? createPortal(commandButton, cancelButtonTarget) : commandButton}

                {waitingForReason && (<div className="workflow-command-editor">
                    <label>
                      <span>Причина обязательна</span>
                      <textarea value={reason} maxLength={2000} rows={2} onChange={(event) => setReason(event.target.value)} aria-label={`Причина: ${command.code === 'CANCEL' ? 'Отменить заявку' : command.label}`}/>
                    </label>
                    <button className="primary-button compact" data-workflow-command={command.code} type="button" disabled={disabled} onClick={() => submitReason(command)}>
                      Выполнить
                    </button>
                    <button className="secondary-button compact" type="button" disabled={executingCode !== null} onClick={() => {
                            setReasonCode(null);
                            setReason('');
                        }}>
                      Отмена
                    </button>
                  </div>)}

                {command.requiresNsiLinks && (<div className="workflow-command-editor workflow-command-nsi">
                    {isNewCounterparty && (<label>
                      <span>Контрагент НСИ</span>
                      <select value={counterpartyIndex} onChange={(event) => {
                                setCounterpartyId(chosenId(counterparties, event.target.value));
                            }}>
                        <option value="">Выберите контрагента</option>
                        {counterparties.map((item, index) => (<option key={`${item.id}:${index}`} value={String(index)}>{item.name}</option>))}
                      </select>
                    </label>)}
                    {isNewContract && nsiContracts.error !== null && (<p className="notice error" role="alert">{nsiContracts.error}</p>)}
                    {isNewContract && (<label>
                      <span>Договор НСИ</span>
                      <select value={contractIndex} disabled={nsiContracts.loading} onChange={(event) => {
                                setContractId(chosenId(contracts, event.target.value));
                            }}>
                        <option value="">
                          {nsiContracts.loading ? 'Загружаем договоры…' : 'Выберите договор'}
                        </option>
                        {contracts.map((item, index) => (<option key={`${item.id}:${index}`} value={String(index)}>{item.name}</option>))}
                      </select>
                    </label>)}
                  </div>)}
              </div>);
            })}
          {commands.some(command => ['APPROVE', 'CONFIRM_NSI', 'SEND_TO_PAYMENT'].includes(command.code)) && (<label className="workflow-approval-comment360">
              <span>Комментарий к согласованию (необязательно)</span>
              <input type="text" value={approvalComment} maxLength={2000} disabled={mutationLocked || executingCode !== null || loading} placeholder="Например: в оплату на следующую неделю" onChange={event => setApprovalComment(event.target.value)}/>
            </label>)}
        </div>)}
    </section>);
}
