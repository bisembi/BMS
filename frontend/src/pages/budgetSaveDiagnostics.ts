import type { BudgetInstanceCostCenter } from '../api/budgetInstances';
import type { ReferenceDataItem } from '../types';
import { canonicalMoneyText } from '../utils/moneyInput';
import type { BudgetDraftLine } from './budgetInstanceMatrix';
const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();
const validMonth = (value: string) => /^(?!0000)\d{4}-(0[1-9]|1[0-2])-01$/u.test(value);
export function budgetSaveRowLabel(line: BudgetDraftLine, index: number, catalogue: readonly ReferenceDataItem[]): string {
    const article = catalogue.find(item => sameId(item.id, line.cashFlowItemId));
    const label = article ? `${article.code} — ${article.name}` : line.cashFlowItemId ? 'статья отсутствует в справочнике' : 'статья не выбрана';
    return `Строка ${index + 1}: ${label}; месяц ${validMonth(line.budgetMonth) ? line.budgetMonth.slice(5, 7) + '.' + line.budgetMonth.slice(0, 4) : 'не указан или некорректен'}`;
}
export function budgetArticleAssignmentReason(line: BudgetDraftLine, catalogue: readonly ReferenceDataItem[], targetId: string | undefined, centers: readonly BudgetInstanceCostCenter[]): string {
    const article = catalogue.find(item => sameId(item.id, line.cashFlowItemId));
    if (!article)
        return 'статья отсутствует в доступном справочнике. Обновите справочник или выберите доступную статью';
    if (!article.costCenterId)
        return 'статья не назначена ЦФО. Исправьте назначение в справочнике или выберите другую статью';
    if (targetId && !sameId(article.costCenterId, targetId)) {
        const owner = centers.find(center => sameId(center.id, article.costCenterId!));
        return `статья назначена ${owner ? `ЦФО «${owner.name}»` : 'другому ЦФО'}, а не ЦФО бюджета. Исправьте назначение в справочнике или выберите статью своего ЦФО`;
    }
    return 'статья не доступна для ЦФО бюджета. Обновите справочник и проверьте активное назначение статьи';
}
/** Explanations use the existing money parser; they must not change accepted amounts or retained legacy rules. */
export function collectBudgetSaveLineIssues(lines: readonly BudgetDraftLine[], period: {
    from: string;
    to: string;
} | null, parseAmount: (raw: string) => number | null, catalogue: readonly ReferenceDataItem[], scope?: {
    targetId: string | undefined;
    allowedIds: readonly string[];
    centers: readonly BudgetInstanceCostCenter[];
}): string[] {
    if (lines.length === 0)
        return ['В бюджете нет строк. Добавьте хотя бы одну строку лимита.'];
    const issues: string[] = [];
    let totalCents = 0;
    let overflowReported = false;
    lines.forEach((line, index) => {
        const reasons: string[] = [];
        if (!line.cashFlowItemId)
            reasons.push('выберите статью ДДС');
        else if (scope && !scope.allowedIds.some(id => sameId(id, line.cashFlowItemId)))
            reasons.push(budgetArticleAssignmentReason(line, catalogue, scope.targetId, scope.centers));
        if (!validMonth(line.budgetMonth))
            reasons.push('укажите корректный месяц');
        else if (period && (line.budgetMonth < period.from || line.budgetMonth > period.to))
            reasons.push('месяц находится вне периода бюджета. Исправьте месяц или период');
        const amount = parseAmount(line.limitAmountKzt);
        if (amount === null) {
            const normalized = canonicalMoneyText(line.limitAmountKzt);
            reasons.push(line.limitAmountKzt.trim() === '' ? 'укажите сумму; для нулевого лимита введите 0'
                : normalized?.startsWith('-') ? 'сумма не может быть отрицательной; в строковом виде введите положительную сумму. Направление новой строки берётся из справочника статьи ДДС'
                    : /^\d+[.,]\d{3,}$/u.test(line.limitAmountKzt.replace(/[ \u00a0\u202f]/gu, '')) ? 'в сумме больше двух знаков после запятой. Укажите сумму с точностью до двух знаков'
                        : normalized && /^\d+(?:\.\d{1,2})?$/u.test(normalized) ? 'сумма слишком велика для точного сохранения тиынов. Уменьшите сумму'
                            : 'сумма имеет некорректный формат. Введите неотрицательное число с точностью до двух знаков');
        }
        else {
            totalCents += Math.round(amount * 100);
            if (!Number.isSafeInteger(totalCents) && !overflowReported) {
                reasons.push('общая сумма строк слишком велика для точного сохранения тиынов. Уменьшите суммы');
                overflowReported = true;
            }
        }
        const comment = line.comment.trim();
        if (comment.length > 2000)
            reasons.push('операция/комментарий превышает 2 000 символов. Сократите текст');
        else if (/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/u.test(comment))
            reasons.push('операция/комментарий содержит недопустимые управляющие символы. Уберите их');
        if (line.flowDirection !== 'Payment' && line.flowDirection !== 'Receipt')
            reasons.push('задайте направление статьи ДДС в справочнике: выплата или поступление');
        if (reasons.length > 0)
            issues.push(`${budgetSaveRowLabel(line, index, catalogue)} — ${reasons.join('; ')}.`);
    });
    return issues;
}
