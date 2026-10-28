import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { AppErrorBoundary } from './components/AppErrorBoundary';
import { installUiDiagnostics } from './diagnostics/uiDiagnostics';
import './styles.css';
import './styles/navigation.css';
import './styles/list.css';
import './styles/directories.css';
import './styles/session.css';
import './styles/closing.css';
import './styles/bms-theme.css';
import './styles/bdds.css';
import './styles/public-neutral.css';
const rootElement = document.getElementById('root');
installUiDiagnostics();
if (!rootElement) {
    throw new Error('Корневой элемент приложения не найден.');
}
createRoot(rootElement).render(<StrictMode>
    <AppErrorBoundary>
      <App />
    </AppErrorBoundary>
  </StrictMode>);
