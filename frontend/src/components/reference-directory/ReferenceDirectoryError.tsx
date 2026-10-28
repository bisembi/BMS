import { RefreshCw } from 'lucide-react';
type Props = {
    /// Only a fixed, safe message is accepted; server text never reaches here.
    message: string;
    onRetry: () => void;
};
export function ReferenceDirectoryError({ message, onRetry }: Props) {
    return (<div className="directory-error" role="alert">
      <p>{message}</p>
      <button className="secondary-button" type="button" onClick={onRetry}>
        <RefreshCw size={15} aria-hidden="true"/>
        <span>Повторить</span>
      </button>
    </div>);
}
