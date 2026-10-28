import { RefreshCw, ShieldAlert } from 'lucide-react';
type Props = {
    state: 'checking';
} | {
    state: 'failed';
    onRetry: () => void;
} | {
    state: 'signed-out';
    onRetry: () => void;
};
function LoginHint() {
    return <p className="session-gate-login-hint">Логин: {'AKMT\\имя.пользователя'}</p>;
}
/// Neutral gate rendered instead of any protected page until the current
/// session is confirmed. It never falls back to a guest or administrator
/// session and never renders a raw exception.
export function SessionGatePage(props: Props) {
    if (props.state === 'checking') {
        return (<div className="session-gate">
        <p className="session-gate-status" role="status">Проверка сеанса…</p>
        <LoginHint />
      </div>);
    }
    if (props.state === 'signed-out') {
        return (<div className="session-gate">
        <div className="session-gate-error" role="status">
          <ShieldAlert size={20} aria-hidden="true"/>
          <div>
            <h1>Вы вышли из BMS</h1>
            <p>Рабочая область закрыта. Для Windows/AD вход подтверждается заново при повторной проверке.</p>
          </div>
        </div>
        <LoginHint />
        <button className="secondary-button" type="button" onClick={props.onRetry}>
          <RefreshCw size={15} aria-hidden="true"/>
          <span>Войти снова</span>
        </button>
      </div>);
    }
    return (<div className="session-gate">
      <div className="session-gate-error" role="alert">
        <ShieldAlert size={20} aria-hidden="true"/>
        <div>
          <h1>Не удалось проверить сеанс</h1>
          <p>
            Данные не открыты, потому что сеанс не подтверждён. Повторите проверку
            или обратитесь к администратору BMS.
          </p>
        </div>
      </div>
      <LoginHint />
      <button className="secondary-button" type="button" onClick={props.onRetry}>
        <RefreshCw size={15} aria-hidden="true"/>
        <span>Повторить</span>
      </button>
    </div>);
}
