import { useEffect, useId, useRef, useState } from 'react';
import { listCompanyBudgetVersions, type CompanyBudgetDeleteInput, type CompanyBudgetVersionItem } from '../api/companyBudgetVersions';
/** The same native-dialog/fallback and focus boundaries as the activation confirmation. */
export function CompanyBudgetDeleteDialog({ name, instanceCount, activeVersion, onConfirm, onCancel }: {
    name: string;
    instanceCount: number;
    activeVersion?: {
        id: string;
        concurrencyStamp: string;
        activeConcurrencyStamp: string;
    };
    onConfirm: (replacement?: CompanyBudgetDeleteInput) => void;
    onCancel: () => void;
}) {
    const dialog = useRef<HTMLDialogElement>(null);
    const cancel = useRef<HTMLButtonElement>(null);
    const title = useId(), description = useId();
    const [candidates, setCandidates] = useState<CompanyBudgetVersionItem[] | null>(null);
    const [selected, setSelected] = useState('');
    const [error, setError] = useState<string | null>(null);
    const activeId = activeVersion?.id, activeStamp = activeVersion?.activeConcurrencyStamp;
    useEffect(() => {
        if (!activeId)
            return;
        const controller = new AbortController();
        setCandidates(null);
        setSelected('');
        setError(null);
        void (async () => {
            const rows: CompanyBudgetVersionItem[] = [];
            let total: number | undefined;
            for (let skip = 0;; skip += 100) {
                const page = await listCompanyBudgetVersions(skip, 100, controller.signal);
                if (page.scopeLimited || !page.canCreate || page.activeVersionId !== activeId
                    || page.activeConcurrencyStamp !== activeStamp || page.total > 10000
                    || (total !== undefined && total !== page.total))
                    throw new Error('Changed version state');
                total = page.total;
                rows.push(...page.items);
                if (skip + 100 >= total)
                    break;
            }
            if (new Set(rows.map(row => row.id.toLowerCase())).size !== rows.length || rows.length !== total)
                throw new Error('Changed list');
            if (!controller.signal.aborted)
                setCandidates(rows.filter(row => row.id !== activeId && row.isPublished
                    && !row.isActive && !row.isPartial && row.canActivate && row.instanceCount > 0));
        })().catch(() => {
            if (!controller.signal.aborted)
                setError('Не удалось подтвердить список замены. Закройте окно и перечитайте версии.');
        });
        return () => controller.abort();
    }, [activeId, activeStamp]);
    const replacement = candidates?.find(row => row.id === selected);
    useEffect(() => {
        const previous = document.activeElement, element = dialog.current;
        try {
            element?.showModal();
        }
        catch {
            element?.setAttribute('open', '');
        }
        if (element && !element.open)
            element.setAttribute('open', '');
        cancel.current?.focus();
        return () => {
            if (element?.open) {
                try {
                    element.close();
                }
                catch {
                    element.removeAttribute('open');
                }
            }
            if (element?.open)
                element.removeAttribute('open');
            if (previous instanceof HTMLElement && previous.isConnected)
                previous.focus();
        };
    }, []);
    return <dialog ref={dialog} role="dialog" aria-modal="true" aria-labelledby={title} aria-describedby={description} className="company-version-delete-dialog" onCancel={event => { event.preventDefault(); onCancel(); }} onKeyDown={event => {
            if (event.key === 'Escape') {
                event.preventDefault();
                event.stopPropagation();
                onCancel();
                return;
            }
            if (event.key !== 'Tab')
                return;
            const buttons = [...(dialog.current?.querySelectorAll<HTMLElement>('button:not(:disabled), select:not(:disabled)') ?? [])];
            const first = buttons[0], last = buttons.at(-1);
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last?.focus();
            }
            else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first?.focus();
            }
        }}>
    <h2 id={title}>Удаление версии бюджета</h2>
    <div id={description}>
      <p>Удалить версию «{name}»?</p>
      <p>Версия будет убрана из рабочих списков. Экземпляры бюджетов ({instanceCount}),
        их строки и суммы сохранятся. Они останутся доступны в справочнике бюджетов и других версиях.</p>
      <p>История согласований и технические связи сохраняются.</p>
      {activeVersion && <>
        <p>Действующую версию можно удалить только с заменой. БДДС и лимиты новых проверок будут рассчитываться
          по выбранной версии. Существующие заявки, резервы и согласования не удаляются.</p>
        {error ? <p role="alert">{error}</p> : candidates === null ? <p role="status">Загрузка утверждённых версий…</p>
                : candidates.length === 0 ? <p role="alert">Нет другой утверждённой версии для замены. Удаление невозможно.</p>
                    : <label>Утверждённая версия взамен<select aria-label="Утверждённая версия взамен" value={selected} onChange={event => setSelected(event.target.value)}>
            <option value="">Выберите версию</option>{candidates.map(row => <option key={row.id} value={row.id}>
              {row.name} — экземпляров: {row.instanceCount} ({row.id.slice(0, 8)})</option>)}
          </select></label>}
      </>}
    </div>
    <div className="company-version-actions"><button ref={cancel} type="button" onClick={onCancel}>Отмена удаления</button>
      <button type="button" disabled={activeVersion !== undefined && (!replacement || error !== null)} onClick={() => {
            if (!activeVersion)
                onConfirm();
            else if (replacement && !error)
                onConfirm({ expectedConcurrencyStamp: activeVersion.concurrencyStamp,
                    replacementVersionId: replacement.id, expectedReplacementConcurrencyStamp: replacement.concurrencyStamp,
                    expectedActiveConcurrencyStamp: activeVersion.activeConcurrencyStamp });
        }}>Подтвердить удаление</button></div>
  </dialog>;
}
