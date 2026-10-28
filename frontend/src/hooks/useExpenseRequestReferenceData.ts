import { getCounterparties, getCurrencies, getOrganizations } from '../api';
import { useExpenseRequestContracts } from './useExpenseRequestContracts';
import { useExpenseRequestCostCenters } from './useExpenseRequestCostCenters';
import { useExpenseRequestPaymentDictionaries } from './useExpenseRequestPaymentDictionaries';
import { useCashFlowItemOptions } from './useCashFlowItemOptions';
import { useReferenceCatalog } from './useReferenceCatalog';
import { useReferenceCatalogError } from './useReferenceCatalogError';
import type { ReferenceCodeItem, ReferenceDataItem } from '../types';
/// Only the draft fields that actually drive a catalogue request. The hook
/// never receives the whole draft.
export type ExpenseRequestReferenceScope = {
    organizationId: string;
    currencyCode?: string;
    counterpartyId: string;
    plannedPaymentDate: string;
    paymentBasisCode: string;
    isNewCounterparty: boolean;
    isNewContract: boolean;
    cashFlowItemIds?: readonly string[];
    costCenterId?: string;
};
export type ExpenseRequestReferenceData = {
    organizations: ReferenceDataItem[];
    costCenters: ReferenceDataItem[];
    cashFlowItems: ReferenceDataItem[];
    cashFlowItemLabels: ReferenceDataItem[];
    counterparties: ReferenceDataItem[];
    contracts: ReferenceDataItem[];
    currencies: ReferenceDataItem[];
    paymentBases: ReferenceCodeItem[];
    counterpartyResidencies: ReferenceCodeItem[];
    cashFlowItemQuery: string;
    setCashFlowItemQuery: (query: string) => void;
    printReferenceDataLoading: boolean;
    referenceError: string | null;
};
/// Facade over the independent catalogue readers of the expense request form.
/// The readers are called in the same order as the effects they replaced, so
/// the requests, their dependencies and their cancellation semantics are
/// unchanged; only the ownership of each catalogue moved into its own hook.
export function useExpenseRequestReferenceData(scope: ExpenseRequestReferenceScope): ExpenseRequestReferenceData {
    const { referenceError, reportError } = useReferenceCatalogError();
    const organizationCatalog = useReferenceCatalog(getOrganizations, 'Не удалось загрузить организации.', reportError);
    const cashFlowItems = useCashFlowItemOptions(scope.cashFlowItemIds ?? [], 0, true, scope.costCenterId);
    const counterpartyCatalog = useReferenceCatalog(getCounterparties, 'Не удалось загрузить контрагентов.', reportError);
    const currencyCatalog = useReferenceCatalog(getCurrencies, 'Не удалось загрузить валюты.', reportError);
    const contracts = useExpenseRequestContracts(scope, reportError);
    const paymentDictionaries = useExpenseRequestPaymentDictionaries(reportError);
    const costCenterCatalog = useExpenseRequestCostCenters(scope.plannedPaymentDate, reportError);
    return {
        organizations: organizationCatalog.items,
        costCenters: costCenterCatalog.items,
        cashFlowItems: cashFlowItems.items,
        cashFlowItemLabels: cashFlowItems.selectedItems ?? [],
        counterparties: counterpartyCatalog.items,
        contracts,
        currencies: currencyCatalog.items,
        paymentBases: paymentDictionaries.paymentBases,
        counterpartyResidencies: paymentDictionaries.counterpartyResidencies,
        cashFlowItemQuery: cashFlowItems.query,
        setCashFlowItemQuery: cashFlowItems.setQuery,
        printReferenceDataLoading: organizationCatalog.loading
            || costCenterCatalog.loading
            || cashFlowItems.loading
            || counterpartyCatalog.loading
            || paymentDictionaries.loading,
        referenceError: referenceError ?? cashFlowItems.error,
    };
}
