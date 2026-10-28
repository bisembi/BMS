// Bounded numeric navigation: never render thousands of buttons for a large area.
export function budgetPageNumbers(current: number, total: number): Array<number | null> {
    if (!Number.isSafeInteger(total) || total < 1 || !Number.isSafeInteger(current))
        return [];
    const selected = Math.min(Math.max(current, 1), total);
    const numbers = total <= 7
        ? Array.from({ length: total }, (_, index) => index + 1)
        : [...new Set([1, total, ...Array.from({ length: 5 }, (_, index) => selected - 2 + index)
                    .filter((number) => number >= 1 && number <= total)])].sort((a, b) => a - b);
    const result: Array<number | null> = [];
    numbers.forEach((number, index) => {
        if (index > 0 && number - numbers[index - 1] > 1)
            result.push(null);
        result.push(number);
    });
    return result;
}
