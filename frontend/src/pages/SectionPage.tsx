import { isNavLinkAvailable, type NavigationAccess, type NavLink, type SectionDefinition, type View } from '../navigation';
function SectionLink({ link, onOpen }: {
    link: NavLink;
    onOpen: (view: View) => void;
}) {
    return (<button type="button" className={`section-link ${link.primary ? 'primary' : ''} ${link.emphasized ? 'emphasized' : ''}`} onClick={() => onOpen(link.view)}>
      {link.title}
    </button>);
}
export function SectionPage({ section, onOpen, canCreateExpenseRequest, canAccessBusinessData, canViewIntegrationMonitor, canViewBusinessAuditMonitor, canViewBudgetExecution, canViewAdministration = false, canViewAdministrationReports = false, canViewExchangeJournal = false, canUseStaffSubstitutions471 = false, }: {
    section: SectionDefinition;
    onOpen: (view: View) => void;
    canCreateExpenseRequest: boolean;
    canAccessBusinessData: boolean;
    canViewIntegrationMonitor: boolean;
    canViewBusinessAuditMonitor: boolean;
    canViewBudgetExecution: boolean;
    canViewAdministration?: boolean;
    canViewAdministrationReports?: boolean;
    canViewExchangeJournal?: boolean;
    canUseStaffSubstitutions471?: boolean;
}) {
    const access: NavigationAccess = {
        canUseStaffSubstitutions471,
        canCreateExpenseRequest,
        canAccessBusinessData,
        canViewIntegrationMonitor,
        canViewBusinessAuditMonitor,
        canViewBudgetExecution,
        canViewAdministration,
        canViewAdministrationReports,
        canViewExchangeJournal,
    };
    return (<div className="section-page" aria-label={`Раздел ${section.label}`}>
      {section.columns.map((column, columnIndex) => (<div className="section-column" key={`${section.id}-${columnIndex}`}>
          {columnIndex === 0 && section.lead && (<div className="section-lead">
              {isNavLinkAvailable(section.lead, access) && (<SectionLink link={section.lead} onOpen={onOpen}/>)}
              {section.leadExtra
                    ?.filter((link) => isNavLinkAvailable(link, access))
                    .map((link) => (<SectionLink key={link.title} link={link} onOpen={onOpen}/>))}
            </div>)}
          {column.map((group) => (<section className="section-group" key={group.title}>
              <h2>{group.title}</h2>
              <div className="section-links">
                {group.links
                    .filter((link) => isNavLinkAvailable(link, access))
                    .map((link) => <SectionLink key={link.title} link={link} onOpen={onOpen}/>)}
              </div>
            </section>))}
        </div>))}
    </div>);
}
