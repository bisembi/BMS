using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Audit;

public sealed class ChangeJournalService(IChangeJournalReader reader, ICurrentActor currentActor)
{
    public async Task<ChangeJournalPageDto> ListAsync(
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? category,
        int skip, int take, CancellationToken cancellationToken)
    {
        AdministrationReportReadAccess.Require(currentActor);

        if (skip < 0 || take is < 1 or > 500
            || fromUtc?.Offset != null && fromUtc.Value.Offset != TimeSpan.Zero
            || toUtc?.Offset != null && toUtc.Value.Offset != TimeSpan.Zero
            || fromUtc is not null && toUtc is not null && fromUtc >= toUtc
            || category is not null && category is not
                ("references" or "employees" or "budgets" or "expenses" or "accruals" or "other"))
            throw new ArgumentException("Invalid change-journal filter.");

        var page = await reader.ReadAsync(fromUtc, toUtc, category, skip, take, cancellationToken);
        return new ChangeJournalPageDto(
            page.Items.Select(ChangeJournalFormatter.Format).ToArray(), skip, take, page.Total);
    }
}
