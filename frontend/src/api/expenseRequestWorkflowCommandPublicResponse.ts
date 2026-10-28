import { isExpenseRequestIdentifier } from './expenseRequestIdentifier';
export const expenseRequestWorkflowCommandCodes = [
    'APPROVE',
    'RETURN',
    'REJECT',
    'CANCEL',
    'CONFIRM_NSI',
    'SEND_TO_PAYMENT',
] as const;
export type ExpenseRequestWorkflowCommandCode = (typeof expenseRequestWorkflowCommandCodes)[number];
export type ExpenseRequestWorkflowCommandAvailability = {
    code: ExpenseRequestWorkflowCommandCode;
    label: string;
    requiresReason: boolean;
    isEnabled: boolean;
    requiresNsiLinks: boolean;
};
export type ExpenseRequestWorkflowCommandAvailabilityResult = {
    requestVersion: string;
    commands: ExpenseRequestWorkflowCommandAvailability[];
};
export const invalidExpenseRequestWorkflowCommandResponseMessage = 'Некорректный ответ API команд расходной заявки.';
const labels: Record<ExpenseRequestWorkflowCommandCode, ReadonlySet<string>> = {
    APPROVE: new Set(['Согласовать', 'Утвердить']),
    RETURN: new Set(['Вернуть', 'На доработку']),
    REJECT: new Set(['Отклонить']),
    CANCEL: new Set(['Отменить']),
    CONFIRM_NSI: new Set(['Подтвердить НСИ']),
    SEND_TO_PAYMENT: new Set(['В оплату']),
};
const reasons: Record<ExpenseRequestWorkflowCommandCode, boolean> = {
    APPROVE: false,
    RETURN: true,
    REJECT: true,
    CANCEL: true,
    CONFIRM_NSI: false,
    SEND_TO_PAYMENT: false,
};
function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function hasExactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean {
    const actual = Object.keys(value);
    return actual.length === expected.length
        && expected.every((key) => Object.prototype.hasOwnProperty.call(value, key));
}
function isCommandCode(value: unknown): value is ExpenseRequestWorkflowCommandCode {
    return typeof value === 'string'
        && expenseRequestWorkflowCommandCodes.some((code) => code === value);
}
function parseCommand(value: unknown): ExpenseRequestWorkflowCommandAvailability | null {
    if (!isRecord(value)
        || !hasExactKeys(value, [
            'code',
            'label',
            'requiresReason',
            'isEnabled',
            'requiresNsiLinks',
        ])
        || !isCommandCode(value.code)
        || typeof value.label !== 'string'
        || !labels[value.code].has(value.label)
        || typeof value.requiresReason !== 'boolean'
        || value.requiresReason !== reasons[value.code]
        || typeof value.isEnabled !== 'boolean'
        || typeof value.requiresNsiLinks !== 'boolean'
        || value.requiresNsiLinks !== (value.code === 'CONFIRM_NSI')) {
        return null;
    }
    return {
        code: value.code,
        label: value.label,
        requiresReason: value.requiresReason,
        isEnabled: value.isEnabled,
        requiresNsiLinks: value.requiresNsiLinks,
    };
}
export function publicExpenseRequestWorkflowCommandAvailability(payload: unknown): ExpenseRequestWorkflowCommandAvailabilityResult {
    if (!isRecord(payload)
        || !hasExactKeys(payload, ['requestVersion', 'commands'])
        || !isExpenseRequestIdentifier(payload.requestVersion)
        || !Array.isArray(payload.commands)
        || payload.commands.length > expenseRequestWorkflowCommandCodes.length) {
        throw new Error(invalidExpenseRequestWorkflowCommandResponseMessage);
    }
    const commands = payload.commands.map(parseCommand);
    if (commands.some((command) => command === null)) {
        throw new Error(invalidExpenseRequestWorkflowCommandResponseMessage);
    }
    const publicCommands = commands.filter((command): command is ExpenseRequestWorkflowCommandAvailability => command !== null);
    const commandIds = publicCommands.map((command) => command.code);
    if (new Set(commandIds).size !== commandIds.length) {
        throw new Error(invalidExpenseRequestWorkflowCommandResponseMessage);
    }
    let previousOrder = -1;
    for (const command of publicCommands) {
        const order = expenseRequestWorkflowCommandCodes.indexOf(command.code);
        if (order <= previousOrder) {
            throw new Error(invalidExpenseRequestWorkflowCommandResponseMessage);
        }
        previousOrder = order;
    }
    return {
        requestVersion: payload.requestVersion,
        commands: publicCommands,
    };
}
