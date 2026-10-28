import { Suspense, type ReactNode } from 'react';
import { AppErrorBoundary } from './AppErrorBoundary';
export function WorkspacePageBoundary({ children }: {
    children: ReactNode;
}) {
    return (<AppErrorBoundary>
      <Suspense fallback={<p className="notice" role="status">Загрузка раздела…</p>}>
        {children}
      </Suspense>
    </AppErrorBoundary>);
}
