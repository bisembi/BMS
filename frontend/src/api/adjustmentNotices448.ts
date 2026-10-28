import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { publicMyApprovalTaskPage355, type MyApprovalTaskPage355 } from './myTasks355';
export async function listAdjustmentNotices448(skip = 0, take = 25, signal?: AbortSignal): Promise<MyApprovalTaskPage355> {
    if (!Number.isSafeInteger(skip) || skip < 0 || !Number.isInteger(take) || take < 1 || take > 100)
        throw Error('Некорректная страница уведомлений.');
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/notifications/budget-adjustments?skip=${skip}&take=${take}`), { ...secureReadInit(), signal });
    if (response.status !== 200)
        throw Error('Не удалось загрузить уведомления о корректировках.');
    const page = publicMyApprovalTaskPage355(await response.json(), skip, take);
    if (page.items.some(item => item.kind !== 'BudgetAdjustment'))
        throw Error('Некорректный вид уведомления.');
    return page;
}
