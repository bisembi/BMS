using System.Data;
using Bms.Application.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreNsiReferenceProjectionWriteSink :
    INsiReferenceProjectionWriteSink
{
    private readonly BmsDbContext dbContext;
    private readonly Func<CancellationToken, Task<int>> saveChangesAsync;
    private readonly Func<bool> hasSerializableTransaction;

    internal EfCoreNsiReferenceProjectionWriteSink(BmsDbContext dbContext)
        : this(
            dbContext ?? throw new ArgumentNullException(nameof(dbContext)),
            cancellationToken => dbContext.SaveChangesAsync(cancellationToken),
            () => HasSerializableTransactionCore(dbContext))
    {
    }

    internal EfCoreNsiReferenceProjectionWriteSink(
        BmsDbContext dbContext,
        Func<CancellationToken, Task<int>> saveChangesAsync,
        Func<bool> hasSerializableTransaction)
    {
        this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        this.saveChangesAsync = saveChangesAsync ??
            throw new ArgumentNullException(nameof(saveChangesAsync));
        this.hasSerializableTransaction = hasSerializableTransaction ??
            throw new ArgumentNullException(nameof(hasSerializableTransaction));
    }

    public bool HasSerializableTransaction => hasSerializableTransaction();

    public bool HasPendingChanges => dbContext.ChangeTracker.HasChanges();

    public bool HasTrackedProjectionEntities =>
        dbContext.ChangeTracker.Entries<CounterpartyReference>().Any() ||
        dbContext.ChangeTracker.Entries<ContractReference>().Any();

    public async Task<int> VacateCounterpartyCodesAsync(
        IReadOnlyList<NsiCounterpartyCodeVacate> vacates,
        NsiReferenceProjectionReadModel current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vacates);
        ArgumentNullException.ThrowIfNull(current);
        EnsurePhaseReady(cancellationToken);
        var currentById = current.Counterparties.ToDictionary(item => item.Id);

        foreach (var vacate in vacates)
        {
            if (FindTrackedCounterparty(vacate.Id) is not null)
            {
                throw new InvalidOperationException(
                    "A counterparty write target is already tracked.");
            }

            var entity = AttachCounterparty(currentById[vacate.Id]);
            entity.Code = vacate.TemporaryCode;
        }

        return await SavePhaseAsync(cancellationToken);
    }

    public async Task<int> WriteCounterpartiesAsync(
        IReadOnlyList<NsiCounterpartyImportAssignment> writes,
        IReadOnlyList<NsiCounterpartyCodeVacate> vacates,
        NsiReferenceProjectionReadModel current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(vacates);
        ArgumentNullException.ThrowIfNull(current);
        EnsurePhaseReady(cancellationToken);
        var currentById = current.Counterparties.ToDictionary(item => item.Id);
        var vacateById = vacates.ToDictionary(item => item.Id);

        foreach (var write in writes)
        {
            CounterpartyReference entity;
            if (write.Operation is NsiReferenceImportOperation.Insert)
            {
                if (currentById.ContainsKey(write.Item.Id) ||
                    FindTrackedCounterparty(write.Item.Id) is not null)
                {
                    throw new InvalidOperationException(
                        "A counterparty insert target already exists in the current projection.");
                }

                entity = new CounterpartyReference { Id = write.Item.Id };
                dbContext.Counterparties.Add(entity);
            }
            else if (write.Operation is NsiReferenceImportOperation.Update)
            {
                var currentItem = currentById[write.Item.Id];
                if (vacateById.TryGetValue(write.Item.Id, out var vacate))
                {
                    entity = FindTrackedCounterparty(write.Item.Id) ??
                        throw new InvalidOperationException(
                            "A vacated counterparty target is not tracked.");
                    EnsureCounterpartyState(entity, currentItem, vacate.TemporaryCode);
                }
                else
                {
                    if (FindTrackedCounterparty(write.Item.Id) is not null)
                    {
                        throw new InvalidOperationException(
                            "A counterparty update target is unexpectedly tracked.");
                    }

                    entity = AttachCounterparty(currentItem);
                }
            }
            else
            {
                throw new InvalidOperationException(
                    "Only insert and update counterparty writes can be persisted.");
            }

            entity.Code = write.Item.Code;
            entity.Name = write.Item.Name;
            entity.IsActive = write.Item.IsActive;
        }

        return await SavePhaseAsync(cancellationToken);
    }

    public async Task<int> VacateContractCodesAsync(
        IReadOnlyList<NsiContractCodeVacate> vacates,
        NsiReferenceProjectionReadModel current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vacates);
        ArgumentNullException.ThrowIfNull(current);
        EnsurePhaseReady(cancellationToken);
        var currentById = current.Contracts.ToDictionary(item => item.Id);

        foreach (var vacate in vacates)
        {
            if (FindTrackedContract(vacate.Id) is not null)
            {
                throw new InvalidOperationException(
                    "A contract write target is already tracked.");
            }

            var entity = AttachContract(currentById[vacate.Id]);
            entity.Code = vacate.TemporaryCode;
        }

        return await SavePhaseAsync(cancellationToken);
    }

    public async Task<int> WriteContractsAsync(
        IReadOnlyList<NsiContractImportAssignment> writes,
        IReadOnlyList<NsiContractCodeVacate> vacates,
        NsiReferenceProjectionReadModel current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(vacates);
        ArgumentNullException.ThrowIfNull(current);
        EnsurePhaseReady(cancellationToken);
        var currentById = current.Contracts.ToDictionary(item => item.Id);
        var vacateById = vacates.ToDictionary(item => item.Id);

        foreach (var write in writes)
        {
            ContractReference entity;
            if (write.Operation is NsiReferenceImportOperation.Insert)
            {
                if (currentById.ContainsKey(write.Item.Id) ||
                    FindTrackedContract(write.Item.Id) is not null)
                {
                    throw new InvalidOperationException(
                        "A contract insert target already exists in the current projection.");
                }

                entity = new ContractReference { Id = write.Item.Id };
                dbContext.Contracts.Add(entity);
            }
            else if (write.Operation is NsiReferenceImportOperation.Update)
            {
                var currentItem = currentById[write.Item.Id];
                if (vacateById.TryGetValue(write.Item.Id, out var vacate))
                {
                    entity = FindTrackedContract(write.Item.Id) ??
                        throw new InvalidOperationException(
                            "A vacated contract target is not tracked.");
                    EnsureContractState(entity, currentItem, vacate.TemporaryCode);
                }
                else
                {
                    if (FindTrackedContract(write.Item.Id) is not null)
                    {
                        throw new InvalidOperationException(
                            "A contract update target is unexpectedly tracked.");
                    }

                    entity = AttachContract(currentItem);
                }
            }
            else
            {
                throw new InvalidOperationException(
                    "Only insert and update contract writes can be persisted.");
            }

            entity.CounterpartyId = write.Item.CounterpartyId;
            entity.OrganizationId = write.Item.OrganizationId;
            entity.Code = write.Item.Code;
            entity.Name = write.Item.Name;
            entity.ContractDate = write.Item.ContractDate;
            entity.CurrencyId = write.Item.CurrencyId;
            entity.IsActive = write.Item.IsActive;
        }

        return await SavePhaseAsync(cancellationToken);
    }

    private static bool HasSerializableTransactionCore(BmsDbContext dbContext)
    {
        var transaction = dbContext.Database.CurrentTransaction;
        return transaction is not null &&
            transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable;
    }

    private void EnsurePhaseReady(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasSerializableTransaction)
        {
            throw new InvalidOperationException(
                "A Serializable transaction is required for an NSI projection write.");
        }
        if (HasPendingChanges)
        {
            throw new InvalidOperationException(
                "An NSI projection write phase requires a clean change tracker.");
        }
    }

    private async Task<int> SavePhaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var affectedRows = await saveChangesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (HasPendingChanges)
        {
            throw new InvalidOperationException(
                "The NSI projection write phase did not clear all pending changes.");
        }

        return affectedRows;
    }

    private CounterpartyReference AttachCounterparty(NsiCounterpartyImportItem item)
    {
        var entity = new CounterpartyReference
        {
            Id = item.Id,
            Code = item.Code,
            Name = item.Name,
            IsActive = item.IsActive
        };
        dbContext.Counterparties.Attach(entity);
        return entity;
    }

    private ContractReference AttachContract(NsiContractImportItem item)
    {
        var entity = new ContractReference
        {
            Id = item.Id,
            CounterpartyId = item.CounterpartyId,
            OrganizationId = item.OrganizationId,
            Code = item.Code,
            Name = item.Name,
            ContractDate = item.ContractDate,
            CurrencyId = item.CurrencyId,
            IsActive = item.IsActive
        };
        dbContext.Contracts.Attach(entity);
        return entity;
    }

    private CounterpartyReference? FindTrackedCounterparty(Guid id) =>
        dbContext.Counterparties.Local.SingleOrDefault(item => item.Id == id);

    private ContractReference? FindTrackedContract(Guid id) =>
        dbContext.Contracts.Local.SingleOrDefault(item => item.Id == id);

    private static void EnsureCounterpartyState(
        CounterpartyReference entity,
        NsiCounterpartyImportItem current,
        string expectedCode)
    {
        if (entity.Id != current.Id ||
            !string.Equals(entity.Code, expectedCode, StringComparison.Ordinal) ||
            !string.Equals(entity.Name, current.Name, StringComparison.Ordinal) ||
            entity.IsActive != current.IsActive)
        {
            throw new InvalidOperationException(
                "A tracked counterparty does not match the current projection.");
        }
    }

    private static void EnsureContractState(
        ContractReference entity,
        NsiContractImportItem current,
        string expectedCode)
    {
        if (entity.Id != current.Id ||
            entity.CounterpartyId != current.CounterpartyId ||
            entity.OrganizationId != current.OrganizationId ||
            !string.Equals(entity.Code, expectedCode, StringComparison.Ordinal) ||
            !string.Equals(entity.Name, current.Name, StringComparison.Ordinal) ||
            entity.ContractDate != current.ContractDate ||
            entity.CurrencyId != current.CurrencyId ||
            entity.IsActive != current.IsActive)
        {
            throw new InvalidOperationException(
                "A tracked contract does not match the current projection.");
        }
    }
}
