import { BookOpen } from 'lucide-react';
import './ExpenseRequestGuideLink.css';
/** A normal new-tab link never invokes a document command or replaces its form. */
export function ExpenseRequestGuideLink() {
    return <a className="secondary-button expense-guide-link" href="/api/v1/help/expense-requests/guide" target="_blank" rel="noopener noreferrer" aria-label="Инструкция по созданию расходной заявки — PDF, новая вкладка" title="Пошаговая инструкция со скриншотами — PDF, 10 страниц, откроется в новой вкладке">
    <BookOpen size={16} aria-hidden="true"/>Инструкция
  </a>;
}
