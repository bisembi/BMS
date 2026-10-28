using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.OverpaymentDeficits;

public sealed class ManualDeficitReadService(
    IManualDeficitReader reader, ManualDeficitCorrectionAuthority authority,
    ICurrentActor currentActor, IBusinessTransactionRunner transactionRunner)
{
    public async Task<ManualDeficitPageDto> ListAsync(ManualDeficitQuery query, CancellationToken cancellationToken)
    {
        var actor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        ArgumentNullException.ThrowIfNull(query);
        ValidatePaging(query.Skip, query.Take);
        if (query.OrganizationId == Guid.Empty) throw new InvalidManualDeficitQueryException();
        try
        {
            return await transactionRunner.ExecuteAsync(async token =>
            {
                var access = await authority.ResolveReadAsync(actor, token);
                var result = await reader.ListAsync(access, query, token);
                if (result?.Items is null) throw new ManualDeficitCorrectionUnavailableException();
                ValidatePage(query.Skip, query.Take, result.Total, result.Items.Count);
                var ids = new HashSet<Guid>();
                var items = result.Items.Select(row =>
                {
                    var item = ToDto(row, access);
                    if (!ids.Add(item.Id) || (query.OnlyPending && !item.HasPendingCorrection)
                        || (!query.IncludeSettled && item.CurrentAmount == 0m && !item.HasPendingCorrection)
                        || (query.OrganizationId is { } org && row.Snapshot.OrganizationId != org))
                        throw new ManualDeficitCorrectionUnavailableException();
                    return item;
                }).ToArray();
                return new ManualDeficitPageDto(query.Skip, query.Take, result.Total, items);
            }, cancellationToken);
        }
        catch (Exception error) when (Wrap(error)) { throw new ManualDeficitCorrectionUnavailableException(error); }
    }

    public async Task<ManualDeficitDetailDto> ReadAsync(
        Guid deficitId, int skip, int take, CancellationToken cancellationToken)
    {
        var actor = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (deficitId == Guid.Empty) throw new ManualDeficitCorrectionNotFoundException();
        ValidatePaging(skip, take);
        try
        {
            return await transactionRunner.ExecuteAsync(async token =>
            {
                var access = await authority.ResolveReadAsync(actor, token);
                var result = await reader.ReadAsync(deficitId, access, skip, take, token)
                    ?? throw new ManualDeficitCorrectionNotFoundException();
                var deficit = ToDto(result.Deficit, access);
                if (deficit.Id != deficitId || result.Items is null || result.Total != deficit.CorrectionCount)
                    throw new ManualDeficitCorrectionUnavailableException();
                ValidatePage(skip, take, result.Total, result.Items.Count);
                var ids = new HashSet<Guid>();
                var items = result.Items.Select(snapshot =>
                {
                    if (snapshot?.Proposal is not { } proposal || !ids.Add(proposal.CorrectionId))
                        throw new ManualDeficitCorrectionUnavailableException();
                    ManualDeficitCorrectionService.RequireMatchingContext(
                        new(snapshot, result.Deficit.Snapshot), proposal.CorrectionId);
                    return ManualDeficitCorrectionService.ToDto(snapshot);
                }).ToArray();
                return new ManualDeficitDetailDto(deficit, skip, take, result.Total, items);
            }, cancellationToken);
        }
        catch (Exception error) when (Wrap(error)) { throw new ManualDeficitCorrectionUnavailableException(error); }
    }

    private static ManualDeficitDto ToDto(ManualDeficitReadRow row, ManualDeficitReadAccess access)
    {
        if (row?.Snapshot is not { } data
            || !Text(row.OrganizationName) || !Text(row.CounterpartyName)
            || row.CorrectionCount < 0 || (row.HasPendingCorrection && row.CorrectionCount == 0)
            || (!access.ReadAllOrganizations && !row.OrganizationAllowed))
            throw new ManualDeficitCorrectionUnavailableException();
        ManualDeficitSnapshotValidation.Require(data);
        return new(data.DeficitId, row.OrganizationName, row.CounterpartyName, data.CurrencyCode,
            data.OccurredAtUtc, data.OriginalAmount, data.OriginalAmountKzt,
            data.CurrentAmount, data.CurrentAmountKzt, row.CorrectionCount, row.HasPendingCorrection,
            access.CanPropose && row.OrganizationAllowed && data.CurrentAmount > 0m && !row.HasPendingCorrection,
            access.CanDecide && row.OrganizationAllowed && row.HasPendingCorrection);
    }

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 500
        && value == value.Trim() && !value.Any(char.IsControl);
    private static void ValidatePaging(int skip, int take)
    {
        if (skip < 0 || take is < 1 or > 100 || skip > int.MaxValue - take)
            throw new InvalidManualDeficitQueryException();
    }
    private static void ValidatePage(int skip, int take, int total, int count)
    {
        if (total < 0 || count != Math.Min(take, Math.Max(0, total - skip)))
            throw new ManualDeficitCorrectionUnavailableException();
    }
    private static bool Wrap(Exception error) => !ProcessFatalExceptionClassifier.IsFatal(error)
        && error is not (OperationCanceledException or ActorNotAuthenticatedException or ActorForbiddenException
            or ManualDeficitCorrectionNotFoundException or ManualDeficitCorrectionUnavailableException);
}
