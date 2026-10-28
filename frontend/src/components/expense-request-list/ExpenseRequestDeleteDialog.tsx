import { useEffect, useId, useRef } from 'react';
export function ExpenseRequestDeleteDialog({ number, busy, error, onConfirm, onCancel }: {
    number: string;
    busy: boolean;
    error: string | null;
    onConfirm: () => void;
    onCancel: () => void;
}) {
    const dialog = useRef<HTMLDialogElement>(null), cancel = useRef<HTMLButtonElement>(null);
    const title = useId(), description = useId();
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
            if (previous instanceof HTMLElement && previous.isConnected)
                previous.focus();
        };
    }, []);
    return <dialog ref={dialog} role="dialog" aria-modal="true" aria-labelledby={title} aria-describedby={description} className="company-version-delete-dialog" onCancel={event => {
            event.preventDefault();
            if (!busy)
                onCancel();
        }} onKeyDown={event => {
            if (event.key === 'Escape') {
                event.preventDefault();
                event.stopPropagation();
                if (!busy)
                    onCancel();
                return;
            }
            if (event.key !== 'Tab')
                return;
            const buttons = [...(dialog.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? [])];
            if (!buttons.length) {
                event.preventDefault();
                return;
            }
            const first = buttons[0], last = buttons.at(-1);
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last?.focus();
            }
            else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        }}>
    <h2 id={title}>Удалить черновик?</h2>
    <div id={description}><p>Расходная заявка № {number} исчезнет из рабочих списков.</p>
      <p>Запись, вложения и история сохранятся. Удалить можно только черновик.</p></div>
    {error && <p role="alert" className="notice error">{error}</p>}
    <div className="company-version-actions">
      <button ref={cancel} type="button" disabled={busy} onClick={onCancel}>Отмена</button>
      <button type="button" disabled={busy} onClick={onConfirm}>{busy ? 'Удаление…' : 'Удалить черновик'}</button>
    </div>
  </dialog>;
}
