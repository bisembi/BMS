import type { AccrualDepartmentCatalog485 } from '../../hooks/useAccrualDepartments485';
export function AccrualDepartmentsNotice485({ catalog }: {
    catalog: AccrualDepartmentCatalog485;
}) {
    if (catalog.status === 'idle' || catalog.status === 'ready' && catalog.items.length > 0)
        return null;
    return <div className="notice warning" role="status" aria-label="Загрузка подразделений затрат">
  {catalog.status === 'loading' ? 'Загружаются подразделения затрат…' : <>
   <span>{catalog.status === 'error' ? 'Не удалось загрузить полный список подразделений затрат. Выбор временно недоступен; текущие значения сохранены.' : 'В справочнике нет активных подразделений затрат. Текущие значения сохранены.'}</span>{' '}
   <button type="button" className="secondary-button" onClick={catalog.retry}>Повторить загрузку подразделений</button>
  </>}
 </div>;
}
