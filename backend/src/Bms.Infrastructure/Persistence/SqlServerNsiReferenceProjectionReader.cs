using System.Collections.ObjectModel;
using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class SqlServerNsiReferenceProjectionReader : INsiReferenceProjectionReader
{
    private readonly INsiReferenceProjectionReadRuntime runtime;

    public SqlServerNsiReferenceProjectionReader(BmsDbContext dbContext)
        : this(new EfCoreNsiReferenceProjectionReadRuntime(dbContext))
    {
    }

    internal SqlServerNsiReferenceProjectionReader(INsiReferenceProjectionReadRuntime runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<NsiReferenceProjectionReadModel> ReadAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!runtime.HasSerializableTransaction)
            {
                throw new NsiReferenceProjectionUnavailableException();
            }

            var rows = await runtime.ReadAsync(cancellationToken);
            ArgumentNullException.ThrowIfNull(rows);
            cancellationToken.ThrowIfCancellationRequested();
            return NsiReferenceProjectionReadModel.Create(
                rows.Counterparties.Select(row => NsiCounterpartyImportItem.Create(
                    row.Id,
                    row.Code,
                    row.Name,
                    row.IsActive)),
                rows.Contracts.Select(row => NsiContractImportItem.Create(
                    row.Id,
                    row.CounterpartyId,
                    row.OrganizationId,
                    row.Code,
                    row.Name,
                    row.ContractDate,
                    row.CurrencyId,
                    row.IsActive)),
                rows.OrganizationIds,
                rows.CurrencyIds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NsiReferenceProjectionUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new NsiReferenceProjectionUnavailableException(exception);
        }
    }
}

internal interface INsiReferenceProjectionReadRuntime
{
    bool HasSerializableTransaction { get; }

    Task<NsiReferenceProjectionRows> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class EfCoreNsiReferenceProjectionReadRuntime(BmsDbContext dbContext) :
    INsiReferenceProjectionReadRuntime
{
    private readonly BmsDbContext dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public bool HasSerializableTransaction
    {
        get
        {
            var transaction = dbContext.Database.CurrentTransaction;
            return transaction is not null &&
                transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable;
        }
    }

    public async Task<NsiReferenceProjectionRows> ReadAsync(
        CancellationToken cancellationToken)
    {
        if (!HasSerializableTransaction)
        {
            throw new InvalidOperationException(
                "A Serializable transaction is required for the current NSI projection.");
        }

        var counterparties = await dbContext.Counterparties
            .FromSqlRaw(NsiReferenceProjectionLockSql.Counterparties)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var organizationIds = await dbContext.Organizations
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var currencyIds = await dbContext.Currencies
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var contracts = await dbContext.Contracts
            .FromSqlRaw(NsiReferenceProjectionLockSql.Contracts)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new NsiReferenceProjectionRows(
            counterparties.Select(item => new NsiCounterpartyProjectionRow(
                item.Id,
                item.Code,
                item.Name,
                item.IsActive)),
            contracts.Select(item => new NsiContractProjectionRow(
                item.Id,
                item.CounterpartyId,
                item.OrganizationId,
                item.Code,
                item.Name,
                item.ContractDate,
                item.CurrencyId,
                item.IsActive)),
            organizationIds,
            currencyIds);
    }
}

internal static class NsiReferenceProjectionLockSql
{
    internal const string Counterparties = """
        SELECT [Id], [Code], [Name], [Bin], [IsActive]
        FROM [bms_web].[Counterparties] WITH (UPDLOCK, HOLDLOCK)
        ORDER BY [Id] ASC
        """;

    internal const string Contracts = """
        SELECT [Id], [CounterpartyId], [OrganizationId], [Code], [Name],
               [ContractDate], [CurrencyId], [IsActive]
        FROM [bms_web].[Contracts] WITH (UPDLOCK, HOLDLOCK)
        ORDER BY [Id] ASC
        """;
}

internal sealed class NsiReferenceProjectionRows
{
    internal NsiReferenceProjectionRows(
        IEnumerable<NsiCounterpartyProjectionRow> counterparties,
        IEnumerable<NsiContractProjectionRow> contracts,
        IEnumerable<Guid> organizationIds,
        IEnumerable<Guid> currencyIds)
    {
        ArgumentNullException.ThrowIfNull(counterparties);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(organizationIds);
        ArgumentNullException.ThrowIfNull(currencyIds);
        Counterparties = new ReadOnlyCollection<NsiCounterpartyProjectionRow>(
            counterparties.ToArray());
        Contracts = new ReadOnlyCollection<NsiContractProjectionRow>(contracts.ToArray());
        OrganizationIds = new ReadOnlyCollection<Guid>(organizationIds.ToArray());
        CurrencyIds = new ReadOnlyCollection<Guid>(currencyIds.ToArray());
    }

    internal IReadOnlyList<NsiCounterpartyProjectionRow> Counterparties { get; }

    internal IReadOnlyList<NsiContractProjectionRow> Contracts { get; }

    internal IReadOnlyList<Guid> OrganizationIds { get; }

    internal IReadOnlyList<Guid> CurrencyIds { get; }
}

internal sealed class NsiCounterpartyProjectionRow
{
    internal NsiCounterpartyProjectionRow(Guid id, string code, string name, bool isActive)
    {
        Id = id;
        Code = code;
        Name = name;
        IsActive = isActive;
    }

    internal Guid Id { get; }

    internal string Code { get; }

    internal string Name { get; }

    internal bool IsActive { get; }
}

internal sealed class NsiContractProjectionRow
{
    internal NsiContractProjectionRow(
        Guid id,
        Guid counterpartyId,
        Guid organizationId,
        string code,
        string name,
        DateOnly contractDate,
        Guid currencyId,
        bool isActive)
    {
        Id = id;
        CounterpartyId = counterpartyId;
        OrganizationId = organizationId;
        Code = code;
        Name = name;
        ContractDate = contractDate;
        CurrencyId = currencyId;
        IsActive = isActive;
    }

    internal Guid Id { get; }

    internal Guid CounterpartyId { get; }

    internal Guid OrganizationId { get; }

    internal string Code { get; }

    internal string Name { get; }

    internal DateOnly ContractDate { get; }

    internal Guid CurrencyId { get; }

    internal bool IsActive { get; }
}
