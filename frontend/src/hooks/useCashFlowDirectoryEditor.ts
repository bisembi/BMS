import { useEffect, useRef, useState } from 'react';
import { cashFlowAccrualErrorMessage, cashFlowAccrualReadErrorMessage, getCashFlowAccrualSettings, isCashFlowAccrualResultUncertain, updateCashFlowAccrualSettings, type CashFlowAccrualCommand, type CashFlowAccrualSettings } from '../api/cashFlowAccrualSettings';
import { isPublicExpenseRequestText, isPublicExpenseRequestUuid } from '../api/expenseRequestPublicResponse';
import { CASH_FLOW_ACTIVITY_TYPES, type CashFlowActivityType, type ReferenceDataItem } from '../types';
type Fields = {
    name: string;
    flowDirection: 'Payment' | 'Receipt' | '';
    activityType: CashFlowActivityType | '';
    groupName: string;
    costCenterId: string;
    enabled: boolean;
    expenseId: string | null;
};
type Draft = Fields & {
    item: ReferenceDataItem;
};
type Phase = 'loading' | 'editing' | 'saving' | 'uncertain' | 'refresh' | 'refreshing';
type Attempt = {
    command: CashFlowAccrualCommand;
    key: string;
    acknowledged: boolean;
};
type Entry = {
    draft: Draft;
    patch: Partial<Fields>;
    baseline: CashFlowAccrualSettings | null;
    phase: Phase;
    error: string | null;
    controller: AbortController;
    attempt: Attempt | null;
    uncertain: boolean;
};
const sameId = (a?: string | null, b?: string | null) => (a?.toLowerCase() ?? '') === (b?.toLowerCase() ?? '');
const fields = (current: CashFlowAccrualSettings): Fields => ({ name: current.name, activityType: current.activityType ?? '',
    flowDirection: current.flowDirection ?? '',
    groupName: current.groupName ?? '', costCenterId: current.costCenter?.id ?? '',
    enabled: current.formsManagerialAccrual, expenseId: current.expenseItem?.id ?? null });
