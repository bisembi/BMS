using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;

namespace Bms.Application.ExpenseRequests;

public sealed record ExpenseRequestExportData(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ExpenseRequestExportRow> Rows);

public sealed class ExpenseRequestExportService(
    IExpenseRequestExportRepository repository,
    ICurrentActor currentActor,
    IClock clock,
    IBusinessTransactionRunner transactionRunner,
    IEmployeeActorDisplayNamesReader? displayNamesReader = null)
{
    public async Task<ExpenseRequestExportData> ExportAsync(
        ExpenseRequestFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        filter.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var includeAll = currentActor.HasFullBusinessDataAccess;

        // The repository reads headers, lines, projections and budget positions
        // separately. Capture them in one retryable read transaction; workbook
        // construction happens only after the transaction has been released.
        return await transactionRunner.ExecuteAsync(
            async attemptCancellationToken =>
            {
                var rows = await repository.ListVisibleAsync(
                    actorExternalId,
                    includeAll,
                    filter,
                    attemptCancellationToken);
                attemptCancellationToken.ThrowIfCancellationRequested();
                var captured = rows.ToArray();
                if (displayNamesReader is not null)
                    captured = await WithInitiatorNamesAsync(captured, displayNamesReader, attemptCancellationToken);
                return new ExpenseRequestExportData(clock.UtcNow, captured);
            },
            cancellationToken);
    }

    private static async Task<ExpenseRequestExportRow[]> WithInitiatorNamesAsync(
        ExpenseRequestExportRow[] visibleRows,
        IEmployeeActorDisplayNamesReader reader,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        // Only actors from the already-authorized export. The shared presentation
        // reader accepts at most 100 distinct actors, even for a larger export.
        var actors = visibleRows.Select(row => row.InitiatorExternalId)
            .Where(ActorIdentity.IsCanonical).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var batch in actors.Chunk(100))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<EmployeeActorMappingProjectionRow> mappings;
            try { mappings = await reader.ReadAsync(batch, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { continue; } // A missing optional name never exposes the raw actor identity.
            cancellationToken.ThrowIfCancellationRequested();
            var byActor = mappings.ToLookup(row => row.ActorExternalId, StringComparer.Ordinal);
            foreach (var actor in batch)
            {
                var matches = byActor[actor].ToArray();
                if (matches.Length != 1) continue;
                var mapping = matches[0];
                var name = mapping.EmployeeName?.Trim();
                if (mapping.EmployeeId != Guid.Empty && mapping.MappingIsActive && mapping.EmployeeIsActive
                    && !string.IsNullOrWhiteSpace(name) && name.Length <= 256
                    && !name.Any(char.IsControl) && !name.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
                    names.Add(actor, name);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return visibleRows.Select(row => row with
        {
            InitiatorDisplayName = names.GetValueOrDefault(row.InitiatorExternalId)
        }).ToArray();
    }
}

internal static class ExpenseRequestCreatedPeriodFilter
{
    internal static void Validate(
        DateTimeOffset? createdFromUtc,
        DateTimeOffset? createdBeforeUtc)
    {
        if (createdFromUtc is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Expense-request created-from filter must be UTC.",
                nameof(createdFromUtc));
        }
        if (createdBeforeUtc is { Offset: var beforeOffset } && beforeOffset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Expense-request created-before filter must be UTC.",
                nameof(createdBeforeUtc));
        }
        if (createdFromUtc.HasValue &&
            createdBeforeUtc.HasValue &&
            createdFromUtc.Value >= createdBeforeUtc.Value)
        {
            throw new ArgumentException(
                "Expense-request created period must have a start before its exclusive end.",
                nameof(createdBeforeUtc));
        }
    }
}
