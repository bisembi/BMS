import { useEffect, useRef, useState } from 'react';
import { cashFlowOrderError, getCashFlowOrder, isCashFlowOrderUncertain, moveCashFlowOrder, type CashFlowOrder, type CashFlowOrderMove } from '../api/cashFlowOrder';
import { useFrozenCreate } from './useFrozenCreate';
/** Server owns global order. Only anchor IDs, never classifications, are submitted. */
export function useCashFlowOrder(lock: () => boolean, unlock: () => void) {
    const [active, setActive] = useState(false);
    const [snapshot, setSnapshot] = useState<CashFlowOrder | null>(null);
    const [loading, setLoading] = useState(false);
    const [message, setMessage] = useState<string | null>(null);
    const frozen = useFrozenCreate<CashFlowOrderMove>('cash-flow-order', isCashFlowOrderUncertain);
    const alive = useRef(true), sequence = useRef(0);
    const last = useRef<CashFlowOrderMove | null>(null);
    const inFlight = useRef(false);
    const loadingNow = useRef(false);
    useEffect(() => { alive.current = true; return () => { alive.current = false; sequence.current += 1; }; }, []);
    async function load() {
        if (frozen.blocked || inFlight.current || last.current || loadingNow.current)
            return;
        const token = ++sequence.current;
        loadingNow.current = true;
        setLoading(true);
        setMessage(null);
        try {
            const data = await getCashFlowOrder();
            if (!alive.current || token !== sequence.current)
                return;
            if (!data.canEdit) {
                setMessage('Недостаточно прав для изменения порядка.');
                setActive(false);
                return;
            }
            setSnapshot(data);
            setActive(true);
        }
        catch {
            if (alive.current && token === sequence.current)
                setMessage('Не удалось загрузить структуру. Повторите загрузку.');
        }
        finally {
            if (token === sequence.current) {
                loadingNow.current = false;
                if (alive.current)
                    setLoading(false);
            }
        }
    }
    async function run(body: CashFlowOrderMove) {
        if (inFlight.current || loadingNow.current || frozen.busy || !lock())
            return;
        inFlight.current = true;
        const result = await frozen.run(body, moveCashFlowOrder);
        inFlight.current = false;
        if (!alive.current)
            return;
        if (result.kind === 'confirmed') {
            setSnapshot(result.value);
            last.current = null;
            setMessage('Порядок сохранён.');
            unlock();
        }
        else if (result.kind === 'uncertain') {
            last.current = body;
            setMessage(cashFlowOrderError(result.error));
        }
        else {
            last.current = null;
            if (result.kind === 'refused')
                setMessage(cashFlowOrderError(result.error));
            unlock();
        }
    }
    async function move(sourceId: string, targetId: string, placement: 'Before' | 'After') {
        if (!snapshot?.canEdit || loadingNow.current || loading || frozen.blocked || inFlight.current || last.current || sourceId === targetId)
            return;
        const source = snapshot.items.find(item => item.id === sourceId), target = snapshot.items.find(item => item.id === targetId);
        if (!source || !target) {
            setMessage('Статья не найдена в текущей структуре. Обновите структуру.');
            return;
        }
        await run({ expectedVersion: snapshot.version, sourceId, targetId, placement });
    }
    async function moveToPosition(sourceId: string, position: number) {
        if (!snapshot?.canEdit || loadingNow.current || loading || frozen.blocked || inFlight.current || last.current)
            return;
        if (!Number.isInteger(position) || position < 1 || position > Math.min(999, snapshot.items.length)) {
            setMessage(`Введите номер от 1 до ${Math.min(999, snapshot.items.length)}.`);
            return;
        }
        const sourceIndex = snapshot.items.findIndex(item => item.id === sourceId);
        if (sourceIndex < 0) {
            setMessage('Статья не найдена в текущей структуре. Обновите структуру.');
            return;
        }
        const targetIndex = position - 1;
        if (sourceIndex === targetIndex)
            return;
        // Use the full catalogue. Removing the source shifts a later anchor left.
        await move(sourceId, snapshot.items[targetIndex].id, sourceIndex < targetIndex ? 'After' : 'Before');
    }
    function finish() {
        if (frozen.blocked || loadingNow.current || inFlight.current || last.current)
            return false;
        setActive(false);
        setSnapshot(null);
        setMessage(null);
        return true;
    }
    return { active, snapshot, loading, message, blocked: frozen.blocked, busy: frozen.busy, unknown: frozen.unknown,
        load, move, moveToPosition, finish, retry: () => {
            if (last.current && !frozen.busy)
                void run(last.current);
        } };
}
