import { X } from 'lucide-react';
import type { ReactNode } from 'react';
type Props = {
    query: string;
    visibleCount: number;
    codeLabel?: string;
    onQueryChange: (query: string) => void;
    onClear: () => void;
    children?: ReactNode;
};
export function ReferenceDirectorySearchToolbar({ query, visibleCount, codeLabel = 'Код', onQueryChange, onClear, children, }: Props) {
    const searchLabel = codeLabel === 'Префикс' ? 'префиксу' : 'коду';
    return (<div className="directory-toolbar">
      <label className="directory-search">
        <span>Поиск по {searchLabel} и наименованию</span>
        <input type="search" value={query} placeholder={`${codeLabel} или наименование`} onChange={(event) => onQueryChange(event.target.value)}/>
      </label>
      <button className="secondary-button" type="button" disabled={query === ''} onClick={onClear}>
        <X size={15} aria-hidden="true"/>
        <span>Очистить поиск</span>
      </button>
      {children}
      <span className="directory-count" role="status">
        Найдено элементов: {visibleCount}
      </span>
    </div>);
}
