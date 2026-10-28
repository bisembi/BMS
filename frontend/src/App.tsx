import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { getCurrentSession } from './api/session';
import { rememberBddsDrill } from './api/bddsDrill';
import { useSessionPresence } from './hooks/useSessionPresence';
import { useMyTasksSummary355 } from './hooks/useMyTasksSummary355';
import { useReturnedNoticeSummary377, useAdjustmentNoticeSummary448, useNoticeSummary448 } from './hooks/useReturnedNoticeSummary377';
import { staffPendingSummary471 } from './api/staffSubstitution471';
import { StaffNews471, StaffPending471 } from './components/StaffNews471';
import { useBrowserWorkspaceHistory } from './hooks/useBrowserWorkspaceHistory';
import { EnterpriseShell } from './components/EnterpriseShell';
import { WorkspaceCommandLockContext } from './components/WorkspaceCommandLockContext';
import { WorkspaceRefreshProvider, useWorkspaceRefreshController } from './components/WorkspaceRefreshContext';
import { WorkspacePageBoundary } from './components/WorkspacePageBoundary';
import { AttachmentPreviewWorkspace } from './components/expense-request-form/AttachmentPreviewWorkspace';
import { keyForView, isViewAvailable, isBookmarkableView, navigationSearchItems, SECTIONS, type View } from './navigation';
import { PlaceholderPage } from './pages/PlaceholderPage';
import { SectionPage } from './pages/SectionPage';
import { SessionGatePage } from './pages/SessionGatePage';
import { CurrencyRatesPage } from './pages/CurrencyRatesPage';
import { OpiuReportPage, StaffSubstitutionPage471 } from './pages/lazyPages';
import { ActiveAccrualReportPage, AccrualNotificationsPage, ApprovalMatrixPage, AuthorizationDirectoryPage, BudgetExecutionPage, BddsReportPage, BddsDrillPage, UnbudgetedArticlesPage, BudgetInstancesPage, BudgetAdjustmentsPage, CompanyBudgetVersionsPage, BudgetDirectoryPage, BusinessAuditMonitorPage, ChangeJournalPage, BudgetExecutionRegisterPage343, ExchangeJournalPage, SessionMonitorPage, HelpPage, MyTasksPage355, ExpenseRequestFormPage, ExpenseRequestListPage, ExpensePaymentRegisterPage, FinanceWorkspacePage, IntegrationOutboxMonitorPage, ManualDeficitsPage, PaymentCalendarPage, DatedPaymentCalendarPage, ReferenceDirectoryPage } from './pages/lazyPages';
import type { CurrentSession } from './types';
import type { SwitchableBmsAccessProfileCode } from './types';
import { useMockApi } from './api/runtime';
import { beginBmsRoleSwitch, hasPendingApiWrites, setActiveBmsProfileOverride, setActiveBmsRoleOverride } from './api/http';
import { profileForRole, roleLabels, roleSelectionValue471, type RoleSelection } from './utils/activeRole';
import type { FinanceWorkspaceId } from './financeWorkspaces';
import { emptyFinancePreparation, type FinancePreparation } from './utils/financeWorkspaceDraft';
import { taskMailRequestId, taskBudgetInstanceId, taskBudgetAdjustmentId448, taskStaffSubstitution471 } from './utils/taskMailLink';
type SessionStatus = 'checking' | 'ready' | 'failed';
const initialView: View = { kind: 'section', sectionId: 'home' };
function isNewExpenseRequestDraft(view: View): boolean {
    return view.kind === 'expense-form' && view.requestId === null;
}
function WorkspaceViewCache({ active, children }: {
    active: boolean;
    children: ReactNode;
}) {
    const mountRef = useRef<HTMLDivElement | null>(null);
    const [host] = useState(() => document.createElement('div'));
    useLayoutEffect(() => {
        const mount = mountRef.current;
        if (!active || mount === null) {
            host.remove();
            return;
        }
        if (host.parentElement !== mount)
            mount.appendChild(host);
        return () => host.remove();
    }, [active, host]);
    return (<div ref={mountRef} className="workspace-view-cache" hidden={!active} aria-hidden={!active}>
      {createPortal(children, host)}
    </div>);
}
export function App() {
    const [view, setView] = useState<View>(initialView);
    const [listOpenToken, setListOpenToken] = useState(0);
    const [paymentRegisterOpenToken, setPaymentRegisterOpenToken] = useState(0);
    const [listReloadToken, setListReloadToken] = useState(0);
    const [budgetListReloadToken, setBudgetListReloadToken] = useState(0);
    const [tasksOpenToken, setTasksOpenToken] = useState(0);
    const [taskSummaryRefreshToken, setTaskSummaryRefreshToken] = useState(0);
    const refreshTaskSummary = useCallback(() => setTaskSummaryRefreshToken(value => value + 1), []);
    const nextBudgetWorkspace = useRef(0);
    const [copyNoticeRequestId, setCopyNoticeRequestId] = useState<string | null>(null);
    const [currentSession, setCurrentSession] = useState<CurrentSession | null>(null);
    const [sessionStatus, setSessionStatus] = useState<SessionStatus>('checking');
    const [sessionAttempt, setSessionAttempt] = useState(0);
    const sessionReadGeneration = useRef(0);
    const [signedOut, setSignedOut] = useState(false);
    const commandLocks = useRef(new Map<string, Map<symbol, string>>());
    const sessionScope = sessionStatus === 'ready' && currentSession
        ? JSON.stringify([sessionAttempt, currentSession.actorExternalId, currentSession.activeProfileCode, currentSession.activeRoleCode ?? null, currentSession.activeSubstitutionId471 ?? null]) : null;
    const sessionScopeRef = useRef(sessionScope);
    sessionScopeRef.current = sessionScope;
    const setCommandLock = useCallback((key: string, scope: string, owner: symbol, locked: boolean) => {
        if (locked && sessionScopeRef.current !== scope)
            return false;
        const owners = commandLocks.current.get(key) ?? new Map<symbol, string>();
        if (locked)
            owners.set(owner, scope);
        else if (owners.get(owner) === scope)
            owners.delete(owner);
        if (owners.size)
            commandLocks.current.set(key, owners);
        else
            commandLocks.current.delete(key);
        return true;
    }, []);
    const hasCommandLock = useCallback((key?: string) => {
        const maps = key === undefined ? [...commandLocks.current.values()] : [commandLocks.current.get(key)];
        return maps.some(owners => owners && [...owners.values()].some(scope => scope === sessionScopeRef.current));
    }, []);
    const directoryNavigationLock = useRef(false);
    const navigationLocks = useRef(new Map<string, boolean>());
    // Unlike a dirty draft, an in-flight or unconfirmed settings command must
    // not be discarded by closing its tab, logging out or switching profiles.
    const assignmentMutationLocks = useRef(new Set<string>());
    const activeViewKeyRef = useRef(keyForView(initialView));
    const [navigationLockRevision, setNavigationLockRevision] = useState(0);
    const [navigationNotice, setNavigationNotice] = useState(false);
    const [profileSwitchNotice, setProfileSwitchNotice] = useState<string | null>(null);
    const [roleSwitchBusy, setRoleSwitchBusy] = useState(false);
    const closePresence = useSessionPresence(sessionStatus === 'ready' && !roleSwitchBusy, sessionScope);
    const roleSwitchLock = useRef(false);
    const finishRoleSwitch = useRef<(() => void) | null>(null);
    useLayoutEffect(() => {
        if (!roleSwitchBusy && finishRoleSwitch.current) {
            finishRoleSwitch.current();
            finishRoleSwitch.current = null;
            roleSwitchLock.current = false;
        }
    }, [roleSwitchBusy, currentSession, sessionStatus]);
    useEffect(() => () => { finishRoleSwitch.current?.(); finishRoleSwitch.current = null; }, []);
    const navigationNoticeRef = useRef<HTMLParagraphElement>(null);
    const activeProfileOverride = useRef<SwitchableBmsAccessProfileCode | null>(null);
    const profileOverrideInitialized = useRef(false);
    const [openViews, setOpenViews] = useState<View[]>([]);
    const [expenseTabLabels, setExpenseTabLabels] = useState<Record<string, string>>({});
    // Non-tab list/report workspaces also keep their filters and local state
    // while browser history temporarily switches to another screen.
    const [retainedViews, setRetainedViews] = useState<View[]>([]);
    const setViewNavigationLock = useCallback((viewKey: string, locked: boolean) => {
        navigationLocks.current.set(viewKey, locked);
        setNavigationLockRevision((revision) => revision + 1);
        if (viewKey !== activeViewKeyRef.current)
            return;
        directoryNavigationLock.current = locked;
        if (!locked)
            setNavigationNotice(false);
    }, []);
    const clearNavigationLocks = useCallback(() => {
        setExpenseTabLabels({});
        commandLocks.current.clear();
        navigationLocks.current.clear();
        assignmentMutationLocks.current.clear();
        setNavigationLockRevision((revision) => revision + 1);
        directoryNavigationLock.current = false;
    }, []);
    const anyNavigationLocked = useMemo(() => Array.from(navigationLocks.current.values()).some(Boolean), [navigationLockRevision]);
    useEffect(() => {
        if (!anyNavigationLocked)
            return;
        const beforeUnload = (event: BeforeUnloadEvent) => {
            event.preventDefault();
            event.returnValue = '';
        };
        window.addEventListener('beforeunload', beforeUnload);
        return () => window.removeEventListener('beforeunload', beforeUnload);
    }, [anyNavigationLocked]);
    useEffect(() => {
        const guard = (event: BeforeUnloadEvent) => {
            if (hasCommandLock()) {
                event.preventDefault();
                event.returnValue = '';
            }
        };
        window.addEventListener('beforeunload', guard);
        return () => window.removeEventListener('beforeunload', guard);
    }, [hasCommandLock]);
    useEffect(() => {
        if (navigationNotice)
            navigationNoticeRef.current?.focus();
    }, [navigationNotice]);
    const [financeDrafts, setFinanceDrafts] = useState<Partial<Record<FinanceWorkspaceId, FinancePreparation>>>({});
    useEffect(() => {
        let active = true;
        const generation = ++sessionReadGeneration.current;
        if (!profileOverrideInitialized.current) {
            // A reload starts from the server's safe default. The choice lives only
            // for this page session and is revalidated by the API on every request.
            setActiveBmsProfileOverride(null);
            setActiveBmsRoleOverride(null);
            activeProfileOverride.current = null;
            profileOverrideInitialized.current = true;
        }
        // Local preparation documents belong to the confirmed session, never to
        // another actor after reauthentication. They are not browser persistence.
        setFinanceDrafts({});
        setSignedOut(false);
        setSessionStatus('checking');
        setCurrentSession(null);
        void getCurrentSession()
            .then((session) => {
            if (!active || generation !== sessionReadGeneration.current)
                return;
            if (session.availableRoleCodes !== undefined) {
                setActiveBmsRoleOverride(session.activeRoleCode ?? null, session.activeSubstitutionId471);
                if (session.activeProfileCode !== 'INTEGRATION_ADMIN') {
                    setActiveBmsProfileOverride(session.activeProfileCode);
                    activeProfileOverride.current = session.activeProfileCode;
                }
            }
            setCurrentSession(session);
            setSessionStatus('ready');
        })
            .catch(() => {
            // Fail closed: no guest and no administrator fallback is invented.
            if (!active || generation !== sessionReadGeneration.current)
                return;
            setCurrentSession(null);
            setSessionStatus('failed');
        });
        return () => {
            active = false;
        };
    }, [sessionAttempt]);
    const retrySession = useCallback(() => {
        // A failed session check may follow role revocation or a different Windows
        // principal. Retry from the server's safe default, not a stale profile
        // selector left in this page module.
        setActiveBmsProfileOverride(null);
        setActiveBmsRoleOverride(null);
        activeProfileOverride.current = null;
        setSignedOut(false);
        clearNavigationLocks();
        setOpenViews([]);
        setRetainedViews([]);
        setSessionAttempt((attempt) => attempt + 1);
    }, [clearNavigationLocks]);
    const logout = useCallback(() => {
        if (hasCommandLock()) {
            setProfileSwitchNotice('Сначала подтвердите результат отправленной команды в её вкладке.');
            return;
        }
        if (assignmentMutationLocks.current.size > 0) {
            setProfileSwitchNotice('Сначала подтвердите результат сохранения статьи ДДС.');
            return;
        }
        // Windows/AD owns the authentication ticket; the browser cannot revoke
        // that ticket. This action closes the BMS workspace and drops local
        // profile/session state. "Войти снова" performs a fresh session check.
        sessionReadGeneration.current++;
        closePresence();
        clearNavigationLocks();
        setActiveBmsProfileOverride(null);
        setActiveBmsRoleOverride(null);
        activeProfileOverride.current = null;
        setFinanceDrafts({});
        setOpenViews([]);
        setRetainedViews([]);
        setView({ kind: 'section', sectionId: 'home' });
        setCurrentSession(null);
        setNavigationNotice(false);
        setProfileSwitchNotice(null);
        setSignedOut(true);
        setSessionStatus('failed');
    }, [clearNavigationLocks, closePresence]);
    const registerOpenView = useCallback((nextView: View) => {
        if (!isBookmarkableView(nextView)) {
            if (nextView.kind !== 'section' && nextView.kind !== 'placeholder')
                setRetainedViews(current => current.some(candidate => keyForView(candidate) === keyForView(nextView)) ? current : [...current, nextView]);
            return;
        }
        const key = keyForView(nextView);
        setOpenViews((current) => current.some((candidate) => keyForView(candidate) === key)
            ? current
            : [...current, nextView]);
    }, []);
    const initialMailRequest = useRef(taskMailRequestId(window.location.search));
    const initialBudgetLink = useRef(taskBudgetInstanceId(window.location.search));
    const initialAdjustmentLink = useRef(taskBudgetAdjustmentId448(window.location.search));
    const initialStaffLink471 = useRef(taskStaffSubstitution471(window.location.search));
    const mailLinkHandled = useRef(false);
    useEffect(() => {
        if (mailLinkHandled.current || (!initialMailRequest.current && !initialBudgetLink.current && !initialAdjustmentLink.current && !initialStaffLink471.current) || sessionStatus !== 'ready' || !currentSession || roleSwitchBusy)
            return;
        mailLinkHandled.current = true;
        if (!currentSession.canAccessBusinessData || currentSession.isBmsIntegrationAdministrator) {
            setProfileSwitchNotice('Для открытия заявки из письма войдите под своей корпоративной учётной записью с рабочей ролью BMS.');
            return;
        }
        const next: View = initialStaffLink471.current ? { kind: 'staff-substitutions', documentId: initialStaffLink471.current } : initialMailRequest.current ? { kind: 'expense-form', requestId: initialMailRequest.current }
            : initialAdjustmentLink.current ? { kind: 'budget-adjustments', adjustmentId: initialAdjustmentLink.current }
                : { kind: 'budget-document', workspaceKey: `saved:${initialBudgetLink.current!}`, instanceId: initialBudgetLink.current! };
        registerOpenView(next);
        setView(next);
    }, [sessionStatus, currentSession, roleSwitchBusy, registerOpenView]);
    const switchAccessProfile = useCallback(async (profile: SwitchableBmsAccessProfileCode) => {
        const previousSession = currentSession;
        if (sessionStatus !== 'ready' || previousSession === null
            || !previousSession.availableProfileCodes.includes(profile)
            || previousSession.activeProfileCode === profile) {
            return;
        }
        if (hasCommandLock()) {
            setProfileSwitchNotice('Сначала подтвердите результат отправленной команды в её вкладке.');
            return;
        }
        if (assignmentMutationLocks.current.size > 0) {
            setProfileSwitchNotice('Сначала подтвердите результат сохранения статьи ДДС.');
            return;
        }
        if (directoryNavigationLock.current) {
            setNavigationNotice(true);
            navigationNoticeRef.current?.focus();
            return;
        }
        if (hasPendingApiWrites()) {
            setProfileSwitchNotice('Дождитесь завершения отправки изменений, затем переключите профиль.');
            return;
        }
        const profileLabel = profile === 'SYSTEM_ADMIN'
            ? 'Системный администратор'
            : 'Бизнес-процессы';
        if (!window.confirm(`Переключиться в профиль «${profileLabel}»? Открытая рабочая область будет закрыта. `
            + 'Учетная запись, назначенные роли и авторство действий не меняются.')) {
            return;
        }
        const priorOverride = activeProfileOverride.current;
        setProfileSwitchNotice(null);
        setSessionStatus('checking');
        setCurrentSession(null);
        setActiveBmsProfileOverride(profile);
        activeProfileOverride.current = profile;
        try {
            const switchedSession = await getCurrentSession();
            if (switchedSession.activeProfileCode !== profile) {
                throw new Error('The server did not confirm the requested BMS profile.');
            }
            setFinanceDrafts({});
            clearNavigationLocks();
            setOpenViews([]);
            setRetainedViews([]);
            setView({ kind: 'section', sectionId: 'home' });
            setCurrentSession(switchedSession);
            setSessionStatus('ready');
        }
        catch {
            // Do not leave the user trapped behind a rejected selector (for example,
            // if their SQL role assignment was revoked during the switch). Restore
            // the last selection and ask the server for the current effective session.
            setActiveBmsProfileOverride(priorOverride);
            activeProfileOverride.current = priorOverride;
            try {
                const restoredSession = await getCurrentSession();
                setCurrentSession(restoredSession);
                setSessionStatus('ready');
                setProfileSwitchNotice('Профиль не переключен: сервер не подтвердил это назначение.');
            }
            catch {
                setCurrentSession(null);
                setSessionStatus('failed');
            }
        }
    }, [clearNavigationLocks, currentSession, sessionStatus]);
    const canCreateExpenseRequest = currentSession?.canCreateExpenseRequest === true;
    const canAccessBusinessData = currentSession?.canAccessBusinessData === true;
    const canViewIntegrationMonitor = currentSession?.isBmsAdministrator === true
        || currentSession?.isBmsIntegrationAdministrator === true;
    const canViewBusinessAuditMonitor = currentSession?.canViewBusinessAuditMonitor === true;
    const canViewAdministration = currentSession?.isBmsAdministrator === true
        && currentSession?.isBmsIntegrationAdministrator !== true;
    const canViewAdministrationReports = canAccessBusinessData
        && currentSession?.activeRoleCode === 'FIN_DIRECTOR'
        && currentSession?.activeProfileCode === 'BUSINESS'
        && currentSession?.hasFullBusinessDataAccess === true
        && currentSession?.isBmsIntegrationAdministrator !== true;
    const canViewExchangeJournal = (currentSession?.availableRoleCodes?.includes('SYSTEM_ADMIN') === true
        || currentSession?.activeRoleCode === 'AUDITOR')
        && (currentSession?.canReadAllBusinessData ?? currentSession?.hasFullBusinessDataAccess) === true
        && currentSession?.isBmsIntegrationAdministrator !== true;
    const canViewBudgetExecution = (currentSession?.canReadBudgetReports ?? currentSession?.hasFullBusinessDataAccess) === true
        && currentSession?.isBmsIntegrationAdministrator !== true;
    const navigationAccess = useMemo(() => ({
        canUseStaffSubstitutions471: currentSession?.staffSubstitutionRoles471 !== undefined,
        canViewMyTasks: canAccessBusinessData && currentSession?.isBmsAdministrator !== true && currentSession?.isBmsIntegrationAdministrator !== true,
        canCreateExpenseRequest,
        canAccessBusinessData,
        canViewIntegrationMonitor,
        canViewBusinessAuditMonitor,
        canViewBudgetExecution,
        canViewAdministration,
        canViewAdministrationReports,
        canViewExchangeJournal,
    }), [
        currentSession,
        canAccessBusinessData,
        canCreateExpenseRequest,
        canViewBudgetExecution,
        canViewBusinessAuditMonitor,
        canViewIntegrationMonitor,
        canViewAdministration,
        canViewAdministrationReports,
        canViewExchangeJournal,
    ]);
    const shellNavigationItems = useMemo(() => {
        if (sessionStatus !== 'ready' || currentSession === null)
            return [];
        return navigationSearchItems(navigationAccess);
    }, [
        currentSession,
        navigationAccess,
        sessionStatus,
    ]);
    useEffect(() => {
        if (currentSession === null || isViewAvailable(view, navigationAccess))
            return;
        // A section click can arrive after the permitted landing commits but
        // before this effect runs. Normalize the queued current view, not the
        // obsolete view captured by the session-ready render.
        setView((latestView) => {
            if (isViewAvailable(latestView, navigationAccess))
                return latestView;
            if (currentSession.isBmsIntegrationAdministrator === true
                && canViewIntegrationMonitor) {
                return { kind: 'integration-monitor' };
            }
            else if (!canCreateExpenseRequest && isNewExpenseRequestDraft(latestView)
                && canAccessBusinessData) {
                return { kind: 'expense-list' };
            }
            else {
                return { kind: 'section', sectionId: 'home' };
            }
        });
    }, [
        canAccessBusinessData,
        canCreateExpenseRequest,
        canViewIntegrationMonitor,
        currentSession,
        navigationAccess,
        view,
    ]);
    // The permitted view is derived during render, so a page the session does not
    // allow is never mounted even for a single commit before the effect above
    // rewrites the stored view.
    const effectiveView: View = (() => {
        if (sessionStatus !== 'ready' || currentSession === null)
            return view;
        if (isViewAvailable(view, navigationAccess))
            return view;
        if (currentSession.isBmsIntegrationAdministrator === true
            && canViewIntegrationMonitor) {
            return { kind: 'integration-monitor' };
        }
        if (!canCreateExpenseRequest && isNewExpenseRequestDraft(view)
            && canAccessBusinessData) {
            return { kind: 'expense-list' };
        }
        return { kind: 'section', sectionId: 'home' };
    })();
    // The first confirmed view is an opened tab as well. While the session is
    // pending or failed, protected tabs stay hidden from the shell.
    const effectiveViewKey = keyForView(effectiveView);
    const mainArea = useRef<HTMLElement | null>(null);
    const browserNavigation = useBrowserWorkspaceHistory({
        view: effectiveView, views: [...openViews, ...retainedViews], main: mainArea,
        binding: sessionStatus === 'ready' && currentSession
            ? JSON.stringify([currentSession.actorExternalId, currentSession.activeProfileCode, currentSession.activeRoleCode ?? null, currentSession.activeSubstitutionId471 ?? null]) : null,
        generation: sessionScope, enabled: sessionStatus === 'ready' && currentSession !== null && !roleSwitchBusy, signedOut,
        canRestore: candidate => sessionStatus === 'ready' && currentSession !== null && isViewAvailable(candidate, navigationAccess),
        blockedReason: () => roleSwitchLock.current ? 'Дождитесь завершения переключения роли.'
            : assignmentMutationLocks.current.size > 0 ? 'Сначала подтвердите результат сохранения статьи ДДС.' : null,
        onNotice: setProfileSwitchNotice,
        onRestore: candidate => {
            setNavigationNotice(false);
            setCopyNoticeRequestId(null);
            setProfileSwitchNotice(null);
            // Do not use openView: its list-refresh and new-document shortcuts are
            // user commands, never effects of traversing browser history.
            registerOpenView(candidate);
            setView(candidate);
        },
    });
    const pageRefresh = useWorkspaceRefreshController(effectiveViewKey, sessionScope, () => hasCommandLock(effectiveViewKey) ? 'Сначала подтвердите результат отправленной команды в текущей вкладке.'
        : hasPendingApiWrites() ? 'Дождитесь завершения отправки изменений.' : null, () => mainArea.current);
    const taskSummary = useMyTasksSummary355(sessionStatus === 'ready' && !roleSwitchBusy && navigationAccess.canViewMyTasks ? sessionScope : null, JSON.stringify([listReloadToken, budgetListReloadToken, tasksOpenToken, taskSummaryRefreshToken]));
    const returnedNoticeSummary = useReturnedNoticeSummary377(sessionStatus === 'ready' && !roleSwitchBusy && navigationAccess.canAccessBusinessData ? sessionScope : null, JSON.stringify([listReloadToken, taskSummaryRefreshToken, effectiveViewKey, pageRefresh.busy]));
    const adjustmentNoticeSummary = useAdjustmentNoticeSummary448(sessionStatus === 'ready' && !roleSwitchBusy && navigationAccess.canViewMyTasks ? sessionScope : null, JSON.stringify([listReloadToken, budgetListReloadToken, taskSummaryRefreshToken, effectiveViewKey, pageRefresh.busy]));
    const staffNoticeSummary471 = useNoticeSummary448(sessionStatus === 'ready' && !roleSwitchBusy && navigationAccess.canUseStaffSubstitutions471 ? sessionScope : null, JSON.stringify([taskSummaryRefreshToken, effectiveViewKey]), staffPendingSummary471);
    activeViewKeyRef.current = effectiveViewKey;
    useEffect(() => {
        const activeLocked = navigationLocks.current.get(effectiveViewKey) === true;
        directoryNavigationLock.current = activeLocked;
        if (!activeLocked)
            setNavigationNotice(false);
    }, [effectiveViewKey]);
    useEffect(() => {
        if (sessionStatus !== 'ready' || currentSession === null)
            return;
        registerOpenView(effectiveView);
    }, [currentSession, effectiveViewKey, registerOpenView, sessionStatus]);
    const switchRole = useCallback(async (value: string) => {
        const [role, substitutionId] = value.split('@');
        const previous = currentSession;
        const allowed471 = substitutionId ? previous?.staffSubstitutionRoles471?.some(d => d.documentId === substitutionId && d.roleCode === role)
            : (previous?.ownRoleCodes471 ?? previous?.availableRoleCodes)?.includes(role);
        if (roleSwitchLock.current || sessionStatus !== 'ready' || !previous
            || !allowed471 || previous.activeRoleCode === role && previous.activeSubstitutionId471 === substitutionId)
            return;
        if (hasCommandLock()) {
            setProfileSwitchNotice('Сначала подтвердите результат отправленной команды в её вкладке.');
            return;
        }
        if (assignmentMutationLocks.current.size > 0) {
            setProfileSwitchNotice('Сначала подтвердите результат сохранения статьи ДДС.');
            return;
        }
        if ([...navigationLocks.current.values()].some(Boolean)) {
            setNavigationNotice(true);
            return;
        }
        if (hasPendingApiWrites()) {
            setProfileSwitchNotice('Дождитесь завершения отправки изменений, затем переключите роль.');
            return;
        }
        const blocked = pageRefresh.blockedReasonAll();
        if (blocked) {
            setProfileSwitchNotice(blocked);
            return;
        }
        const profile = profileForRole(role);
        if (profile === 'INTEGRATION_ADMIN' || previous.activeProfileCode === 'INTEGRATION_ADMIN')
            return;
        if (!window.confirm(`Переключиться на роль «${roleLabels[role] ?? role}»? Будут действовать только её права. `
            + 'Открытая рабочая область будет закрыта. Учетная запись и назначенные роли не меняются.'))
            return;
        roleSwitchLock.current = true;
        try {
            finishRoleSwitch.current = beginBmsRoleSwitch();
        }
        catch {
            roleSwitchLock.current = false;
            setProfileSwitchNotice('Дождитесь завершения отправки изменений, затем переключите роль.');
            return;
        }
        setRoleSwitchBusy(true);
        setProfileSwitchNotice('Проверяется выбранная роль…');
        const requested: RoleSelection = { profileCode: profile, roleCode: role, ...(substitutionId ? { substitutionId } : {}) };
        const prior: RoleSelection | undefined = previous.activeRoleCode ? { profileCode: previous.activeProfileCode, roleCode: previous.activeRoleCode,
            ...(previous.activeSubstitutionId471 ? { substitutionId: previous.activeSubstitutionId471 } : {}) } : undefined;
        try {
            // An isolated probe: the committed request headers and old workspaces
            // stay unchanged until the server confirms actor AND profile AND role.
            const confirmed = await getCurrentSession(requested);
            if (confirmed.actorExternalId !== previous.actorExternalId || confirmed.activeRoleCode !== role
                || confirmed.activeProfileCode !== profile || !confirmed.availableRoleCodes?.includes(role)
                || confirmed.activeSubstitutionId471 !== substitutionId)
                throw new Error('Role not confirmed');
            setActiveBmsProfileOverride(profile);
            activeProfileOverride.current = profile;
            setActiveBmsRoleOverride(role, substitutionId);
            setFinanceDrafts({});
            clearNavigationLocks();
            setOpenViews([]);
            setRetainedViews([]);
            setView({ kind: 'section', sectionId: 'home' });
            setCurrentSession(confirmed);
            setProfileSwitchNotice(null);
        }
        catch {
            try {
                const restored = await getCurrentSession(prior);
                if (restored.actorExternalId !== previous.actorExternalId || restored.activeRoleCode !== previous.activeRoleCode
                    || restored.activeProfileCode !== previous.activeProfileCode || restored.activeSubstitutionId471 !== previous.activeSubstitutionId471)
                    throw new Error('Previous role not confirmed');
                setCurrentSession(restored);
                setProfileSwitchNotice('Роль не переключена: сервер не подтвердил назначение. Открытые формы сохранены.');
            }
            catch {
                setCurrentSession(null);
                setSessionStatus('failed');
                setProfileSwitchNotice('Не удалось подтвердить текущую роль. Выполните повторную проверку сеанса.');
            }
        }
        finally {
            setRoleSwitchBusy(false);
        }
        // The transport fence is released in the layout commit, not here while
        // handlers from the previous render could still send a command.
    }, [clearNavigationLocks, currentSession, hasCommandLock, pageRefresh.blockedReasonAll, sessionStatus]);
    const staffChanged471 = useCallback(() => {
        refreshTaskSummary();
        const expected = sessionScopeRef.current;
        void getCurrentSession().then(next => {
            if (expected === sessionScopeRef.current && !roleSwitchLock.current)
                setCurrentSession(next);
        })
            .catch(() => setProfileSwitchNotice('Документ сохранён. Не удалось обновить список ролей; обновите сеанс перед работой по замещению.'));
    }, [refreshTaskSummary]);
    const closeOpenView = useCallback((key: string) => {
        if (hasCommandLock(key)) {
            setProfileSwitchNotice('Сначала подтвердите результат отправленной команды в её вкладке.');
            return;
        }
        if (assignmentMutationLocks.current.size > 0) {
            setProfileSwitchNotice('Сначала подтвердите результат сохранения статьи ДДС.');
            return;
        }
        browserNavigation.captureBeforeNavigation();
        // Closing a tab is an explicit discard action. It must not be blocked by
        // the dirty-form navigation guard and must not issue a save request. Keep
        // the guard for ordinary navigation, but release the removed view's lock
        // immediately so an explicitly closed dirty tab is discarded cleanly.
        setViewNavigationLock(key, false);
        setExpenseTabLabels((current) => {
            if (!(key in current))
                return current;
            const next = { ...current };
            delete next[key];
            return next;
        });
        setOpenViews((current) => {
            const index = current.findIndex((candidate) => keyForView(candidate) === key);
            if (index < 0)
                return current;
            const remaining = current.filter((_, candidateIndex) => candidateIndex !== index);
            setView((active) => {
                if (keyForView(active) !== key)
                    return active;
                return remaining[index - 1] ?? remaining[index] ?? { kind: 'section', sectionId: 'home' };
            });
            return remaining;
        });
    }, [setViewNavigationLock, browserNavigation]);
    const shellView: View = sessionStatus === 'ready' && currentSession !== null
        ? effectiveView
        : { kind: 'section', sectionId: 'home' };
    const shellOpenViews = sessionStatus === 'ready' && currentSession !== null
        ? openViews.filter((candidate) => isViewAvailable(candidate, navigationAccess))
        : [];
    const openView = (nextView: View, fromWorkspaceTab = false) => {
        if (assignmentMutationLocks.current.size > 0) {
            setProfileSwitchNotice('Сначала подтвердите результат сохранения статьи ДДС.');
            return;
        }
        browserNavigation.captureBeforeNavigation();
        // Navigation inside BMS never discards a local draft: every opened view
        // stays mounted in the workspace cache, so the user can move through the
        // menu and tabs in any form state and return to the draft later.
        setNavigationNotice(false);
        setCopyNoticeRequestId(null);
        // A navigation shortcut is a creation intent, not a reusable draft identity.
        // Allocate the same isolated workspace keys as the existing list action.
        if (nextView.kind === 'budget-document' && nextView.instanceId === null
            && nextView.workspaceKey === 'create') {
            nextView = { kind: 'budget-document', workspaceKey: `local:${++nextBudgetWorkspace.current}`, instanceId: null };
        }
        if (nextView.kind === 'expense-list' && !fromWorkspaceTab) {
            setListOpenToken((current) => current + 1);
        }
        if (nextView.kind === 'expense-payment-register') {
            setPaymentRegisterOpenToken(current => current + 1);
        }
        if (nextView.kind === 'my-tasks')
            setTasksOpenToken(current => current + 1);
        registerOpenView(nextView);
        setView(nextView);
    };
    // Activating an existing list tab must retain its filters/page, not remount it.
    const openTab = (nextView: View) => openView(nextView, true);
    const openBudgetDocument = (id: string, number: string) => {
        const existing = openViews.find((candidate) => candidate.kind === 'budget-document'
            && candidate.instanceId?.toLowerCase() === id.toLowerCase());
        openView(existing ?? { kind: 'budget-document', workspaceKey: `saved:${id.toLowerCase()}`, instanceId: id, number });
    };
    const newBudgetDocument = (source?: {
        id: string;
        number: string;
    }) => {
        // Local workspace identity is independent of SQL identity and never changes
        // after first save: cache state and command tickets belong to this key.
        openView({ kind: 'budget-document', workspaceKey: `local:${++nextBudgetWorkspace.current}`,
            instanceId: null, copyFromId: source?.id, number: source?.number });
    };
    const updateBudgetTab = (ownerKey: string, document: {
        id: string;
        number: string;
    }) => {
        const update = (candidate: View): View => candidate.kind === 'budget-document'
            && keyForView(candidate) === ownerKey
            ? { ...candidate, instanceId: document.id, number: document.number } : candidate;
        setOpenViews((current) => current.map(update));
        // A hidden save/GET must update its label, never steal the active document.
        setView(update);
    };
    const renderContent = (targetView: View) => {
        // No protected page is mounted before the session is confirmed, so no
        // page-level data request can start from a failed or pending session.
        if (sessionStatus === 'checking') {
            return <SessionGatePage state="checking"/>;
        }
        if (sessionStatus === 'failed') {
            return <SessionGatePage state={signedOut ? 'signed-out' : 'failed'} onRetry={retrySession}/>;
        }
        if (targetView.kind === 'help')
            return <HelpPage />;
        if (targetView.kind === 'expense-form') {
            return (<ExpenseRequestFormPage key={keyForView(targetView)} requestId={targetView.requestId} readOnly={currentSession?.isBmsAdministrator === true
                    || currentSession?.isBmsIntegrationAdministrator === true
                    || currentSession?.activeRoleCode === 'AUDITOR'} canEditExistingFallback={canCreateExpenseRequest} canCreateExpenseRequest={canCreateExpenseRequest} canCopyExpenseRequest={canCreateExpenseRequest &&
                    (currentSession?.activeRoleCode === 'CFO_INITIATOR' || currentSession?.activeRoleCode === 'BUDGET_OWNER')} currentActorExternalId={currentSession?.actorExternalId ?? null} currentActorDisplayName={currentSession?.actorDisplayName ?? null} onNavigationLockChange={(locked) => setViewNavigationLock(keyForView(targetView), locked)} onDocumentPresentationChange={({ number, dirty }) => {
                    const key = keyForView(targetView);
                    const label = `${number ?? (targetView.requestId === null ? 'Расходная заявка (создание)' : 'Расходная заявка')}${dirty ? ' *' : ''}`;
                    setExpenseTabLabels(previous => previous[key] === label ? previous : { ...previous, [key]: label });
                }} onSaved={() => setListReloadToken((current) => current + 1)} onCopied={(copied) => {
                    browserNavigation.captureBeforeNavigation();
                    setListReloadToken((current) => current + 1);
                    setCopyNoticeRequestId(copied.id);
                    // Open the server-created Draft in its own cached view. The source
                    // remains mounted with its original identity, status and local edits.
                    const copiedView = { kind: 'expense-form', requestId: copied.id } as const;
                    registerOpenView(copiedView);
                    setView(copiedView);
                }} showCopyCreatedWarning={copyNoticeRequestId === targetView.requestId}/>);
        }
        if (targetView.kind === 'my-tasks') {
            return <>{navigationAccess.canUseStaffSubstitutions471 && <StaffPending471 revision={JSON.stringify([sessionScope, taskSummaryRefreshToken])} onOpen={id => openView({ kind: 'staff-substitutions', documentId: id })}/>}
       <MyTasksPage355 refreshKey={JSON.stringify([sessionScope, tasksOpenToken, listReloadToken, budgetListReloadToken])} onRefreshRequested={refreshTaskSummary} onOpenExpense={id => openView({ kind: 'expense-form', requestId: id })} onOpenBudget={openBudgetDocument} onOpenAdjustment={id => openView({ kind: 'budget-adjustments', adjustmentId: id })}/></>;
        }
        if (targetView.kind === 'expense-payment-register') {
            return <ExpensePaymentRegisterPage key={paymentRegisterOpenToken} onBack={() => openView({ kind: 'expense-list' })}/>;
        }
        if (targetView.kind === 'expense-list') {
            return (<ExpenseRequestListPage key={`${keyForView(targetView)}:${listOpenToken}:${currentSession?.activeRoleCode ?? ''}`} activeRoleCode={currentSession?.activeRoleCode} reloadToken={listReloadToken} onCreate={() => openView({ kind: 'expense-form', requestId: null })} onOpen={(requestId) => openView({ kind: 'expense-form', requestId })} onOpenPaymentRegister={() => openView({ kind: 'expense-payment-register' })} canCreate={canCreateExpenseRequest} currentActorExternalId={currentSession?.actorExternalId ?? null} onNavigationLockChange={(locked) => setViewNavigationLock(keyForView(targetView), locked)} onCopied={(copied) => {
                    browserNavigation.captureBeforeNavigation();
                    setListReloadToken(current => current + 1);
                    setCopyNoticeRequestId(null);
                    const copiedView = { kind: 'expense-form', requestId: copied.id } as const;
                    registerOpenView(copiedView);
                    setView(copiedView);
                }}/>);
        }
        if (targetView.kind === 'integration-monitor') {
            return <IntegrationOutboxMonitorPage canViewNotificationAdministration={false}/>;
        }
        if (targetView.kind === 'accrual-notifications') {
            return <>{navigationAccess.canUseStaffSubstitutions471 && <StaffPending471 revision={JSON.stringify([sessionScope, taskSummaryRefreshToken])} onOpen={id => openView({ kind: 'staff-substitutions', documentId: id })}/>}
       <AccrualNotificationsPage adjustmentRefreshKey={JSON.stringify([sessionScope, budgetListReloadToken, taskSummaryRefreshToken])} canViewRegister={canAccessBusinessData && currentSession?.isBmsAdministrator !== true
                    && currentSession?.isBmsIntegrationAdministrator !== true
                    && (currentSession?.activeRoleCode === 'FIN_CONTROLLER' || currentSession?.activeRoleCode === 'FIN_DIRECTOR'
                        || currentSession?.activeRoleCode === 'AUDITOR')} onOpenRequest={(requestId) => openView({ kind: 'expense-form', requestId })} onOpenAdjustment={id => openView({ kind: 'budget-adjustments', adjustmentId: id })}/></>;
        }
        if (targetView.kind === 'business-audit-monitor') {
            return <BusinessAuditMonitorPage />;
        }
        if (targetView.kind === 'change-journal')
            return <ChangeJournalPage />;
        if (targetView.kind === 'budget-register')
            return <BudgetExecutionRegisterPage343 onOpenRequest={id => openView({ kind: 'expense-form', requestId: id })}/>;
        if (targetView.kind === 'exchange-journal')
            return <ExchangeJournalPage />;
        if (targetView.kind === 'session-monitor')
            return <SessionMonitorPage />;
        if (targetView.kind === 'reference-directory') {
            return <ReferenceDirectoryPage key={keyForView(targetView)} directoryId={targetView.directoryId} canManageReferenceData={currentSession?.canManageReferenceData === true} onNavigationLockChange={(locked) => {
                    const key = keyForView(targetView);
                    if (locked)
                        assignmentMutationLocks.current.add(key);
                    else
                        assignmentMutationLocks.current.delete(key);
                    setViewNavigationLock(key, locked);
                    if (!locked)
                        setProfileSwitchNotice(null);
                }} canEditExpenseItems={currentSession?.canManageReferenceData === true} canEditOrganizations={currentSession?.canManageReferenceData === true}/>;
        }
        if (targetView.kind === 'budget-owner-cost-center-assignment-directory') {
            return <AuthorizationDirectoryPage canEdit={currentSession?.canManageReferenceData === true} canManageRoles={currentSession?.activeRoleCode === 'SYSTEM_ADMIN'} onNavigationLockChange={(locked) => setViewNavigationLock(keyForView(targetView), locked)}/>;
        }
        if (targetView.kind === 'budget-execution') {
            return <BudgetExecutionPage />;
        }
        if (targetView.kind === 'bdds-report') {
            return <BddsReportPage onOpenDrill={context => openView({ kind: 'bdds-drill', workspaceKey: rememberBddsDrill(context) })}/>;
        }
        if (targetView.kind === 'bdds-drill') {
            return <BddsDrillPage workspaceKey={targetView.workspaceKey} onBdds={() => openView({ kind: 'bdds-report' })}/>;
        }
        if (targetView.kind === 'unbudgeted-articles') {
            return <UnbudgetedArticlesPage />;
        }
        if (targetView.kind === 'budget-instances') {
            return <BudgetInstancesPage onNavigationLockChange={(locked) => setViewNavigationLock(keyForView(targetView), locked)} listReloadToken={budgetListReloadToken} onOpenInstance={openBudgetDocument} onCreateInstance={() => newBudgetDocument()} onCopyInstance={(id, number) => newBudgetDocument({ id, number })}/>;
        }
        if (targetView.kind === 'budget-adjustments') {
            return <BudgetAdjustmentsPage baseBudgetId={targetView.baseBudgetId} adjustmentId={targetView.adjustmentId} onChanged={() => { setBudgetListReloadToken(value => value + 1); refreshTaskSummary(); }} onNavigationLockChange={locked => setViewNavigationLock(keyForView(targetView), locked)}/>;
        }
        if (targetView.kind === 'company-budget-versions') {
            return <CompanyBudgetVersionsPage onOpenInstance={openBudgetDocument} onOpenAdjustments={baseBudgetId => openView({ kind: 'budget-adjustments', baseBudgetId })} onNavigationLockChange={(locked) => setViewNavigationLock(keyForView(targetView), locked)}/>;
        }
        if (targetView.kind === 'budget-document') {
            return <BudgetInstancesPage initialDocument={targetView.instanceId !== null ? { kind: 'open', id: targetView.instanceId }
                    : targetView.copyFromId ? { kind: 'copy', id: targetView.copyFromId } : { kind: 'new' }} onNavigationLockChange={(locked) => setViewNavigationLock(keyForView(targetView), locked)} onBackToList={() => openView({ kind: 'budget-instances' })} onOpenAdjustments={baseBudgetId => openView({ kind: 'budget-adjustments', baseBudgetId })} onDocumentLoaded={(document) => updateBudgetTab(keyForView(targetView), document)} onDocumentSaved={(document) => {
                    updateBudgetTab(keyForView(targetView), document);
                    setBudgetListReloadToken((current) => current + 1);
                }}/>;
        }
        if (targetView.kind === 'budget-directory') {
            return <BudgetDirectoryPage canEdit={currentSession?.activeRoleCode !== 'AUDITOR'} onNavigationLockChange={(locked) => setViewNavigationLock(keyForView(targetView), locked)}/>;
        }
        if (targetView.kind === 'authorization-directory') {
            return <AuthorizationDirectoryPage canEdit={currentSession?.canManageReferenceData === true} canManageRoles={currentSession?.activeRoleCode === 'SYSTEM_ADMIN'} onNavigationLockChange={(locked) => setViewNavigationLock(keyForView(targetView), locked)}/>;
        }
        if (targetView.kind === 'currency-rates')
            return <CurrencyRatesPage canEdit={currentSession?.canManageReferenceData === true} onNavigationLockChange={(locked) => setViewNavigationLock(keyForView(targetView), locked)}/>;
        if (targetView.kind === 'approval-matrix') {
            return <ApprovalMatrixPage />;
        }
        if (targetView.kind === 'dated-payment-calendar') {
            return <DatedPaymentCalendarPage onOpenRequest={(requestId) => openView({ kind: 'expense-form', requestId })}/>;
        }
        if (targetView.kind === 'payment-calendar' || targetView.kind === 'goods-calendar') {
            return (<PaymentCalendarPage goodsOnly={targetView.kind === 'goods-calendar'} onOpenRequest={(requestId) => openView({ kind: 'expense-form', requestId })}/>);
        }
        if (targetView.kind === 'manual-deficits') {
            return <ManualDeficitsPage />;
        }
        if (targetView.kind === 'active-accruals') {
            return (<ActiveAccrualReportPage onOpenRequest={(requestId) => openView({ kind: 'expense-form', requestId })}/>);
        }
        if (targetView.kind === 'opiu-report') {
            return <OpiuReportPage onOpenRequest={requestId => openView({ kind: 'expense-form', requestId })}/>;
        }
        if (targetView.kind === 'finance-workspace') {
            const workspaceId = targetView.workspaceId;
            return <FinanceWorkspacePage key={keyForView(targetView)} workspaceId={workspaceId} draft={financeDrafts[workspaceId] ?? emptyFinancePreparation} onDraftChange={(draft) => setFinanceDrafts(current => ({ ...current, [workspaceId]: draft }))} onOpenRequest={(requestId) => openView({ kind: 'expense-form', requestId })} onOpenCalendar={() => openView({ kind: 'payment-calendar' })}/>;
        }
        if (targetView.kind === 'placeholder') {
            return <PlaceholderPage title={targetView.title}/>;
        }
        if (targetView.kind === 'staff-substitutions')
            return <StaffSubstitutionPage471 documentId={targetView.documentId} refreshKey={String(taskSummaryRefreshToken)} acting={currentSession?.activeSubstitutionId471 !== undefined} onOpen={id => openView({ kind: 'staff-substitutions', ...(id ? { documentId: id } : {}) })} onChanged={staffChanged471} onDirty={dirty => setViewNavigationLock(keyForView(targetView), dirty)}/>;
        const section = SECTIONS.find((candidate) => candidate.id === targetView.sectionId) ?? SECTIONS.find(s => s.id === 'home')!;
        return (<>{targetView.sectionId === 'home' && navigationAccess.canUseStaffSubstitutions471 && <StaffNews471 revision={JSON.stringify([sessionScope, taskSummaryRefreshToken])}/>}
       <SectionPage canUseStaffSubstitutions471={navigationAccess.canUseStaffSubstitutions471} section={section} onOpen={openView} canCreateExpenseRequest={canCreateExpenseRequest} canAccessBusinessData={canAccessBusinessData} canViewIntegrationMonitor={canViewIntegrationMonitor} canViewBusinessAuditMonitor={canViewBusinessAuditMonitor} canViewBudgetExecution={canViewBudgetExecution} canViewAdministration={canViewAdministration} canViewAdministrationReports={canViewAdministrationReports} canViewExchangeJournal={canViewExchangeJournal}/></>);
    };
    const cachedViews = [
        effectiveView,
        ...shellOpenViews.filter((candidate) => keyForView(candidate) !== effectiveViewKey),
        ...(sessionStatus === 'ready' && currentSession !== null ? retainedViews.filter(candidate => keyForView(candidate) !== effectiveViewKey && isViewAvailable(candidate, navigationAccess)) : []),
    ];
    const content = (<>
      {cachedViews.map((cachedView) => {
            const cachedKey = keyForView(cachedView);
            const active = cachedKey === effectiveViewKey;
            return (<WorkspaceViewCache key={cachedKey} active={active}>
            <WorkspaceRefreshProvider viewKey={cachedKey} registry={pageRefresh.registry}>
            <WorkspaceCommandLockContext.Provider value={{ viewKey: cachedKey, scopeKey: sessionScope,
                    isScopeCurrent: scope => sessionScopeRef.current === scope, setLock: setCommandLock,
                    hasViewCommands: () => hasCommandLock(cachedKey) }}>
              <WorkspacePageBoundary>{renderContent(cachedView)}</WorkspacePageBoundary>
            </WorkspaceCommandLockContext.Provider>
            </WorkspaceRefreshProvider>
          </WorkspaceViewCache>);
        })}
    </>);
    return (<AttachmentPreviewWorkspace scopeKey={sessionScope}>
    <EnterpriseShell canViewAdministration={canViewAdministration || canViewAdministrationReports || canViewExchangeJournal} canOpenHelp={sessionStatus === 'ready' && currentSession !== null} view={shellView} onOpen={next => {
            if (!roleSwitchLock.current)
                openView(next);
        }} onOpenTab={next => {
            if (!roleSwitchLock.current)
                openTab(next);
        }} onCloseDocument={() => {
            if (!roleSwitchLock.current)
                openView({ kind: 'expense-list' });
        }} openViews={shellOpenViews} tabLabels={expenseTabLabels} onCloseTab={key => {
            if (!roleSwitchLock.current)
                closeOpenView(key);
        }} demoMode={useMockApi} navigationItems={shellNavigationItems} taskSummary={taskSummary} returnedNoticeSummary={returnedNoticeSummary} adjustmentNoticeSummary={adjustmentNoticeSummary} staffNoticeSummary471={staffNoticeSummary471} canViewStaff471={navigationAccess.canUseStaffSubstitutions471} activeRoleValue471={currentSession?.staffSubstitutionRoles471 === undefined ? undefined : roleSelectionValue471(currentSession.activeRoleCode, currentSession.activeSubstitutionId471)} roleChoices471={currentSession?.staffSubstitutionRoles471 === undefined ? undefined : [
            ...(currentSession.ownRoleCodes471 ?? []).map(role => ({ value: role, label: roleLabels[role] ?? role })),
            ...currentSession.staffSubstitutionRoles471.map(d => ({ value: roleSelectionValue471(d.roleCode, d.documentId), label: `${roleLabels[d.roleCode]} · замещение ${d.principalName}` }))
        ]} activeProfileCode={currentSession?.activeProfileCode} availableProfileCodes={currentSession?.availableProfileCodes ?? []} onProfileChange={(profile) => { void switchAccessProfile(profile); }} activeRoleCode={currentSession?.activeRoleCode} availableRoleCodes={currentSession?.availableRoleCodes} onRoleChange={role => { void switchRole(role); }} roleSwitchBusy={roleSwitchBusy} onLogout={() => {
            if (!roleSwitchLock.current)
                logout();
        }} mainAreaRef={mainArea} onRefresh={() => { void pageRefresh.refresh(); }} refreshBusy={pageRefresh.busy} refreshDisabled={roleSwitchBusy || sessionStatus !== 'ready' || currentSession === null} actorLabel={currentSession?.actorDisplayName ?? currentSession?.actorExternalId
            ?? (sessionStatus === 'failed' ? 'Сеанс недоступен' : 'Проверка сеанса…')}>
      {pageRefresh.notice !== null && <p className="notice" role="status">{pageRefresh.notice}</p>}
      {profileSwitchNotice !== null && <p role="status" className="profile-switch-notice">
        {profileSwitchNotice}
      </p>}
      {navigationNotice && <p role="alert" tabIndex={-1} ref={navigationNoticeRef} className="directory-navigation-notice">
        Сначала сохраните или отмените изменения в открытой форме.
        Если результат записи неизвестен, проверьте его в этой форме перед переходом.
      </p>}
      <WorkspacePageBoundary>{content}</WorkspacePageBoundary>
    </EnterpriseShell>
    </AttachmentPreviewWorkspace>);
}
