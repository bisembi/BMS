import type { ReferenceDataItem } from '../../types';
import { sameOpaqueIdentifier } from '../../utils/expenseRequestDraft';
import { CashFlowItemCombobox } from '../reference-directory/CashFlowItemCombobox';
import { matchesWildcardFragment } from '../../utils/matchesWildcardFragment';
const normalize = (text: string) => text.normalize('NFKC').toLocaleLowerCase('ru-RU').replace(/\s+/gu, ' ').trim();
// Bounded dynamic programming, not a user-created regular expression. Stars
// match any fragment, question marks one Unicode character; ordinary text is
// an unanchored fragment. Each space-separated term must occur in the name.
export function matchesCounterpartyNameMask(item: ReferenceDataItem, query: string): boolean {
    const text = normalize(query.slice(0, 100));
    return !text || text.split(' ').every(token => matchesWildcardFragment(normalize(item.name), token));
}
const nameOnly = (item: ReferenceDataItem) => item.name;
export function CounterpartyCombobox({ value, items, onChange }: {
    value: string;
    items: readonly ReferenceDataItem[];
    onChange: (id: string) => void;
}) {
    return <CashFlowItemCombobox ariaLabel="Контрагент" required value={value} items={items} emptyLabel="Не выбран" unavailableLabel="Выбранный контрагент недоступен в справочнике" matchesItem={matchesCounterpartyNameMask} formatItem={nameOnly} searchPlaceholder="Название или маска * ?" noMatchesLabel="Контрагенты не найдены" searchSelectsFirstMatch onChange={next => onChange(sameOpaqueIdentifier(next, value) ? value : next)}/>;
}
