import type { BudgetFlowDirection } from '../api/budgetInstances';
import { canonicalMoneyText } from '../utils/moneyInput';
import { CASH_FLOW_ACTIVITY_TYPES, type ReferenceDataItem } from '../types';
import { catalogueRank, minimumCatalogueRank } from '../utils/catalogueDisplayOrder';
export type BudgetDraftLine = {
    cashFlowItemId: string;
    budgetMonth: string;
    limitAmountKzt: string;
    comment: string;
    flowDirection: BudgetFlowDirection;
};
export type BudgetMatrixRow = {
    key: string;
    kind: 'activity' | 'group' | 'article';
    label: string;
    articleIds: string[];
};
const matrixValidationMessages = {
    catalogDirectionMissing: 'Укажите направление статьи в справочнике ДДС перед использованием.',
    unavailableArticle: 'Статья больше недоступна. Перезагрузите справочник перед сохранением.',
    invalidAmount: 'Введите сумму с точностью до двух знаков. Направление новой операции задано в справочнике ДДС.',
    invalidExistingAmount: 'Сначала исправьте некорректную сумму в строковом виде.',
    excessiveSum: 'Сумма слишком велика.',
    longComments: 'Комментарии ячейки превышают 2 000 символов. Измените суммы в строковом виде, чтобы сохранить комментарии.',
} as const;
export class BudgetMatrixInputError extends Error {
    constructor(readonly code: keyof typeof matrixValidationMessages) { super(matrixValidationMessages[code]); }
}
/** Only local typed validation codes may become visible text. Never expose a rejection's message. */
export function budgetMatrixErrorMessage(failure: unknown): string {
    return failure instanceof BudgetMatrixInputError ? matrixValidationMessages[failure.code]
        : 'Не удалось подготовить ячейки бюджета.';
}
const idKey = (id: string) => id.toLowerCase();
const monthPattern = /^(?!0000)\d{4}-(0[1-9]|1[0-2])$/u;
/** Integer cents only. Display rounding is never used to calculate or save a cell. */
export function signedBudgetCents(raw: string): number | null {
    const text = canonicalMoneyText(raw);
    if (text === null || !/^-?\d+(?:\.\d{1,2})?$/u.test(text))
        return null;
    const negative = text.startsWith('-');
    const [whole, fraction = ''] = (negative ? text.slice(1) : text).split('.');
    const cents = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
    if (cents > BigInt(Number.MAX_SAFE_INTEGER))
        return null;
    return Number(cents) * (negative ? -1 : 1);
}
export function budgetCentsText(cents: number): string {
    const absolute = Math.abs(cents);
    const fraction = String(absolute % 100).padStart(2, '0').replace(/0+$/u, '');
    return `${cents < 0 ? '-' : ''}${Math.floor(absolute / 100)}${fraction ? `.${fraction}` : ''}`;
}
export function budgetMatrixCellKey(articleId: string, month: string): string {
    return `${idKey(articleId)}\u0000${month}`;
}
// Technical display budget, aligned with the existing XLSX column limit.
// This does not restrict or rewrite the document's financial validity period.
export const MAX_BUDGET_MATRIX_MONTHS = 120;
export const MAX_BUDGET_MATRIX_CELLS = 50000;
/** Future columns are virtual: they do not manufacture zero lines or extend a saved period. */
export function budgetMatrixMonths(fromMonth: string, throughMonth = fromMonth): string[] {
    if (!monthPattern.test(fromMonth) || !monthPattern.test(throughMonth) || throughMonth < fromMonth)
        return [];
    const firstYear = Number(fromMonth.slice(0, 4));
    const finalYear = Number(throughMonth.slice(0, 4));
    const count = (finalYear - firstYear) * 12 + 13 - Number(fromMonth.slice(5, 7));
    // Check before allocating: hostile persisted periods must remain safe to read.
    if (count > MAX_BUDGET_MATRIX_MONTHS)
        return [];
    const result: string[] = [];
    for (let year = firstYear; year <= finalYear; year += 1) {
        for (let month = year === firstYear ? Number(fromMonth.slice(5, 7)) : 1; month <= 12; month += 1) {
            result.push(`${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-01`);
        }
    }
    return result;
}
const publicName = (name: string) => name.replace(/^CF-\d+\s*[·—-]\s*/iu, '').trim();
/** Current shared catalogue supplies classification; amounts come only from this document. */
export function budgetMatrixRows(items: readonly ReferenceDataItem[], lines: readonly BudgetDraftLine[]): BudgetMatrixRow[] {
    const byId = new Map(items.map(item => [idKey(item.id), item]));
    for (const line of lines)
        if (line.cashFlowItemId && !byId.has(idKey(line.cashFlowItemId))) {
            byId.set(idKey(line.cashFlowItemId), {
                id: line.cashFlowItemId, code: '', name: 'Статья отсутствует в справочнике',
            });
        }
    const activities = [...CASH_FLOW_ACTIVITY_TYPES.map(item => item.value), 'Unclassified'];
    const rows: BudgetMatrixRow[] = [];
    for (const activity of activities) {
        const articles = [...byId.values()].filter(item => (item.activityType ?? 'Unclassified') === activity);
        if (articles.length === 0)
            continue;
        const activityLabel = CASH_FLOW_ACTIVITY_TYPES.find(item => item.value === activity)?.label ?? 'Без вида деятельности';
        rows.push({ key: activity, kind: 'activity', label: activityLabel.toLocaleUpperCase('ru-RU'),
            articleIds: articles.map(item => item.id) });
        const groups = new Map<string, ReferenceDataItem[]>();
        for (const article of articles) {
            const group = publicName(article.parentName ?? '')
                || (article.parentId ? 'Группа отсутствует в справочнике' : 'Без группы');
            groups.set(group, [...(groups.get(group) ?? []), article]);
        }
        for (const [group, members] of [...groups].sort(([left, leftMembers], [right, rightMembers]) => minimumCatalogueRank(leftMembers) - minimumCatalogueRank(rightMembers) || left.localeCompare(right, 'ru-RU'))) {
            rows.push({ key: `${activity}\u0000${group}`, kind: 'group', label: group.toLocaleUpperCase('ru-RU'),
                articleIds: members.map(item => item.id) });
            for (const article of members.sort((left, right) => catalogueRank(left) - catalogueRank(right)
                || left.name.localeCompare(right.name, 'ru-RU'))) {
                rows.push({ key: idKey(article.id), kind: 'article', label: publicName(article.name), articleIds: [article.id] });
            }
        }
    }
    return rows;
}
export function budgetMatrixAmount(lines: readonly BudgetDraftLine[], articleIds: readonly string[], months: readonly string[]): {
    cents: number | null;
    invalid: boolean;
} {
    const ids = new Set(articleIds.map(idKey));
    const selectedMonths = new Set(months);
    let cents = 0;
    let present = false;
    for (const line of lines) {
        if (!ids.has(idKey(line.cashFlowItemId)) || !selectedMonths.has(line.budgetMonth))
            continue;
        const amount = signedBudgetCents(line.limitAmountKzt);
        if (amount === null || amount < 0)
            return { cents: null, invalid: true };
        cents += line.flowDirection === 'Receipt' ? amount : -amount;
        if (!Number.isSafeInteger(cents))
            return { cents: null, invalid: true };
        present = true;
    }
    return { cents: present ? cents : null, invalid: false };
}
/** Local display projection only. Does not approve a replacement of either underlying direction. */
export function previewBudgetMatrix(lines: readonly BudgetDraftLine[], drafts: Readonly<Record<string, string | null>>): BudgetDraftLine[] {
    const keys = new Set(Object.keys(drafts));
    const result = lines.filter(line => !keys.has(budgetMatrixCellKey(line.cashFlowItemId, line.budgetMonth)));
    for (const [key, raw] of Object.entries(drafts)) {
        if (raw === null)
            continue;
        const [cashFlowItemId, budgetMonth] = key.split('\u0000');
        const cents = signedBudgetCents(raw);
        result.push({ cashFlowItemId, budgetMonth, limitAmountKzt: cents === null ? '' : budgetCentsText(Math.abs(cents)),
            flowDirection: cents !== null && cents > 0 ? 'Receipt' : 'Payment', comment: '' });
    }
    return result;
}
function joinedComments(lines: readonly BudgetDraftLine[]): string {
    const comment = [...new Set(lines.map(line => line.comment).filter(value => value.trim() !== ''))].join('\n');
    if (comment.length > 2000)
        throw new BudgetMatrixInputError('longComments');
    return comment;
}
/** Only an explicitly edited cell is transformed. Viewing a net amount never removes gross limits. */
export function replaceBudgetMatrixCell(lines: readonly BudgetDraftLine[], articleId: string, month: string, raw: string, strategy: 'replace' | 'preserveCounterflow' | 'manualReplace'): BudgetDraftLine[] {
    const cents = signedBudgetCents(raw);
    if (cents === null || !monthPattern.test(month.slice(0, 7)) || !month.endsWith('-01')) {
        throw new BudgetMatrixInputError('invalidAmount');
    }
    const matches = lines.filter(line => idKey(line.cashFlowItemId) === idKey(articleId) && line.budgetMonth === month);
    const previous = budgetMatrixAmount(matches, [articleId], [month]);
    if (strategy !== 'replace' && !previous.invalid && previous.cents === cents)
        return [...lines];
    let pairedCents = 0;
    if (strategy === 'preserveCounterflow' && matches.length > 1) {
        let receipt = 0;
        let payment = 0;
        for (const line of matches) {
            const value = signedBudgetCents(line.limitAmountKzt);
            if (value === null || value < 0)
                throw new BudgetMatrixInputError('invalidExistingAmount');
            if (line.flowDirection === 'Receipt')
                receipt += value;
            else
                payment += value;
            if (!Number.isSafeInteger(receipt) || !Number.isSafeInteger(payment))
                throw new BudgetMatrixInputError('excessiveSum');
        }
        pairedCents = Math.min(receipt, payment);
    }
    const comment = strategy === 'replace' ? '' : joinedComments(matches);
    const replacement: BudgetDraftLine[] = [];
    const add = (value: number, flowDirection: BudgetFlowDirection) => {
        if (!Number.isSafeInteger(value))
            throw new BudgetMatrixInputError('excessiveSum');
        replacement.push({ cashFlowItemId: articleId, budgetMonth: month, limitAmountKzt: budgetCentsText(value),
            comment, flowDirection });
    };
    if (pairedCents > 0) {
        add(pairedCents + Math.max(cents, 0), 'Receipt');
        add(pairedCents + Math.max(-cents, 0), 'Payment');
    }
    else
        add(Math.abs(cents), cents > 0 ? 'Receipt' : 'Payment');
    const first = lines.findIndex(line => matches.includes(line));
    const remaining = lines.filter(line => !matches.includes(line));
    remaining.splice(first < 0 ? remaining.length : first, 0, ...replacement);
    return remaining;
}
