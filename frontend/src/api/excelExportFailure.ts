const limitMessage = 'Выгрузка превышает допустимый объём или точность Excel. Сузьте фильтры. Исходные данные в BMS не изменены.';
export class ExcelExportLimitError extends Error {
    constructor() { super(limitMessage); this.name = 'ExcelExportLimitError'; }
}
// No exception message, server detail, stack or arbitrary body is published.
export function excelExportFailureMessage(error: unknown, fallback: string): string {
    return error instanceof ExcelExportLimitError ? limitMessage : fallback;
}
