type Props = {
    skip: number;
    count: number;
    total: number;
    disabled?: boolean;
    onChange: (skip: number) => void;
    label: string;
};
export function ManualDeficitPagination({ skip, count, total, disabled, onChange, label }: Props) {
    return <nav className="manual-deficit-pagination" aria-label={label}>
    <button type="button" disabled={disabled || skip === 0} onClick={() => onChange(Math.max(0, skip - 25))}>Назад</button>
    <span>{count ? `${skip + 1}–${skip + count} из ${total}` : `Записей: ${total}`}</span>
    <button type="button" disabled={disabled || skip + count >= total} onClick={() => onChange(skip + 25)}>Далее</button>
  </nav>;
}
