using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Audit;

public sealed record ExchangeJournalFilter(DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, string Kind, string Status);
public sealed record ExchangeJournalItemDto(Guid Id, string Kind, DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc, string Name, string Status, int? RecordCount, string Description, bool HasDetails);
public sealed record ExchangeFactSummaryDto(DateTimeOffset? CapturedAtUtc, DateTimeOffset? CheckedAtUtc,
    DateOnly? CutoffExclusiveLocal, DateOnly? LatestBusinessDate, int? SourceLegs,
    string? ReceiptsAmountKzt, string? PaymentsAmountKzt, string? NetCashFlowAmountKzt);
public sealed record ExchangeJournalReadPage(IReadOnlyList<ExchangeJournalItemDto> Items, int Total,
    bool CoverageComplete, IReadOnlyList<string> Warnings, ExchangeFactSummaryDto? Fact);
public sealed record ExchangeJournalPageDto(IReadOnlyList<ExchangeJournalItemDto> Items, int Skip, int Take,
    int Total, bool CoverageComplete, IReadOnlyList<string> Warnings, ExchangeFactSummaryDto? Fact);
public sealed record ExchangeRegistryTotalDto(string Label, string AmountKzt);
public sealed record ExchangeJournalDetailDto(string Title, string Information, IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows, int Skip, int Take, int Total,
    IReadOnlyList<ExchangeRegistryTotalDto> Totals);
public sealed class ExchangeJournalEntryNotFoundException : Exception;

public interface IExchangeJournalReader
{
    Task<ExchangeJournalReadPage> ReadAsync(ExchangeJournalFilter filter, int skip, int take, CancellationToken cancellationToken);
    Task<ExchangeJournalDetailDto> ReadDetailAsync(string kind, Guid id, bool includeExcluded, int skip, int take, CancellationToken cancellationToken);
}

public sealed class ExchangeJournalService(IExchangeJournalReader reader, ICurrentActor actor, IClock clock)
{
    private async Task RequireAdministratorAsync(CancellationToken token)
    {
        ActorIdentity.RequireBusinessDataAccess(actor);
        if ((!actor.HasBmsAdministratorAssignment && actor.ActiveRoleCode!="AUDITOR") || actor.IsBmsIntegrationAdministrator) throw new ActorForbiddenException();
        // Administrative assignment is not financial-read authority. Respect the
        // selected business role; never reconstruct the union of assigned roles.
        if (!(await actor.ResolveBusinessDataReadScopeAsync(token)).CanReadAll) throw new ActorForbiddenException();
    }
    private static void Page(int skip, int take, int maxSkip)
    {
        if (skip < 0 || skip > maxSkip || take is not (50 or 100)) throw new ArgumentException("Invalid journal page.");
    }
    public async Task<ExchangeJournalPageDto> ListAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
        string? kind, string? status, int skip, int take, CancellationToken token)
    {
        await RequireAdministratorAsync(token); Page(skip, take, 10000);
        if (fromUtc is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero
            || toUtc is { Offset: var toOffset } && toOffset != TimeSpan.Zero
            || fromUtc is not null && toUtc is not null && fromUtc >= toUtc
            || kind is not (null or "all" or "dds" or "currency" or "references" or "payments")
            || status is not (null or "all" or "success" or "failed" or "running" or "warning"))
            throw new ArgumentException("Invalid journal filter.");
        var result = await reader.ReadAsync(new(fromUtc, toUtc, kind ?? "all", status ?? "all"), skip, take, token);
        var warnings = result.Warnings.ToList();
        var localNow = clock.UtcNow.ToOffset(TimeSpan.FromHours(5));
        var expectedCapture = new DateTimeOffset(localNow.Date.AddHours(7), TimeSpan.FromHours(5));
        if (localNow < expectedCapture) expectedCapture = expectedCapture.AddDays(-1);
        if (result.Fact?.CapturedAtUtc is { } capture
            && capture < expectedCapture)
            warnings.Add("В SQL нет подтверждения актуального ежедневного среза факта. Проверьте результат обмена ДДС; плановое время — 07:00 UTC+5.");
        if (result.Fact is null && result.CoverageComplete)
            warnings.Add("В SQL BMS нет действующего подтверждённого снимка факта ДДС. Пустой журнал не подтверждает успешную загрузку.");
        return new(result.Items, skip, take, result.Total, result.CoverageComplete, warnings.Distinct().ToArray(), result.Fact);
    }
    public async Task<ExchangeJournalDetailDto> DetailAsync(string kind, Guid id, bool includeExcluded,
        int skip, int take, CancellationToken token)
    {
        await RequireAdministratorAsync(token); Page(skip, take, 1000000);
        if (id == Guid.Empty || kind is not ("dds" or "currency" or "references" or "payments"))
            throw new ArgumentException("Invalid journal entry.");
        return await reader.ReadDetailAsync(kind, id, includeExcluded, skip, take, token);
    }
}
