import { Info } from 'lucide-react';
export function PlaceholderPage({ title }: {
    title: string;
}) {
    return (<div className="placeholder-page">
      <h1>{title}</h1>
      <div className="readonly-placeholder" role="status">
        <Info size={18}/>
        <div>
          <strong>Раздел доступен только для просмотра структуры.</strong>
          <p>Операции и данные этого раздела ещё не подключены к текущему API.</p>
        </div>
      </div>
    </div>);
}
