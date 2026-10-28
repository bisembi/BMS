import { useCallback, useEffect, useRef, useState } from 'react';
import { getBudgetOwnerCostCenterAssignments } from '../api';
import type { BudgetOwnerCostCenterAssignment } from '../types';
const loadError = 'Не удалось загрузить связи владельцев бюджета и ЦФО. Повторите попытку.';
export function useBudgetOwnerCostCenterAssignments() {
    const [items, setItems] = useState<BudgetOwnerCostCenterAssignment[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const requestSequence = useRef(0);
    const reload = useCallback(async () => {
        const requestNumber = requestSequence.current + 1;
        requestSequence.current = requestNumber;
        setLoading(true);
        setError(null);
        try {
            const catalog = await getBudgetOwnerCostCenterAssignments();
            if (requestNumber !== requestSequence.current)
                return;
            setItems(catalog.items);
        }
        catch {
            if (requestNumber !== requestSequence.current)
                return;
            setItems([]);
            setError(loadError);
        }
        finally {
            if (requestNumber === requestSequence.current)
                setLoading(false);
        }
    }, []);
    useEffect(() => {
        void reload();
        return () => {
            requestSequence.current += 1;
        };
    }, [reload]);
    return { items, loading, error, reload };
}
