import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import type { OutboxBacklogSummary, OutboxMonitorPage } from '../types';
import { publicOutboxBacklogSummary, publicOutboxMonitorPage } from './outboxPublicResponse';
const invalidMonitorPagingMessage = 'Некорректные параметры страницы сервисного монитора.';
const maximumInt32 = 2147483647;
function assertMonitorPaging(skip: number, take: number): void {
    if (!Number.isSafeInteger(skip)
        || skip < 0
        || skip > maximumInt32
        || !Number.isSafeInteger(take)
        || take < 1
        || take > 100) {
        throw new Error(invalidMonitorPagingMessage);
    }
}
export async function getOutboxMonitor(skip = 0, take = 25): Promise<OutboxMonitorPage> {
    assertMonitorPaging(skip, take);
    const parameters = new URLSearchParams({
        skip: String(skip),
        take: String(take),
    });
    const response = await fetchWithSafeTransport(apiUrl(`/api/v1/integration-monitor/outbox?${parameters.toString()}`), secureReadInit());
    if (!response.ok) {
        if (response.status === 401) {
            throw new Error('Для просмотра Outbox требуется аутентификация.');
        }
        if (response.status === 403) {
            throw new Error('Недостаточно прав для просмотра Outbox.');
        }
        throw new Error('Не удалось загрузить сервисный монитор Outbox.');
    }
    return publicOutboxMonitorPage(await response.json(), Math.max(skip, 0), Math.min(Math.max(take, 1), 100));
}
export async function getOutboxBacklogSummary(): Promise<OutboxBacklogSummary> {
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/integration-monitor/outbox-summary'), secureReadInit());
    if (!response.ok) {
        if (response.status === 401) {
            throw new Error('Для просмотра сводки Outbox требуется аутентификация.');
        }
        if (response.status === 403) {
            throw new Error('Недостаточно прав для просмотра сводки Outbox.');
        }
        throw new Error('Не удалось загрузить сводку Outbox.');
    }
    return publicOutboxBacklogSummary(await response.json());
}
