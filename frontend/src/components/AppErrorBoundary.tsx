import { Component, type ErrorInfo, type ReactNode } from 'react';
import { reportUiFailure } from '../diagnostics/uiDiagnostics';
/// W259: shared by the root fallback and each isolated workspace page.
///
/// Without one, a throw anywhere below the root unmounts the whole React tree
/// and the user is left on a blank page with nothing to act on. The boundary
/// replaces that with a fixed notice and a way out.
///
/// The Error never enters state or the UI. Diagnostics extracts only known
/// compiled asset names and numeric offsets, never arbitrary exception text.
/// The only extra value shown is a newly generated incident UUID.
const boundaryMessage = 'Произошла непредвиденная ошибка интерфейса. Обновите страницу и повторите действие.';
const reloadLabel = 'Обновить страницу';
type AppErrorBoundaryProps = {
    children: ReactNode;
};
type AppErrorBoundaryState = {
    hasError: boolean;
    diagnosticId?: string;
};
export class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
    constructor(props: AppErrorBoundaryProps) {
        super(props);
        this.state = { hasError: false };
    }
    /// Records that a render failed and nothing about why.
    static getDerivedStateFromError(): AppErrorBoundaryState {
        return { hasError: true };
    }
    /// The diagnostics boundary strips all free text before transmission.
    componentDidCatch(_error: Error, _errorInfo: ErrorInfo): void {
        const diagnosticId = reportUiFailure('render', _error);
        if (diagnosticId)
            this.setState({ diagnosticId });
    }
    render(): ReactNode {
        if (!this.state.hasError) {
            return this.props.children;
        }
        return (<div className="notice error" role="alert">
        <p>{boundaryMessage}</p>
        {this.state.diagnosticId && <p>Код диагностики: {this.state.diagnosticId}</p>}
        <button type="button" onClick={() => window.location.reload()}>
          {reloadLabel}
        </button>
      </div>);
    }
}
