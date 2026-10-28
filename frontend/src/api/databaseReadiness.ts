import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import type { DatabaseReadinessMonitor } from '../types';
import { publicDatabaseReadinessMonitor } from './technicalMonitorPublicResponse';
export async function getDatabaseReadinessMonitor(): Promise<DatabaseReadinessMonitor> {
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/integration-monitor/database-readiness'), secureReadInit());
    if (!response.ok) {
        if (response.status === 401) {
            throw new Error('Для проверки готовности БД требуется аутентификация.');
        }
        if (response.status === 403) {
            throw new Error('Недостаточно прав для проверки готовности БД.');
        }
        throw new Error('Не удалось проверить готовность базы данных.');
    }
    return publicDatabaseReadinessMonitor(await response.json());
}
