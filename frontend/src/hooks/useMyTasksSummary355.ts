import { listMyApprovalTasks355 } from '../api/myTasks355';
import type { ExpenseRequestTaskSummary } from './useExpenseRequestTaskSummary';
import { useNoticeSummary448 } from './useReturnedNoticeSummary377';
export function useMyTasksSummary355(context: string | null, revision: string): ExpenseRequestTaskSummary {
    return useNoticeSummary448(context, revision, listMyApprovalTasks355);
}