const editable = (current: CashFlowAccrualSettings) => current.canEdit && !!current.activityType && current.groupName !== undefined;
function matches(current: CashFlowAccrualSettings, command: CashFlowAccrualCommand) {
    return current.formsManagerialAccrual === command.formsManagerialAccrual
        && (!command.flowDirectionSpecified || current.flowDirection === command.flowDirection)
        && (!command.nameSpecified || current.name === command.name)
        && sameId(current.expenseItem?.id, command.expenseItemId)
        && current.activityType === command.activityType
        && (current.groupName?.toLocaleLowerCase() ?? '') === (command.groupName?.toLocaleLowerCase() ?? '')
        && (!command.costCenterIdSpecified || sameId(current.costCenter?.id, command.costCenterId));
}
function commandFor(entry: Entry): CashFlowAccrualCommand {
    const { draft, baseline } = entry;
    return { expectedVersion: baseline!.version, formsManagerialAccrual: draft.enabled,
        ...(draft.flowDirection !== (baseline!.flowDirection ?? '')
            ? { flowDirection: draft.flowDirection || null, flowDirectionSpecified: true } : {}),
        ...(draft.name.trim() !== baseline!.name ? { name: draft.name.trim(), nameSpecified: true } : {}),
        expenseItemId: draft.enabled ? draft.expenseId : null, activityType: draft.activityType || null,
        groupName: draft.groupName.trim() || null, classificationSpecified: true,
        ...(!sameId(draft.costCenterId, baseline!.costCenter?.id)
            ? { costCenterId: draft.costCenterId || null, costCenterIdSpecified: true } : {}) };
}
/** One UI queue, independently versioned SQL commands. Not a multi-row SQL transaction. */
export function useCashFlowDirectoryEditor(acquire: () => boolean, release: () => void, onSaved: () => void) {
    const [entries, setEntries] = useState<ReadonlyMap<string, Entry>>(new Map());
    const live = useRef(new Map<string, Entry>());
    const busy = useRef(false);
    const mounted = useRef(true);
    const [saving, setSaving] = useState(false);
    const [notice, setNotice] = useState<string | null>(null);
    useEffect(() => {
        mounted.current = true;
        return () => {
            mounted.current = false;
            for (const entry of live.current.values())
                entry.controller.abort();
        };
    }, []);
    function publish(next: Map<string, Entry>) {
        live.current = next;
        if (mounted.current)
            setEntries(next);
        if (next.size === 0)
            release();
    }
    function put(id: string, entry: Entry) { const next = new Map(live.current); next.set(id, entry); publish(next); }
    function remove(id: string) { const next = new Map(live.current); next.delete(id); publish(next); }
    function discard() {
        // Local abandonment is allowed only when no command might have committed.
        // Check again after the caller's confirmation, not only in disabled UI.
        if (busy.current || [...live.current.values()].some(entry => entry.uncertain || entry.attempt?.acknowledged))
            return false;
        for (const entry of live.current.values())
            entry.controller.abort();
        publish(new Map());
        setNotice(null);
        return true;
    }
    function change(item: ReferenceDataItem, patch: Partial<Fields>) {
        const id = item.id.toLowerCase();
        if (busy.current || [...live.current.values()].some(entry => entry.uncertain))
            return;
        const existing = live.current.get(id);
        if (existing) {
            if (existing.phase !== 'editing' && existing.phase !== 'loading')
                return;
            put(id, { ...existing, draft: { ...existing.draft, ...patch }, patch: { ...existing.patch, ...patch }, error: null });
            setNotice(null);
            return;
        }
        if (!acquire())
            return;
        const controller = new AbortController();
        put(id, { draft: { item, name: item.name, activityType: item.activityType ?? '', groupName: item.parentName ?? '',
                flowDirection: item.flowDirection ?? '',
                costCenterId: item.costCenterId ?? '', enabled: item.formsManagerialAccrual ?? false, expenseId: null, ...patch },
            patch, baseline: null, phase: 'loading', error: null, controller, attempt: null, uncertain: false });
        setNotice(null);
        void (async () => {
            try {
                const current = await getCashFlowAccrualSettings(id, controller.signal);
                const entry = live.current.get(id);
                if (controller.signal.aborted || !entry)
                    return;
                const unchanged = editable(current) && current.name === item.name && current.activityType === item.activityType
                    && (current.flowDirection ?? '') === (item.flowDirection ?? '')
                    && (current.groupName ?? '') === (item.parentName ?? '') && sameId(current.costCenter?.id, item.costCenterId)
                    && current.formsManagerialAccrual === item.formsManagerialAccrual
                    && (current.expenseItem?.name ?? null) === (item.accrualExpenseItemName ?? null);
                put(id, { ...entry, baseline: current, draft: { ...entry.draft,
                        expenseId: entry.patch.expenseId !== undefined ? entry.patch.expenseId : current.expenseItem?.id ?? null },
                    phase: unchanged ? 'editing' : 'refresh', error: unchanged ? null : 'Данные или права изменились. Обновите проверку изменений.' });
            }
            catch (failure) {
                const entry = live.current.get(id);
                if (!controller.signal.aborted && entry)
                    put(id, { ...entry, phase: 'refresh', error: cashFlowAccrualReadErrorMessage(failure) });
            }
        })();
    }
    async function save() {
        if (busy.current || live.current.size === 0 || [...live.current.values()].some(entry => !['editing', 'uncertain'].includes(entry.phase)))
            return;
        // Validate the entire queue, including rows outside the current search/page,
        // BEFORE freezing keys or issuing the first write.
        let invalid = false;
        for (const [id, entry] of live.current) {
            const { draft } = entry;
            const error = !isPublicExpenseRequestText(draft.name.trim(), 150) || !draft.name.trim() ? 'Введите название статьи ДДС: от 1 до 150 символов, без управляющих знаков.'
                : !['', 'Payment', 'Receipt'].includes(draft.flowDirection) ? 'Выберите корректное направление.'
                    : !CASH_FLOW_ACTIVITY_TYPES.some(option => option.value === draft.activityType) ? 'Выберите вид деятельности.'
                        : draft.enabled && !isPublicExpenseRequestUuid(draft.expenseId) ? 'Выберите статью затрат для начисления.'
                            : draft.costCenterId && !isPublicExpenseRequestUuid(draft.costCenterId) ? 'Выберите корректный ЦФО.'
                                : draft.groupName.trim() && !isPublicExpenseRequestText(draft.groupName.trim(), 150) ? 'Проверьте наименование группы.' : null;
            if (error) {
                invalid = true;
                put(id, { ...entry, error });
            }
        }
        if (invalid) {
            setNotice('Изменения не сохранены. Исправьте отмеченные строки.');
            return;
        }
        busy.current = true;
        setSaving(true);
        setNotice(null);
        let confirmed = 0;
        try {
            const pending = [...live.current.keys()];
            for (const id of pending) {
                const entry = live.current.get(id)!;
                if (!entry.attempt) {
                    const command = commandFor(entry);
                    if (matches(entry.baseline!, command)) {
                        remove(id);
                        continue;
                    }
                    put(id, { ...entry, attempt: { command, key: crypto.randomUUID(), acknowledged: false } });
                }
            }
            for (const id of pending) {
                const entry = live.current.get(id);
                if (!entry)
                    continue;
                const attempt = entry.attempt!;
                put(id, { ...entry, phase: 'saving', error: null });
                try {
                    if (!attempt.acknowledged) {
                        await updateCashFlowAccrualSettings(id, attempt.command, attempt.key, entry.controller.signal);
                        attempt.acknowledged = true;
                    }
                    const current = await getCashFlowAccrualSettings(id, entry.controller.signal);
                    if (entry.controller.signal.aborted)
                        return;
                    if (!matches(current, attempt.command)) {
                        put(id, { ...entry, phase: 'refresh', error: 'Текущее состояние отличается от сохранённых значений. Обновите проверку изменений.', uncertain: false });
                        break;
                    }
                    remove(id);
                    confirmed++;
                }
                catch (failure) {
                    if (entry.controller.signal.aborted)
                        return;
                    const uncertain = entry.uncertain || attempt.acknowledged || isCashFlowAccrualResultUncertain(failure);
                    put(id, { ...entry, phase: uncertain ? 'uncertain' : 'refresh', uncertain,
                        attempt: uncertain ? attempt : null, error: cashFlowAccrualErrorMessage(failure) });
                    break;
                }
            }
            setNotice(live.current.size ? `Подтверждено сохранение: ${confirmed}. Осталось изменений: ${live.current.size}.`
                : 'Все изменения сохранены.');
        }
        catch {
            setNotice('Не удалось подготовить сохранение. Введённые изменения сохранены в редакторе.');
        }
        finally {
            // Never reuse frozen but unsent commands after the user edits a row.
            for (const [id, entry] of live.current)
                if (entry.phase === 'editing' && entry.attempt)
                    put(id, { ...entry, attempt: null });
            busy.current = false;
            setSaving(false);
            if (confirmed)
                onSaved();
        }
    }
    /** Explicit user-triggered re-read/rebase. Desired patches and neighbours survive. */
    async function refresh() {
        if (busy.current)
            return;
        busy.current = true;
        setSaving(true);
        let confirmed = 0;
        try {
            for (const [id, entry] of [...live.current]) {
                if (entry.phase !== 'refresh' && entry.phase !== 'uncertain')
                    continue;
                put(id, { ...entry, phase: 'refreshing', error: null });
                try {
                    const current = await getCashFlowAccrualSettings(id, entry.controller.signal);
                    if (entry.controller.signal.aborted)
                        return;
                    if (entry.attempt && matches(current, entry.attempt.command)) {
                        remove(id);
                        confirmed++;
                        continue;
                    }
                    if (!editable(current)) {
                        put(id, { ...entry, phase: 'refresh', error: 'Редактирование сейчас недоступно. Ваши значения сохранены в редакторе.' });
                        continue;
                    }
                    // Same-version reads cannot disprove an in-flight/ambiguous command.
                    if (entry.uncertain && current.version === entry.baseline?.version) {
                        put(id, { ...entry, phase: 'uncertain', error: 'Результат записи ещё не подтверждён. Повторите сохранение той же команды.' });
                        continue;
                    }
                    put(id, { ...entry, baseline: current, draft: { item: { ...entry.draft.item, name: current.name },
                            ...fields(current), ...entry.patch }, attempt: null, uncertain: false, phase: 'editing',
                        error: `Текущее название: «${current.name}»; группа: «${current.groupName ?? '—'}»; ЦФО: «${current.costCenter?.name ?? 'Не назначено'}»; акрул: ${current.formsManagerialAccrual ? 'Да' : 'Нет'}; статья затрат: «${current.expenseItem?.name ?? 'Не назначена'}». Ваши правки сохранены — проверьте их перед общим сохранением.` });
                }
                catch (failure) {
                    if (!entry.controller.signal.aborted)
                        put(id, { ...entry, phase: entry.uncertain ? 'uncertain' : 'refresh',
                            error: cashFlowAccrualReadErrorMessage(failure) });
                }
            }
            setNotice(live.current.size ? 'Проверка обновлена. Введённые изменения не удалены.' : 'Все изменения подтверждены.');
        }
        finally {
            busy.current = false;
            setSaving(false);
            if (confirmed)
                onSaved();
        }
    }
    const pending = [...entries.values()];
    return { entries, change, save, refresh, discard, saving, notice, count: entries.size,
        canDiscard: !saving && pending.every(entry => !entry.uncertain && !entry.attempt?.acknowledged),
        frozen: saving || pending.some(entry => entry.uncertain),
        hasProblems: pending.some(entry => entry.phase === 'refresh' || entry.phase === 'uncertain'),
        canSave: entries.size > 0 && !saving && pending.every(entry => entry.phase === 'editing' || entry.phase === 'uncertain') };
}
