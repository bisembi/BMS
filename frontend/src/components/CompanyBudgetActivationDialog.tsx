import { useEffect, useId, useRef } from 'react';
/** An in-page confirmation, following BudgetMatrixPasteDialog's native-dialog/fallback pattern. */
export function CompanyBudgetActivationDialog({ name, onConfirm, onCancel }: {
    name: string;
    onConfirm: () => void;
    onCancel: () => void;
}) {
    const dialog = useRef<HTMLDialogElement>(null);
    const cancel = useRef<HTMLButtonElement>(null);
    const title = useId();
    const description = useId();
    useEffect(() => {
        const previous = document.activeElement;
        const element = dialog.current;
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
    return <dialog ref={dialog} role="dialog" aria-modal="true" aria-labelledby={title} aria-describedby={description} className="company-version-activation-dialog" onCancel={event => { event.preventDefault(); onCancel(); }} onKeyDown={event => {
            if (event.key === 'Escape') {
                event.preventDefault();
                event.stopPropagation();
                onCancel();
                return;
            }
            if (event.key !== 'Tab')
                return;
            const buttons = [...(dialog.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? [])];
            const first = buttons[0];
            const last = buttons.at(-1);
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last?.focus();
            }
            else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first?.focus();
            }
        }}>
    <h2 id={title}>Назначение действующей версии</h2>
    <p id={description}>Назначить версию «{name}» действующей? Она станет источником бюджетных лимитов.</p>
    <div className="company-version-actions"><button ref={cancel} type="button" onClick={onCancel}>Отмена назначения</button>
      <button type="button" onClick={onConfirm}>Подтвердить назначение</button></div>
  </dialog>;
}
