import type { ReactNode } from 'react';
import '../../styles/budget-context-row.css';
/** A stable CFO slot; messages never precede or remount the controlled field. */
export function BudgetContextRow({ center, actions, children }: {
    center?: ReactNode;
    actions?: ReactNode;
    children: ReactNode;
}) {
    return <div className={`budget-context-row${center ? ' has-center' : ''}`} role="group" aria-label="ЦФО и сообщения бюджета">
    {center && <div className="budget-context-center">{center}</div>}
    <div className="budget-context-toolbar">
      {actions}
      <div className="budget-context-messages">{children}</div>
    </div>
  </div>;
}
