using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>At most four batched reads for a full-card-visible page, not N+1 requests.
/// This reader does not redact raw database failures; its caller owns the optional
/// column error boundary and must choose the retry semantics.</summary>
public sealed class ExpenseRequestListBusinessColumnsReader(BmsDbContext dbContext)
    : IExpenseRequestListBusinessColumnsReader
{
    public async Task<IReadOnlyDictionary<Guid, ExpenseRequestListBusinessColumnsDto>> ReadAsync(
        IReadOnlyList<ExpenseRequest> requests, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable ||
            requests.Count > 100 || requests.Select(item => item.Id).Distinct().Count() != requests.Count)
            throw new ExpenseRequestListBusinessColumnsUnavailableException();
        if (requests.Count == 0) return new Dictionary<Guid, ExpenseRequestListBusinessColumnsDto>();

        var ids = requests.Select(item => item.Id).ToArray();
        var currencyCodes = requests.Select(item => item.CurrencyCode).Distinct().ToArray();
        var currencies = await dbContext.Currencies.AsNoTracking()
            .Where(item => currencyCodes.Contains(item.Code))
            .Select(item => new { item.Id, item.Code }).ToArrayAsync(cancellationToken);
        var currencyIds = currencies.ToDictionary(item => item.Code, item => item.Id, StringComparer.Ordinal);
        var contractIds = requests.Where(item => item.ContractId.HasValue)
            .Select(item => item.ContractId.GetValueOrDefault()).Distinct().ToArray();
        var contracts = contractIds.Length == 0 ? [] : await dbContext.Contracts.AsNoTracking()
            .Where(item => contractIds.Contains(item.Id)).ToArrayAsync(cancellationToken);
        var contractsById = contracts.ToDictionary(item => item.Id);
        // Capture ALL currencies; a contradictory row must not disappear through a filter.
        var facts = await dbContext.ExpenseRequestPaymentFacts.AsNoTracking()
            .Where(item => ids.Contains(item.ExpenseRequestId)).ToArrayAsync(cancellationToken);
        var events = facts.Select(row => new ConfirmedPaymentEvent(row.SourceSystem, row.ExternalId,
            row.EventId, row.ExpenseRequestId, row.CurrencyId, row.Amount, row.AmountKzt,
            row.PaidAt, row.PaymentNumber, row.IsReversal, row.ReversedEventId))
            .ToLookup(item => item.ExpenseRequestId);
        // The target controls visibility, never the source request. A left join
        // makes a missing source a refused capture rather than a missing amount.
        var allocationRows = await (
            from allocation in dbContext.OverpaymentAllocations.AsNoTracking()
            where ids.Contains(allocation.TargetExpenseRequestId)
            join source in dbContext.OverpaymentSources.AsNoTracking()
                on allocation.SourceId equals source.SourceId into sources
            from source in sources.DefaultIfEmpty()
            select new
            {
                allocation.AllocationId,
                allocation.TargetExpenseRequestId,
                OrganizationId = (Guid?)source.OrganizationId,
                CounterpartyId = (Guid?)source.CounterpartyId,
                CurrencyId = (Guid?)source.CurrencyId,
                allocation.Amount,
                allocation.AmountKzt,
                allocation.Status,
                allocation.ReservedAt,
                allocation.AppliedOrReleasedAt
            }).ToArrayAsync(cancellationToken);
        var allocations = allocationRows.Select(row => new ExpenseRequestAppliedOverpaymentSnapshot(
            row.AllocationId, row.TargetExpenseRequestId, row.OrganizationId.GetValueOrDefault(),
            row.CounterpartyId.GetValueOrDefault(), row.CurrencyId.GetValueOrDefault(),
            row.Amount, row.AmountKzt, row.Status, row.ReservedAt, row.AppliedOrReleasedAt))
            .ToLookup(item => item.TargetExpenseRequestId);

        var approved400 = await ApprovedAccountingEvidence400.ReadAsync(dbContext, cancellationToken);
        var result = new Dictionary<Guid, ExpenseRequestListBusinessColumnsDto>();
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!currencyIds.TryGetValue(request.CurrencyCode, out var currencyId))
                throw new ExpenseRequestListBusinessColumnsUnavailableException();
            string? contractName = null;
            if (request.ContractId is { } contractId)
            {
                if (!contractsById.TryGetValue(contractId, out var contract) ||
                    (request.OrganizationId.HasValue && contract.OrganizationId != request.OrganizationId) ||
                    (request.CounterpartyId.HasValue && contract.CounterpartyId != request.CounterpartyId) || contract.CurrencyId != currencyId)
                {
                    // A legacy inconsistent draft must remain discoverable and
                    // repairable, not take every valid row off the page. Never
                    // relax financial-state or payment-ledger validation.
                    if (!request.Status.IsEditableDraft())
                        throw new ExpenseRequestListBusinessColumnsUnavailableException();
                    contractName = "Договор не соответствует реквизитам заявки";
                }
                else contractName = contract.Name;
            }
            else if (request.IsNewContract)
            {
                contractName = request.CandidateContractNumber is { Length: > 0 } number
                    ? $"Новый договор: {number}" : "Новый договор (без номера)";
            }
            result.Add(request.Id, ExpenseRequestListBusinessColumnsProjector.Project(
                request, currencyId, contractName, events[request.Id].ToArray(), allocations[request.Id].ToArray(),
                ApprovedAccountingEvidence400.Rounding(approved400, request.Id)));
        }
        return result;
    }
}
