import type { ExpenseRequestDraft } from '../types';
import { isValidPositiveMoneyInput } from '../api/expenseRequestCalculations';
export function expenseDraftInputIssue(draft: ExpenseRequestDraft, creating: boolean): {
    tab: 'main' | 'lines';
    text: string;
} | null {
    if (creating && draft.defaultCostCenterId.trim() === '')
        return { tab: 'main', text: 'Выберите ЦФО на вкладке «Основное» — он нужен для присвоения номера заявки.' };
    const index = draft.lines.findIndex(line => line.amount.trim() !== '' && !isValidPositiveMoneyInput(line.amount));
    return index < 0 ? null : { tab: 'lines', text: `Строка ${index + 1}: исправьте сумму. Допустимо положительное число с не более чем двумя знаками после запятой; например, 1 234 567,89.` };
}
