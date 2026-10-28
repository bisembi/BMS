import { readBoundedPublicCode } from './expenseDraftSaveProblems';
export const newContractRequiresSaveCode = 'expense-attachment-new-contract-requires-save';
export const newContractRequiresSaveMessage = 'Сначала сохраните черновик с отметкой «Новый договор», затем повторите загрузку договора. Выбранный файл и введённые данные оставлены в форме.';
export function readExpenseAttachmentCode(response: Response) {
    return readBoundedPublicCode(response, (value): value is typeof newContractRequiresSaveCode => value === newContractRequiresSaveCode);
}
