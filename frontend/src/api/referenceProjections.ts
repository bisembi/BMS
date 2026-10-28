import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import type { ReferenceProjectionInventory } from '../types';
import { publicReferenceProjectionInventory } from './technicalMonitorPublicResponse';
export async function getReferenceProjectionInventory(): Promise<ReferenceProjectionInventory> {
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/integration-monitor/reference-projections'), secureReadInit());
    if (!response.ok) {
        if (response.status === 401) {
            throw new Error('Для просмотра инвентаризации проекций требуется аутентификация.');
        }
        if (response.status === 403) {
            throw new Error('Недостаточно прав для просмотра инвентаризации проекций.');
        }
        throw new Error('Не удалось загрузить инвентаризацию проекций.');
    }
    return publicReferenceProjectionInventory(await response.json());
}
