import { useMemo, useState } from 'react';
import { filterHelpTopics, HELP_TOPICS } from './helpContent';
import { ExpenseRequestGuideLink } from '../components/ExpenseRequestGuideLink';
import { BudgetGuideLink } from '../components/BudgetGuideLink';
import './HelpPage.css';
export function HelpPage() {
    const [query, setQuery] = useState('');
    const [selectedId, setSelectedId] = useState(HELP_TOPICS[0].questions[0].id);
    const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set(['about']));
    const topics = useMemo(() => filterHelpTopics(query), [query]);
    const searching = query.trim() !== '';
    const questions = topics.flatMap(topic => topic.questions);
    const selected = questions.find(item => item.id === selectedId) ?? questions[0];
    const selectedTopic = topics.find(topic => topic.questions.some(item => item.id === selected?.id));
    function toggleTopic(id: string) {
        setExpanded(current => {
            const next = new Set(current);
            if (next.has(id))
                next.delete(id);
            else
                next.add(id);
            return next;
        });
    }
    return <section className="help-page" aria-labelledby="bms-help-heading">
    <h1 id="bms-help-heading">Помощь</h1>
    <p className="help-intro">Выберите вопрос в дереве тем или найдите ответ по ключевым словам.</p>
    <section className="help-expense-guide" aria-label="Инструкция по созданию расходной заявки">
      <div><strong>Создание расходной заявки</strong>
        <p>Пошаговая инструкция со скриншотами и стрелками. PDF · 10 страниц.</p></div>
      <ExpenseRequestGuideLink />
    </section>
    <section className="help-expense-guide" aria-label="Инструкция по заполнению бюджета">
      <div><strong>Заполнение бюджета на 2026 год</strong>
        <p>Факт за январь–сентябрь и план на октябрь–декабрь. Скриншоты и стрелки. PDF · 6 страниц.</p></div>
      <BudgetGuideLink />
    </section>
    <div className="help-search">
      <label htmlFor="bms-help-search">Поиск по вопросам и ответам</label>
      <div className="help-search-controls">
        <input id="bms-help-search" type="search" value={query} placeholder="Например: F11, ЦФО, скопировать факт" onChange={event => setQuery(event.target.value)}/>
        <button type="button" className="secondary-button" disabled={!query} onClick={() => setQuery('')}>Очистить поиск</button>
      </div>
      {searching && <p className="help-search-status" role="status">Найдено вопросов: {questions.length}</p>}
    </div>
    <div className="help-layout">
      <nav className="help-tree" aria-label="Дерево вопросов помощи">
        <ul>
          {topics.map(topic => {
            const open = searching || expanded.has(topic.id);
            const label = <><span aria-hidden="true">{open ? '▾' : '▸'}</span>{topic.title}</>;
            return <li key={topic.id} className="help-topic">
              {searching ? <div className="help-topic-label">{label}</div>
                    : <button type="button" className="help-topic-toggle" aria-expanded={open} aria-controls={`help-topic-${topic.id}`} onClick={() => toggleTopic(topic.id)}>{label}</button>}
              <ul id={`help-topic-${topic.id}`} hidden={!open}>
                {topic.questions.map(item => <li key={item.id}>
                  <button type="button" className="help-question" aria-current={selected?.id === item.id ? 'page' : undefined} onClick={() => {
                        setSelectedId(item.id);
                        setExpanded(current => new Set([...current, topic.id]));
                    }}>{item.question}</button>
                </li>)}
              </ul>
            </li>;
        })}
        </ul>
        {questions.length === 0 && <p className="help-empty">Ничего не найдено. Попробуйте другое слово или очистите поиск.</p>}
      </nav>
      <article className="help-answer" aria-labelledby={selected ? 'help-answer-heading' : undefined}>
        {selected ? <>
          <p className="help-answer-topic">{selectedTopic?.title}</p>
          <h2 id="help-answer-heading">{selected.question}</h2>
          {selected.answer.map((paragraph, index) => <p key={index}>{paragraph}</p>)}
        </> : <p>Для просмотра тем очистите поиск.</p>}
      </article>
    </div>
  </section>;
}
