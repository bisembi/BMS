using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreAccrualSnapshotMaterializationWriteSink :
    IAccrualSnapshotMaterializationRuntime
{
    private readonly BmsDbContext dbContext;
    private readonly Func<CancellationToken, Task<int>> saveChangesAsync;
    private readonly Func<bool> hasSerializableTransaction;
    private readonly IAccrualSnapshotSourceResolver resolver;

    internal EfCoreAccrualSnapshotMaterializationWriteSink(BmsDbContext dbContext)
        : this(
            dbContext ?? throw new ArgumentNullException(nameof(dbContext)),
            cancellationToken => dbContext.SaveChangesAsync(cancellationToken),
            () => HasSerializableTransactionCore(dbContext),
            new SqlServerAccrualSnapshotSourceResolver(dbContext))
    {
    }

    internal EfCoreAccrualSnapshotMaterializationWriteSink(
        BmsDbContext dbContext,
        Func<CancellationToken, Task<int>> saveChangesAsync,
        Func<bool> hasSerializableTransaction,
        IAccrualSnapshotSourceResolver resolver)
    {
        this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        this.saveChangesAsync = saveChangesAsync ??
            throw new ArgumentNullException(nameof(saveChangesAsync));
        this.hasSerializableTransaction = hasSerializableTransaction ??
            throw new ArgumentNullException(nameof(hasSerializableTransaction));
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public bool HasSerializableTransaction => hasSerializableTransaction();

    public bool HasPendingChanges => dbContext.ChangeTracker.HasChanges();

    public Task<AccrualSnapshotSourceResolution> ResolveAsync(
        IReadOnlyList<AccrualSnapshotSourceLine> lines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasSerializableTransaction)
        {
            throw new InvalidOperationException(
                "A Serializable transaction is required for accrual snapshot source resolution.");
        }

        return resolver.ResolveAsync(lines, cancellationToken);
    }

    public bool HasExactTrackedAggregate(ExpenseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expectedEntities = request.Lines
            .Cast<object>()
            .Append(request)
            .ToArray();
        var trackedEntries = dbContext.ChangeTracker.Entries().ToArray();
        return trackedEntries.Length == expectedEntities.Length &&
            expectedEntities.All(expected => trackedEntries.Any(entry =>
                ReferenceEquals(entry.Entity, expected))) &&
            trackedEntries.All(entry => expectedEntities.Any(expected =>
                ReferenceEquals(entry.Entity, expected)));
    }

    public async Task<int> SaveAsync(
        ExpenseRequest request,
        Guid expectedOriginalVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasSerializableTransaction ||
            expectedOriginalVersion == Guid.Empty ||
            !HasExactTrackedAggregate(request))
        {
            throw new InvalidOperationException(
                "A clean tracked expense request in a Serializable transaction is required.");
        }

        dbContext.ChangeTracker.DetectChanges();
        EnsureExpectedMutationShape(request, expectedOriginalVersion);
        var affectedEntries = await saveChangesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (HasPendingChanges || !HasExactTrackedAggregate(request))
        {
            throw new InvalidOperationException(
                "The accrual snapshot save did not leave the aggregate unchanged in the tracker.");
        }

        return affectedEntries;
    }

    private static bool HasSerializableTransactionCore(BmsDbContext dbContext)
    {
        var transaction = dbContext.Database.CurrentTransaction;
        return transaction is not null &&
            transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable;
    }

    private void EnsureExpectedMutationShape(
        ExpenseRequest request,
        Guid expectedOriginalVersion)
    {
        var requestEntry = FindTrackedEntry(request);
        if (requestEntry.State != EntityState.Modified ||
            requestEntry.Property(nameof(ExpenseRequest.ConcurrencyStamp)).OriginalValue
                is not Guid originalVersion ||
            originalVersion != expectedOriginalVersion ||
            request.ConcurrencyStamp == expectedOriginalVersion ||
            !HasExactModifiedProperties(
                requestEntry,
                nameof(ExpenseRequest.ConcurrencyStamp)))
        {
            throw new InvalidOperationException(
                "The tracked expense request mutation does not match the accrual snapshot contract.");
        }

        foreach (var line in request.Lines)
        {
            var lineEntry = FindTrackedEntry(line);
            var expectedProperties = line.AccrualSnapshotFormsManagerialAccrual is true
                ? new[]
                {
                    nameof(ExpenseRequestLine.AccrualSnapshotFormsManagerialAccrual),
                    nameof(ExpenseRequestLine.AccrualSnapshotExpenseItemId)
                }
                : new[]
                {
                    nameof(ExpenseRequestLine.AccrualSnapshotFormsManagerialAccrual)
                };
            if (lineEntry.State != EntityState.Modified ||
                !HasExactModifiedProperties(lineEntry, expectedProperties))
            {
                throw new InvalidOperationException(
                    "A tracked expense request line mutation does not match the accrual snapshot contract.");
            }
        }
    }

    private EntityEntry FindTrackedEntry(object entity) =>
        dbContext.ChangeTracker.Entries().Single(entry => ReferenceEquals(entry.Entity, entity));

    private static bool HasExactModifiedProperties(
        EntityEntry entry,
        params string[] expectedPropertyNames)
    {
        var actualPropertyNames = entry.Properties
            .Where(property => property.IsModified)
            .Select(property => property.Metadata.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return actualPropertyNames.SequenceEqual(
            expectedPropertyNames.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }
}
