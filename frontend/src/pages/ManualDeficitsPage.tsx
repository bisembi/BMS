import { useWorkspaceRefreshRevision, useWorkspaceRefreshGuard, useWorkspaceRefreshBusy } from '../components/WorkspaceRefreshContext';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { getOrganizations } from '../api/referenceData';
import type { ReferenceDataItem } from '../types';
import { useManualDeficitCommand, useManualDeficitDetail, useManualDeficitList } from '../hooks/useManualDeficits';
import { ManualDeficitList } from '../components/manual-deficits/ManualDeficitList';
import { ManualDeficitDetail } from '../components/manual-deficits/ManualDeficitDetail';
import { ManualDeficitPagination } from '../components/manual-deficits/ManualDeficitPagination';
import '../styles/manual-deficits.css';
export function ManualDeficitsPage() {
    const workspaceRefresh = useWorkspaceRefreshRevision();
    const [organizationsLoading, setOrganizationsLoading] = useState(true);
    const [organizations, setOrganizations] = useState<ReferenceDataItem[]>([]);
    const [organizationLoadFailed, setOrganizationLoadFailed] = useState(false);
    const [organizationIndex, setOrganizationIndex] = useState('');
    const [includeSettled, setIncludeSettled] = useState(false);
    const [onlyPending, setOnlyPending] = useState(false);
    const [skip, setSkip] = useState(0);
    const [selected, setSelected] = useState<string | null>(null);
    const [detailSkip, setDetailSkip] = useState(0);
    const [revision, setRevision] = useState(0);
    const selectedOrganization = useRef<string | null>(null);
    selectedOrganization.current = organizationIndex === '' ? null : organizations[Number(organizationIndex)]?.id ?? null;
    const trigger = useRef<HTMLButtonElement | null>(null);
    const refresh = useRef<HTMLButtonElement | null>(null);
    const detailHeading = useRef<HTMLHeadingElement | null>(null);
    useWorkspaceRefreshBusy(organizationsLoading);
    useEffect(() => {
        setOrganizationsLoading(true);
        let active = true;
        void getOrganizations().then((catalog) => {
            if (active) {
                const chosen = selectedOrganization.current;
                setOrganizations(catalog.items);
                setOrganizationLoadFailed(false);
                if (chosen) {
                    const next = catalog.items.findIndex(item => item.id === chosen);
                    setOrganizationIndex(next < 0 ? '' : String(next));
                }
            }
        }).catch(() => {
            if (active)
                setOrganizationLoadFailed(true);
        }).finally(() => {
            if (active)
                setOrganizationsLoading(false);
        });
        return () => { active = false; };
    }, [workspaceRefresh]);
    const organizationId = organizationIndex === '' ? undefined : organizations[Number(organizationIndex)]?.id;
    const query = useMemo(() => ({ organizationId, includeSettled, onlyPending, skip, take: 25 }), [organizationId, includeSettled, onlyPending, skip]);
    const list = useManualDeficitList(query, revision);
    const detail = useManualDeficitDetail(selected, detailSkip, revision);
    const completed = useCallback(() => {
        // The submitted control is removed while the detail reloads. Keep the
        // keyboard position on the persistent heading instead of falling to body.
        detailHeading.current?.focus();
        setDetailSkip(0);
        setRevision((value) => value + 1);
    }, []);
    const command = useManualDeficitCommand(completed);
    const locked = command.busy || command.pending !== null;
    useWorkspaceRefreshGuard(locked ? 'Сначала подтвердите результат корректировки дефицита.' : null);
    useEffect(() => {
        if (selected)
            detailHeading.current?.focus();
    }, [selected]);
    const clearSelection = () => {
        if (locked)
            return;
        setSelected(null);
        setDetailSkip(0);
        command.clearNotice();
    };
    const close = () => {
        clearSelection();
        if (!locked)
            (trigger.current?.isConnected ? trigger.current : refresh.current)?.focus();
    };
    const filterChanged = () => { setSkip(0); clearSelection(); };
    return <div className="manual-deficits-page">
    <h1>Корректировки дефицита переплаты</h1>
    <p className="manual-deficit-lead">Предложение бухгалтера не изменяет остаток. Корректировка применяется только после подтверждения финансовым директором.</p>
    <fieldset className="manual-deficit-filters" disabled={locked}>
      <legend>Область просмотра</legend>
      <label>Организация<select value={organizationIndex} disabled={organizationLoadFailed || organizations.length === 0} onChange={(event) => { setOrganizationIndex(event.target.value); filterChanged(); }}>
        <option value="">Все доступные организации</option>
        {organizations.map((item, index) => <option key={item.id} value={String(index)}>{item.name}</option>)}
      </select></label>
      <label className="manual-deficit-check"><input type="checkbox" checked={includeSettled} onChange={(event) => { setIncludeSettled(event.target.checked); filterChanged(); }}/>Включая погашенные</label>
      <label className="manual-deficit-check"><input type="checkbox" checked={onlyPending} onChange={(event) => { setOnlyPending(event.target.checked); filterChanged(); }}/>Только ожидающие решения</label>
      <button type="button" ref={refresh} onClick={() => setRevision((value) => value + 1)}>Обновить</button>
    </fieldset>
    {organizationLoadFailed && <p role="status">Не удалось обновить список организаций; текущая область просмотра сохранена.</p>}
    {command.notice && <p role={command.notice.error ? 'alert' : 'status'}>{command.notice.text}</p>}
    {command.pending && <button type="button" disabled={command.busy} onClick={() => {
                if (command.pending)
                    void command.run(command.pending);
            }}>Проверить результат команды</button>}
    {command.busy && <p role="status">Проверка и выполнение команды…</p>}
    {list.kind === 'loading' && <p role="status">Загрузка дефицитов…</p>}
    {list.kind === 'error' && <p role="alert">{list.text}</p>}
    {list.kind === 'ready' && <>
      <ManualDeficitList page={list.data} disabled={locked} selected={selected} onOpen={(id, button) => {
                command.clearNotice();
                trigger.current = button;
                setDetailSkip(0);
                setSelected(id);
            }}/>
      <ManualDeficitPagination label="Страницы дефицитов" skip={list.data.skip} count={list.data.items.length} total={list.data.total} disabled={locked} onChange={(next) => { setSkip(next); clearSelection(); }}/>
    </>}
    {selected && <section className="manual-deficit-detail" aria-labelledby="manual-deficit-detail-title" aria-busy={detail.kind === 'loading' || command.busy} onKeyDown={(event) => {
                if (event.key === 'Escape' && !locked) {
                    event.preventDefault();
                    close();
                }
            }}>
      <div className="manual-deficit-detail-heading"><h3 id="manual-deficit-detail-title" ref={detailHeading} tabIndex={-1}>Дефицит и корректировки</h3>
        <button type="button" disabled={locked} onClick={close}>Закрыть детализацию</button></div>
      {detail.kind === 'loading' && <p role="status">Загрузка корректировок…</p>}
      {detail.kind === 'error' && <><p role="alert">{detail.text}</p><button type="button" disabled={locked} onClick={() => setRevision((value) => value + 1)}>Повторить загрузку детализации</button></>}
      {detail.kind === 'ready' && <ManualDeficitDetail data={detail.data} locked={locked} onExecute={(attempt) => { void command.run(attempt); }} onPage={setDetailSkip}/>}
    </section>}
  </div>;
}
