export class ReferenceDataMaintenanceApiError extends Error {
    constructor(readonly status: number, readonly code?: 'csrf-token-invalid') {
        super('Не удалось изменить справочник.');
    }
}
export function isReferenceDataCreateUncertain(error: unknown): boolean {
    return !(error instanceof ReferenceDataMaintenanceApiError) || ![400, 401, 403, 404, 409].includes(error.status);
}
