import { HelpCircle } from 'lucide-react';
import { SECTIONS, type SectionId } from '../../navigation';
import { SectionIcon } from '../SectionIcon';
type Props = {
    activeSection: SectionId;
    collapsed: boolean;
    onToggleSidebar?: () => void;
    onOpenSection: (sectionId: SectionId) => void;
    onOpenHelp: () => void;
    helpActive?: boolean;
    canOpenHelp?: boolean;
    canViewAdministration?: boolean;
    canViewStaff471?: boolean;
};
export function SidebarNavigation({ activeSection, collapsed, onToggleSidebar, onOpenSection, onOpenHelp, helpActive = false, canOpenHelp = true, canViewAdministration = false, canViewStaff471 = false }: Props) {
    return (<aside id="bms-sidebar" className="sidebar" aria-label="Боковое меню BMS">
      {onToggleSidebar && <button className="sidebar-edge-toggle" type="button" aria-label={collapsed ? 'Развернуть боковую панель' : 'Свернуть боковую панель'} title={collapsed ? 'Развернуть боковую панель' : 'Свернуть боковую панель'} aria-controls="bms-sidebar" aria-expanded={!collapsed} onClick={onToggleSidebar}>
        <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true">
          <path d={collapsed ? 'M4.5 2 L9.5 6 L4.5 10 Z' : 'M8.5 2 L3.5 6 L8.5 10 Z'} fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinejoin="round"/>
        </svg>
      </button>}
      <nav aria-label="Разделы BMS">
        {SECTIONS.filter(candidate => (candidate.id !== 'administration' || canViewAdministration) && (candidate.id !== 'staff' || canViewStaff471)).map((candidate) => (<button key={candidate.id} className={`nav-item ${candidate.id === activeSection ? 'active' : ''}`} type="button" aria-label={candidate.label} aria-current={candidate.id === activeSection ? 'page' : undefined} title={collapsed ? candidate.label : undefined} onClick={() => onOpenSection(candidate.id)}>
            <SectionIcon sectionId={candidate.id}/>
            <span>{candidate.label}</span>
          </button>))}
      </nav>
      <button className={`sidebar-footer${helpActive ? ' active' : ''}`} type="button" aria-label="Помощь" aria-current={helpActive ? 'page' : undefined} disabled={!canOpenHelp} title={!canOpenHelp ? 'Помощь доступна после входа' : collapsed ? 'Помощь' : undefined} onClick={onOpenHelp}>
        <HelpCircle size={18} aria-hidden="true"/>
        <span>Помощь</span>
      </button>
    </aside>);
}
