import { apiUrl, fetchWithSafeTransport, secureReadInit } from './http';
import { publicApprovalMatrix, type ApprovalMatrix } from './approvalMatrixPublicResponse';
export const approvalMatrixUnavailableMessage = 'Не удалось загрузить матрицу согласования. Попробуйте ещё раз.';
export async function getApprovalMatrix(signal?: AbortSignal, version = 1): Promise<ApprovalMatrix> {
    signal?.throwIfAborted();
    const response = await fetchWithSafeTransport(apiUrl('/api/v1/reference-data/approval-matrix' + (version === 2 ? '/initiators' : '')), {
        ...secureReadInit(), signal,
    });
    signal?.throwIfAborted();
    if (!response.ok) {
        throw new Error(approvalMatrixUnavailableMessage);
    }
    try {
        const matrix = publicApprovalMatrix(await response.json());
        signal?.throwIfAborted();
        return matrix;
    }
    catch {
        signal?.throwIfAborted();
        throw new Error(approvalMatrixUnavailableMessage);
    }
}
