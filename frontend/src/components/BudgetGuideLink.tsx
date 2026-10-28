import { BookOpen } from 'lucide-react';
import './ExpenseRequestGuideLink.css';
export function BudgetGuideLink() {
    return <a className="secondary-button expense-guide-link" href="/api/v1/help/budget-instances/guide" target="_blank" rel="noopener noreferrer" aria-label="Инструкция по заполнению бюджета — PDF, новая вкладка" title="Пошаговая инструкция со скриншотами — PDF, 6 страниц, откроется в новой вкладке">
    <BookOpen size={16} aria-hidden="true"/>Инструкция
  </a>;
}
