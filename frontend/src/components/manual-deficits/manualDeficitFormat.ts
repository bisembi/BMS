import type { ManualDeficitCorrectionState } from '../../api/manualDeficitTypes';
import { formatBusinessTimestamp } from '../../utils/businessTime';
const amount = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 });
export const deficitMoney = (value: number, currency: string) => `${amount.format(value)} ${currency}`;
export const deficitDate = formatBusinessTimestamp;
export const correctionStateLabels: Record<ManualDeficitCorrectionState, string> = {
    PendingApproval: 'Ожидает решения', ApprovedAndApplied: 'Подтверждена и применена',
    Rejected: 'Отклонена', Annulled: 'Аннулирована — остаток изменился',
};
