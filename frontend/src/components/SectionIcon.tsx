import budgetingIcon from '../assets/navigation/BMS_Budgeting_24.png';
import directoriesIcon from '../assets/navigation/BMS_Directories_24.png';
import mainIcon from '../assets/navigation/BMS_Main_24.png';
import managementAccountingIcon from '../assets/navigation/BMS_ManagementAccounting_24.png';
import treasuryIcon from '../assets/navigation/BMS_Treasury_24.png';
import type { SectionId } from '../navigation';
import { Shield, UsersRound } from 'lucide-react';
const icons: Record<SectionId, string> = {
    home: mainIcon,
    budgeting: budgetingIcon,
    treasury: treasuryIcon,
    management: managementAccountingIcon,
    directories: directoriesIcon,
    administration: '',
    staff: '',
};
export function SectionIcon({ sectionId }: {
    sectionId: SectionId;
}) {
    if (sectionId === 'staff')
        return <UsersRound className="nav-icon" aria-hidden="true"/>;
    if (sectionId === 'administration')
        return <Shield className="nav-icon" aria-hidden="true"/>;
    return <img className="nav-icon" src={icons[sectionId]} alt="" aria-hidden="true"/>;
}
