import { Bell, ChevronDown, Menu, RefreshCw, UserRound } from 'lucide-react';
import { useState } from 'react';
import type { ExpenseRequestTaskSummary } from '../../hooks/useExpenseRequestTaskSummary';
import type { NavigationSearchItem, View } from '../../navigation';
import type { BmsAccessProfileCode, SwitchableBmsAccessProfileCode } from '../../types';
import { ShellNavigationSearch } from './ShellNavigationSearch';
import { roleLabels } from '../../utils/activeRole';
import { ShellTaskInboxButton } from './ShellTaskInboxButton';
type Props = {
    actorLabel: string;
    demoMode?: boolean;
    navigationItems: readonly NavigationSearchItem[];
    onOpen: (view: View) => void;
    sidebarExpanded: boolean;
    onToggleSidebar: () => void;
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
    onRefresh?: () => void;
    refreshBusy?: boolean;
    refreshDisabled?: boolean;
};
export function ShellHeaderBar({ actorLabel, demoMode = false, navigationItems, onOpen, sidebarExpanded, onToggleSidebar, taskSummary, returnedNoticeSummary, adjustmentNoticeSummary, staffNoticeSummary471, roleChoices471, activeRoleValue471, activeProfileCode, availableProfileCodes = [], onProfileChange, activeRoleCode, availableRoleCodes, onRoleChange, roleSwitchBusy = false, onLogout, onRefresh, refreshBusy = false, refreshDisabled = false, }: Props) {
    const [userMenuOpen, setUserMenuOpen] = useState(false);
    const returnedCount = returnedNoticeSummary?.status === 'ready' ? returnedNoticeSummary.count : 0;
    const adjustmentCount = adjustmentNoticeSummary?.status === 'ready' ? adjustmentNoticeSummary.count : 0;
    const staffCount471 = staffNoticeSummary471?.status === 'ready' ? staffNoticeSummary471.count : 0;
    const noticeCount = returnedCount + adjustmentCount + staffCount471;
    const roleOptions471 = roleChoices471 ?? availableRoleCodes?.map(code => ({ value: code, label: roleLabels[code] ?? code }));
    const canOpenNotifications = navigationItems.some((item) => item.view.kind === 'accrual-notifications');
    const canSwitchProfile = availableProfileCodes.includes('BUSINESS')
        && availableProfileCodes.includes('SYSTEM_ADMIN')
        && activeProfileCode !== undefined
        && onProfileChange !== undefined;
    return (<header className="topbar">
      <div className="brand-mark" aria-label="BMS">BMS</div>
      <button className="icon-button" type="button" aria-label={sidebarExpanded ? 'Свернуть меню' : 'Развернуть меню'} aria-controls="bms-sidebar" aria-expanded={sidebarExpanded} onClick={onToggleSidebar}>
        <Menu aria-hidden="true"/>
      </button>
      <div className="product-name">Система управления бюджетами</div>
      {demoMode && (<span className="demo-mode" role="status" aria-label="Демонстрационный режим">
          Демо-режим
        </span>)}
      <ShellNavigationSearch items={navigationItems} onOpen={onOpen}/>
      <button className="icon-button" type="button" aria-label="Обновить текущую страницу" title="Обновить текущую страницу" aria-busy={refreshBusy} disabled={refreshDisabled || refreshBusy || onRefresh === undefined} onClick={onRefresh}>
        <RefreshCw aria-hidden="true"/>
      </button>
      {availableRoleCodes !== undefined && (<label className="access-profile-switch">
          <span>Роль</span>
          <select aria-label="Роль BMS" value={activeRoleValue471 ?? activeRoleCode ?? ''} disabled={roleSwitchBusy || !roleOptions471?.length || roleOptions471.length === 1 && roleOptions471[0].value === (activeRoleValue471 ?? activeRoleCode ?? '') || !onRoleChange} onChange={event => onRoleChange?.(event.currentTarget.value)}>
            {availableRoleCodes.length === 0 && <option value="">Роль не назначена</option>}
            {roleOptions471?.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </label>)}
      {availableRoleCodes === undefined && canSwitchProfile && (<label className="access-profile-switch">
          <span>Профиль</span>
          <select aria-label="Профиль BMS" value={activeProfileCode} onChange={(event) => onProfileChange(event.currentTarget.value as SwitchableBmsAccessProfileCode)}>
            <option value="BUSINESS">Бизнес-процессы</option>
            <option value="SYSTEM_ADMIN">Системный администратор</option>
          </select>
        </label>)}
      {taskSummary && navigationItems.some(item => item.view.kind === 'my-tasks')
            && <ShellTaskInboxButton summary={taskSummary} onOpen={() => onOpen({ kind: 'my-tasks' })}/>}
      <button className="icon-button notification-inbox-button" type="button" aria-label="Уведомления" aria-describedby={canOpenNotifications ? undefined : 'notifications-unavailable'} title={canOpenNotifications ? noticeCount > 0
            ? [returnedCount > 0 ? `Возвращено на доработку: ${returnedCount}` : '', adjustmentCount > 0 ? `Корректировок на согласовании: ${adjustmentCount}` : '', staffCount471 > 0 ? `Замещений на подтверждение: ${staffCount471}` : ''].filter(Boolean).join('; ')
            : 'Мои уведомления' : 'Уведомления недоступны для текущего сеанса'} disabled={!canOpenNotifications} onClick={() => {
            if (canOpenNotifications)
                onOpen({ kind: 'accrual-notifications' });
        }}>
        <Bell aria-hidden="true"/>
        {canOpenNotifications && noticeCount > 0 &&
            <span className="task-inbox-count notification-inbox-count" aria-hidden="true">{noticeCount > 99 ? '99+' : noticeCount}</span>}
      </button>
      {!canOpenNotifications && <span id="notifications-unavailable" className="sr-only">
        Уведомления недоступны для текущего сеанса.
      </span>}
      <div className="user-menu">
        <button className="user-chip" type="button" aria-label="Меню пользователя" aria-haspopup="menu" aria-expanded={userMenuOpen} onClick={() => setUserMenuOpen((open) => !open)}>
          <UserRound size={18} aria-hidden="true"/>
          <span>{actorLabel}</span>
          <ChevronDown size={15} aria-hidden="true"/>
        </button>
        {userMenuOpen && (<div className="user-menu-popup" role="menu" aria-label="Действия пользователя">
            <button type="button" role="menuitem" onClick={() => {
                setUserMenuOpen(false);
                onLogout?.();
            }}>
              Выход
            </button>
          </div>)}
      </div>
    </header>);
}
