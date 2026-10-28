import { useState } from 'react';
import type { ComponentProps } from 'react';
import { canonicalMoneyText, formatEditableMoney, normalizeEditableMoney } from '../../utils/moneyInput';
type Props = Omit<ComponentProps<'input'>, 'value' | 'onChange'> & {
    value: string;
    onValueChange: (value: string) => void;
    formatUnfocused?: boolean;
    normalizeOnChange?: boolean;
    unfocusedFormatter?: (value: string) => string;
};
/** Group only outside editing: inserting a digit must never invalidate a separator we added. */
export function BudgetAmountInput({ value, onValueChange, onFocus, onBlur, formatUnfocused = true, normalizeOnChange = true, unfocusedFormatter, ...props }: Props) {
    const [focused, setFocused] = useState(false);
    const canonical = canonicalMoneyText(value, true);
    const display = focused ? (canonical === null ? value : canonical.replace('.', ','))
        : formatUnfocused ? (unfocusedFormatter ?? formatEditableMoney)(value) : value;
    return <input {...props} value={display} onFocus={event => { setFocused(true); onFocus?.(event); }} onBlur={event => { setFocused(false); onBlur?.(event); }} onChange={event => onValueChange(normalizeOnChange ? normalizeEditableMoney(event.target.value) : event.target.value)}/>;
}
