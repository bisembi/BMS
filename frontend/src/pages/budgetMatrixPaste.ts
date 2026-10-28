import type { ReferenceDataItem } from '../types';
import { budgetMatrixAmount, budgetMatrixCellKey, budgetMatrixRows, signedBudgetCents, type BudgetDraftLine } from './budgetInstanceMatrix';
/** null means an intentional paste-clear, never an unfilled manual amount. */
export type BudgetMatrixDrafts = Readonly<Record<string, string | null>>;
export type BudgetPasteCell = {
    articleId: string;
    articleLabel: string;
    month: string;
    raw: string | null;
    beforeCents: number | null;
    beforeInvalid: boolean;
    afterCents: number | null;
    operationCount: number;
    hasComments: boolean;
    mixed: boolean;
};
export type BudgetPastePlan = {
    cells: readonly BudgetPasteCell[];
    rowCount: number;
    columnCount: number;
};
/** Freeze every catalogue field that decides the displayed targets or their direction. */
export function budgetPasteScopeKey(available: readonly ReferenceDataItem[], months: readonly string[], items: readonly ReferenceDataItem[]) {
    return JSON.stringify([available.map(item => item.id.toLowerCase()).sort(), months,
        items.map(item => [item.id, item.name, item.parentName, item.activityType, item.flowDirection, item.displayOrder])]);
}
const messages = {
    size: 'Можно вставить не более 5 000 ячеек. Разделите блок на несколько частей.',
    shape: 'Скопируйте прямоугольный блок только сумм, без заголовков.',
    bounds: 'Блок выходит за последнюю статью или месяц таблицы. Вставка не выполнена.',
    article: 'В блоке есть недоступная статья ДДС. Обновите справочник; вставка не выполнена.',
    amount: 'В блоке есть неверная сумма. Допустимы числа с точностью до двух знаков, ноль и пустые ячейки.',
} as const;
export class BudgetPasteError extends Error {
    constructor(readonly code: keyof typeof messages) { super(messages[code]); }
}
export function budgetPasteErrorMessage(error: unknown): string {
    return error instanceof BudgetPasteError ? messages[error.code] : 'Не удалось подготовить вставку. Данные формы не изменены.';
}
/** TSV clipboard only. One terminal line ending belongs to Excel, not to an additional row. */
export function prepareBudgetMatrixPaste(text: string, articleId: string, month: string, items: readonly ReferenceDataItem[], availableIds: readonly string[], months: readonly string[], lines: readonly BudgetDraftLine[], originalLines: readonly BudgetDraftLine[] = lines): BudgetPastePlan {
    if (text.length > 1000000)
        throw new BudgetPasteError('size');
    let normalized = text.replace(/\r\n?/gu, '\n');
    if (normalized.endsWith('\n'))
        normalized = normalized.slice(0, -1);
    const rows = normalized.split('\n').map(row => row.split('\t'));
    const columns = rows[0].length;
    if (rows.length * columns > 5000)
        throw new BudgetPasteError('size');
    if (rows.some(row => row.length !== columns))
        throw new BudgetPasteError('shape');
    const articles = budgetMatrixRows(items, lines).filter(row => row.kind === 'article');
    const startRow = articles.findIndex(row => row.articleIds[0].toLowerCase() === articleId.toLowerCase());
    const startMonth = months.indexOf(month);
    if (startRow < 0 || startMonth < 0 || startRow + rows.length > articles.length
        || startMonth + columns > months.length)
        throw new BudgetPasteError('bounds');
    const available = new Set(availableIds.map(id => id.toLowerCase()));
    const cells: BudgetPasteCell[] = [];
    rows.forEach((row, rowIndex) => row.forEach((raw, columnIndex) => {
        const article = articles[startRow + rowIndex];
        const id = article.articleIds[0];
        if (!available.has(id.toLowerCase()))
            throw new BudgetPasteError('article');
        const targetMonth = months[startMonth + columnIndex];
        const cleared = raw.trim() === '';
        // Keep malformed/oversized text away from BigInt without ever silently truncating it.
        const cents = cleared ? null : raw.length > 1000 ? null : signedBudgetCents(raw);
        if (!cleared && cents === null)
            throw new BudgetPasteError('amount');
        const before = budgetMatrixAmount(lines, [id], [targetMonth]);
        const source = originalLines.filter(line => line.cashFlowItemId.toLowerCase() === id.toLowerCase()
            && line.budgetMonth === targetMonth);
        cells.push({ articleId: id, articleLabel: article.label, month: targetMonth, raw: cleared ? null : raw,
            beforeCents: before.cents, beforeInvalid: before.invalid, afterCents: cents,
            operationCount: source.length, hasComments: source.some(line => line.comment.trim() !== ''),
            mixed: source.some(line => line.flowDirection === 'Receipt') && source.some(line => line.flowDirection === 'Payment') });
    }));
    return { cells, rowCount: rows.length, columnCount: columns };
}
export function pasteBudgetMatrixDrafts(drafts: BudgetMatrixDrafts, plan: BudgetPastePlan): Record<string, string | null> {
    const next = { ...drafts };
    for (const cell of plan.cells)
        next[budgetMatrixCellKey(cell.articleId, cell.month)] = cell.raw;
    return next;
}
