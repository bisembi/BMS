import { getCsrfToken } from '../api/csrfToken';
import { fetchWithSafeTransport, secureWriteInit } from '../api/http';
export type UiFailureKind = 'render' | 'runtime' | 'rejection' | 'resource' | 'import';
const assetPattern = /^[A-Za-z0-9_-]{1,120}-[A-Za-z0-9_-]{8}\.(?:js|css)$/u;
const errorNames = new Set(['Error', 'TypeError', 'RangeError', 'ReferenceError', 'SyntaxError', 'ChunkLoadError']);
const reported = new Set<string>();
let minute = 0, sent = 0, installed = false;
/** Deliberately excludes query strings, document URLs and arbitrary messages. */
export function diagnosticAsset(value: unknown): string | null {
    if (typeof value !== 'string' || value.length > 1024)
        return null;
    try {
        const url = new URL(value, window.location.href);
        if (url.origin !== window.location.origin || url.search || url.hash || url.username || url.password)
            return null;
        if (!url.pathname.startsWith('/assets/'))
            return null;
        const name = url.pathname.slice('/assets/'.length);
        return assetPattern.test(name) ? name : null;
    }
    catch {
        return null;
    }
}
export function diagnosticFrame(error: unknown): {
    asset: string | null;
    line: number;
    column: number;
} {
    if (error instanceof Error && typeof error.stack === 'string') {
        // Bound work and retain only a compiled same-origin filename and offsets.
        for (const match of error.stack.slice(0, 8192).matchAll(/(https?:\/\/[^\s()]+):(\d+):(\d+)/gu)) {
            const asset = diagnosticAsset(match[1]);
            const line = Number(match[2]), column = Number(match[3]);
            if (asset && line > 0 && line <= 10000000 && column > 0 && column <= 10000000)
                return { asset, line, column };
        }
    }
    return { asset: null, line: 0, column: 0 };
}
/** Best effort, no retries, no storage, no error text, identity or form values. */
export function reportUiFailure(kind: UiFailureKind, error?: unknown, resource?: unknown): string | null {
    try {
        if (!getCsrfToken())
            return null;
        const build = diagnosticAsset(document.querySelector<HTMLScriptElement>('script[type="module"][src]')?.src);
        if (!build)
            return null;
        const frame = diagnosticFrame(error);
        const asset = diagnosticAsset(resource) ?? frame.asset;
        const errorType = error instanceof Error && errorNames.has(error.name) ? error.name : 'Unknown';
        const key = `${kind}:${build}:${asset}:${frame.line}:${frame.column}:${errorType}`;
        const now = Math.floor(Date.now() / 60000);
        if (minute !== now) {
            minute = now;
            sent = 0;
        }
        if (sent >= 5 || reported.size >= 100 || reported.has(key))
            return null;
        const incidentId = crypto.randomUUID();
        const init = secureWriteInit('POST', { 'Content-Type': 'application/json' }, JSON.stringify({
            incidentId, kind, build, asset, line: frame.line, column: frame.column, errorType,
        }));
        reported.add(key);
        sent += 1;
        void fetchWithSafeTransport('/api/v1/client-diagnostics', { ...init, redirect: 'error' }).catch(() => { });
        return incidentId;
    }
    catch {
        return null;
    }
}
export function installUiDiagnostics(): void {
    if (installed)
        return;
    installed = true;
    window.addEventListener('error', event => {
        const target = event.target;
        if (target instanceof HTMLScriptElement)
            reportUiFailure('resource', undefined, target.src);
        else if (target instanceof HTMLLinkElement)
            reportUiFailure('resource', undefined, target.href);
        else
            reportUiFailure('runtime', event instanceof ErrorEvent ? event.error : undefined);
    }, true);
    window.addEventListener('unhandledrejection', event => reportUiFailure('rejection', event.reason));
    window.addEventListener('vite:preloadError', event => reportUiFailure('import', (event as Event & {
        payload?: unknown;
    }).payload));
}
