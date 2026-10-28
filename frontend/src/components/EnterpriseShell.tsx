import { useState, type PropsWithChildren, type RefObject } from 'react';
import { isBookmarkableView, keyForView, sectionForView, titleForView, type NavigationSearchItem, type SectionId, type View } from '../navigation';
import type { ExpenseRequestTaskSummary } from '../hooks/useExpenseRequestTaskSummary';
import type { BmsAccessProfileCode, SwitchableBmsAccessProfileCode } from '../types';
import { ShellHeaderBar } from './shell/ShellHeaderBar';
import { WorkspaceTableResize } from './table-resize/WorkspaceTableResize';
import { SidebarNavigation } from './shell/SidebarNavigation';
import { workspaceTabDomId, WorkspaceTabStrip, type ShellTab } from './shell/WorkspaceTabStrip';
function selectedKeyForView(view: View): string {
    return keyForView(view);
}
type Props = PropsWithChildren<{
    view: View;
    onOpen: (view: View) => void;
    onOpenTab?: (view: View) => void;
    onCloseDocument: () => void;
    /// Concrete workspace pages only; section landing pages never become tabs.
    openViews?: readonly View[];
    tabLabels?: Readonly<Record<string, string>>;
    onCloseTab?: (key: string) => void;
    actorLabel: string;
    demoMode?: boolean;
    navigationItems?: readonly NavigationSearchItem[];
    taskSummary?: ExpenseRequestTaskSummary;
    returnedNoticeSummary?: ExpenseRequestTaskSummary;
    adjustmentNoticeSummary?: ExpenseRequestTaskSummary;
    staffNoticeSummary471?: ExpenseRequestTaskSummary;
    roleChoices471?: readonly {
        value: string;
        label: string;
    }[];
    activeRoleValue471?: string;
    activeProfileCode?: BmsAccessProfileCode;
    availableProfileCodes?: readonly BmsAccessProfileCode[];
    onProfileChange?: (profile: SwitchableBmsAccessProfileCode) => void;
    activeRoleCode?: string | null;
    availableRoleCodes?: readonly string[];
    onRoleChange?: (role: string) => void;
    roleSwitchBusy?: boolean;
    onLogout?: () => void;
    mainAreaRef?: RefObject<HTMLElement | null>;
    onRefresh?: () => void;
    refreshBusy?: boolean;
    refreshDisabled?: boolean;
    canViewAdministration?: boolean;
    canViewStaff471?: boolean;
    canOpenHelp?: boolean;
}>;
export function EnterpriseShell({ view, onOpen, onOpenTab, onCloseDocument, openViews, tabLabels = {}, onCloseTab, actorLabel, demoMode = false, navigationItems = [], taskSummary, returnedNoticeSummary, adjustmentNoticeSummary, staffNoticeSummary471, roleChoices471, activeRoleValue471, activeProfileCode, availableProfileCodes = [], onProfileChange, activeRoleCode, availableRoleCodes, onRoleChange, roleSwitchBusy, onLogout, mainAreaRef, onRefresh, refreshBusy, refreshDisabled, canViewAdministration = false, canViewStaff471 = false, canOpenHelp = true, children, }: Props) {
    const [sidebarCollapsed, setSidebarCollapsed] = useState(true);
    const toggleSidebar = () => setSidebarCollapsed((collapsed) => !collapsed);
    const activeSection = sectionForView(view);
    const isDocument = view.kind === 'expense-form' || view.kind === 'budget-document' || view.kind === 'help';
    const openSection = (sectionId: SectionId) => onOpen({ kind: 'section', sectionId });
    const openTab = onOpenTab ?? onOpen;
    // The start page is a shell-owned anchor, not a closable business workspace.
    // Reuse the existing Home screen and the normal guarded navigation path.
    const tabs: ShellTab[] = [{
            key: 'home',
            label: 'Начальная страница',
            open: () => openTab({ kind: 'section', sectionId: 'home' }),
            closable: false,
        }];
    if (openViews !== undefined) {
        // Every concrete page is independent. Never substitute a sidebar section
        // for an opened list, or closing a document would lose its list tab.
        const seen = new Set<string>();
        for (const openView of [...openViews, view]) {
            const key = keyForView(openView);
            if (!isBookmarkableView(openView) || seen.has(key))
                continue;
            seen.add(key);
            tabs.push({
                key,
                label: tabLabels[key] ?? titleForView(openView),
                open: () => openTab(openView),
                closable: true,
                onClose: () => onCloseTab?.(key),
            });
        }
    }
    else {
        // Isolated shell consumers obey the same rule without a tab registry.
        if (isBookmarkableView(view)) {
            tabs.push({
                key: 'document',
                label: titleForView(view),
                open: () => openTab(view),
            });
        }
    }
    const selectedKey = view.kind === 'section' && view.sectionId === 'home' ? 'home' : openViews === undefined
        ? (isBookmarkableView(view) ? 'document' : '')
        : selectedKeyForView(view);
    const selectedIndex = tabs.findIndex((tab) => tab.key === selectedKey);
    const selectedTabId = selectedIndex < 0 ? undefined : workspaceTabDomId(tabs[selectedIndex].key, selectedIndex);
    return (<div className={`app-shell bms-unified-theme${sidebarCollapsed ? ' sidebar-collapsed' : ''}`}>
      <ShellHeaderBar actorLabel={actorLabel} demoMode={demoMode} navigationItems={navigationItems} onOpen={onOpen} sidebarExpanded={!sidebarCollapsed} onToggleSidebar={toggleSidebar} taskSummary={taskSummary} returnedNoticeSummary={returnedNoticeSummary} adjustmentNoticeSummary={adjustmentNoticeSummary} staffNoticeSummary471={staffNoticeSummary471} roleChoices471={roleChoices471} activeRoleValue471={activeRoleValue471} activeProfileCode={activeProfileCode} availableProfileCodes={availableProfileCodes} onProfileChange={onProfileChange} activeRoleCode={activeRoleCode} availableRoleCodes={availableRoleCodes} onRoleChange={onRoleChange} roleSwitchBusy={roleSwitchBusy} onLogout={onLogout} onRefresh={onRefresh} refreshBusy={refreshBusy} refreshDisabled={refreshDisabled}/>

      {tabs.length > 0 && <WorkspaceTabStrip tabs={tabs} selectedIndex={selectedIndex} closable={openViews === undefined && isDocument} onCloseDocument={onCloseDocument}/>}

      <SidebarNavigation canViewStaff471={canViewStaff471} canViewAdministration={canViewAdministration} activeSection={activeSection} collapsed={sidebarCollapsed} onToggleSidebar={toggleSidebar} onOpenSection={openSection} onOpenHelp={() => onOpen({ kind: 'help' })} helpActive={view.kind === 'help'} canOpenHelp={canOpenHelp}/>

      <main className="main-area" ref={mainAreaRef} inert={roleSwitchBusy || undefined} aria-busy={roleSwitchBusy}>
        <div id="workspace-panel" className="workspace-panel" role={selectedTabId ? 'tabpanel' : 'region'} aria-labelledby={selectedTabId} aria-label={selectedTabId ? undefined : titleForView(view)}>
          {children}
        </div>
        <WorkspaceTableResize />
      </main>
    </div>);
}
