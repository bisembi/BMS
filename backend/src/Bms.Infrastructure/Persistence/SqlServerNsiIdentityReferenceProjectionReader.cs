using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using Bms.Application.Abstractions;
using Bms.Application.ReferenceData;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class SqlServerNsiIdentityReferenceProjectionReader :
    INsiIdentityReferenceProjectionReader
{
    private readonly INsiIdentityReferenceProjectionReadRuntime runtime;

    internal SqlServerNsiIdentityReferenceProjectionReader(BmsDbContext dbContext)
        : this(new EfCoreNsiIdentityReferenceProjectionReadRuntime(dbContext))
    {
    }

    internal SqlServerNsiIdentityReferenceProjectionReader(
        INsiIdentityReferenceProjectionReadRuntime runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<NsiIdentityReferenceProjectionSnapshot> ReadAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!runtime.HasSerializableTransaction)
            {
                throw new NsiIdentityReferenceImportUnavailableException();
            }

            var rows = await runtime.ReadAsync(cancellationToken);
            if (rows is null)
            {
                throw new NsiIdentityReferenceImportUnavailableException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return NsiIdentityReferenceProjectionSnapshot.Create(
                rows.Watermark is null
                    ? null
                    : NsiIdentityReferenceImportWatermark.Create(
                        rows.Watermark.AsOf,
                        rows.Watermark.CapturedAtUtc,
                        rows.Watermark.SourceConfigurationSha256,
                        rows.Watermark.SourceSnapshotSha256),
                rows.Employees.Select(row => NsiIdentityEmployeeImportItem.Create(
                    row.Id,
                    row.Code,
                    row.Name,
                    row.SourceDatabaseUserName,
                    row.IsActive)),
                rows.FunctionalRoles.Select(row =>
                    NsiIdentityFunctionalRoleProjectionItem.Create(
                        row.Id,
                        row.Code,
                        row.Name,
                        row.IsActive)),
                rows.CostCenters.Select(row =>
                    NsiIdentityCostCenterProjectionItem.Create(
                        row.Id,
                        row.Code,
                        row.Name,
                        row.ParentId,
                        row.IsGroup,
                        row.IsActive, row.OwnerManagedLocally)),
                rows.EmployeeRoles.Select(row =>
                    NsiIdentityEmployeeRoleImportItem.Create(
                        row.EmployeeId,
                        row.FunctionalRoleId,
                        row.IsActive)),
                rows.BudgetOwners.Select(row =>
                    NsiIdentityBudgetOwnerProjectionItem.Create(
                        row.EmployeeId,
                        row.CostCenterId,
                        row.IsActive)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NsiIdentityReferenceImportUnavailableException)
        {
            throw;
        }
    }
}

internal interface INsiIdentityReferenceProjectionReadRuntime
{
    bool HasSerializableTransaction { get; }

    Task<NsiIdentityReferenceProjectionRows> ReadAsync(
        CancellationToken cancellationToken);
}

internal sealed class EfCoreNsiIdentityReferenceProjectionReadRuntime(
    BmsDbContext dbContext) : INsiIdentityReferenceProjectionReadRuntime
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

    public async Task<NsiIdentityReferenceProjectionRows> ReadAsync(
        CancellationToken cancellationToken)
    {
        if (!HasSerializableTransaction)
        {
            throw new InvalidOperationException(
                "A Serializable transaction is required for the identity reference projection.");
        }

        var currentTransaction = dbContext.Database.CurrentTransaction;
        if (currentTransaction is null)
        {
            throw new InvalidOperationException(
                "A Serializable transaction is required for the identity reference projection.");
        }

        var transaction = currentTransaction.GetDbTransaction();
        var connection = transaction.Connection;
        if (connection is null || connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "The identity reference projection transaction connection is unavailable.");
        }

        var watermark = await ReadWatermarkAsync(
            connection,
            transaction,
            cancellationToken);
        var employees = await ReadEmployeesAsync(
            connection,
            transaction,
            cancellationToken);
        var roles = await ReadRolesAsync(connection, transaction, cancellationToken);
        var costCenters = await ReadCostCentersAsync(
            connection,
            transaction,
            cancellationToken);
        var employeeRoles = await ReadEmployeeRolesAsync(
            connection,
            transaction,
            cancellationToken);
        var budgetOwners = await ReadBudgetOwnersAsync(
            connection,
            transaction,
            cancellationToken);

        return new NsiIdentityReferenceProjectionRows(
            watermark,
            employees,
            roles,
            costCenters,
            employeeRoles,
            budgetOwners);
    }

    private static async Task<NsiIdentityImportWatermarkRow?> ReadWatermarkAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            NsiIdentityReferenceProjectionLockSql.Watermark);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var row = new NsiIdentityImportWatermarkRow(
            DateOnly.FromDateTime(reader.GetDateTime(0)),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.GetString(2),
            reader.GetString(3));
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The identity reference projection has more than one watermark row.");
        }

        return row;
    }

    private static async Task<IReadOnlyList<NsiIdentityEmployeeRow>> ReadEmployeesAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new List<NsiIdentityEmployeeRow>();
        await using var command = CreateCommand(
            connection,
            transaction,
            NsiIdentityReferenceProjectionLockSql.Employees);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new NsiIdentityEmployeeRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<NsiIdentityFunctionalRoleRow>> ReadRolesAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new List<NsiIdentityFunctionalRoleRow>();
        await using var command = CreateCommand(
            connection,
            transaction,
            NsiIdentityReferenceProjectionLockSql.FunctionalRoles);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new NsiIdentityFunctionalRoleRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<NsiIdentityCostCenterRow>> ReadCostCentersAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new List<NsiIdentityCostCenterRow>();
        await using var command = CreateCommand(
            connection,
            transaction,
            NsiIdentityReferenceProjectionLockSql.CostCenters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new NsiIdentityCostCenterRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5), reader.GetBoolean(6)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<NsiIdentityEmployeeRoleRow>>
        ReadEmployeeRolesAsync(
            DbConnection connection,
            DbTransaction transaction,
            CancellationToken cancellationToken)
    {
        var result = new List<NsiIdentityEmployeeRoleRow>();
        await using var command = CreateCommand(
            connection,
            transaction,
            NsiIdentityReferenceProjectionLockSql.EmployeeRoles);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new NsiIdentityEmployeeRoleRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetBoolean(2)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<NsiIdentityBudgetOwnerRow>>
        ReadBudgetOwnersAsync(
            DbConnection connection,
            DbTransaction transaction,
            CancellationToken cancellationToken)
    {
        var result = new List<NsiIdentityBudgetOwnerRow>();
        await using var command = CreateCommand(
            connection,
            transaction,
            NsiIdentityReferenceProjectionLockSql.BudgetOwners);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new NsiIdentityBudgetOwnerRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetBoolean(2)));
        }

        return result;
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        DbTransaction transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }
}

internal static class NsiIdentityReferenceProjectionLockSql
{
    internal const string Watermark = """
        SELECT [AsOf], [CapturedAtUtc], [SourceConfigurationSha256], [SourceSnapshotSha256]
        FROM [bms_web].[NsiIdentityReferenceImportWatermark] WITH (UPDLOCK, HOLDLOCK)
        WHERE [SingletonId] = 1
        ORDER BY [SingletonId] ASC
        """;

    internal const string Employees = """
        SELECT [Id], [Code], [Name], [SourceDatabaseUserName], [IsActive]
        FROM [bms_web].[Employees] WITH (UPDLOCK, HOLDLOCK)
        ORDER BY [Id] ASC
        """;

    internal const string FunctionalRoles = """
        SELECT [Id], [Code], [Name], [IsActive]
        FROM [bms_web].[FunctionalRoles] WITH (UPDLOCK, HOLDLOCK)
        ORDER BY [Id] ASC
        """;

    internal const string CostCenters = """
        SELECT [Id], [Code], [Name], [ParentId], [IsGroup], [IsActive], [OwnerManagedLocally]
        FROM [bms_web].[CostCenters] WITH (UPDLOCK, HOLDLOCK)
        ORDER BY [Id] ASC
        """;

    internal const string EmployeeRoles = """
        SELECT [EmployeeId], [FunctionalRoleId], [IsActive]
        FROM [bms_web].[EmployeeFunctionalRoles] WITH (UPDLOCK, HOLDLOCK)
        ORDER BY [EmployeeId] ASC, [FunctionalRoleId] ASC
        """;

    internal const string BudgetOwners = """
        SELECT [EmployeeId], [CostCenterId], [IsActive]
        FROM [bms_web].[BudgetOwnerCostCenterAssignments] WITH (UPDLOCK, HOLDLOCK)
        ORDER BY [EmployeeId] ASC, [CostCenterId] ASC
        """;
}

internal sealed class NsiIdentityReferenceProjectionRows
{
    internal NsiIdentityReferenceProjectionRows(
        NsiIdentityImportWatermarkRow? watermark,
        IEnumerable<NsiIdentityEmployeeRow> employees,
        IEnumerable<NsiIdentityFunctionalRoleRow> functionalRoles,
        IEnumerable<NsiIdentityCostCenterRow> costCenters,
        IEnumerable<NsiIdentityEmployeeRoleRow> employeeRoles,
        IEnumerable<NsiIdentityBudgetOwnerRow> budgetOwners)
    {
        Watermark = watermark;
        Employees = Snapshot(employees, nameof(employees));
        FunctionalRoles = Snapshot(functionalRoles, nameof(functionalRoles));
        CostCenters = Snapshot(costCenters, nameof(costCenters));
        EmployeeRoles = Snapshot(employeeRoles, nameof(employeeRoles));
        BudgetOwners = Snapshot(budgetOwners, nameof(budgetOwners));
    }

    internal NsiIdentityImportWatermarkRow? Watermark { get; }
    internal IReadOnlyList<NsiIdentityEmployeeRow> Employees { get; }
    internal IReadOnlyList<NsiIdentityFunctionalRoleRow> FunctionalRoles { get; }
    internal IReadOnlyList<NsiIdentityCostCenterRow> CostCenters { get; }
    internal IReadOnlyList<NsiIdentityEmployeeRoleRow> EmployeeRoles { get; }
    internal IReadOnlyList<NsiIdentityBudgetOwnerRow> BudgetOwners { get; }

    private static IReadOnlyList<T> Snapshot<T>(
        IEnumerable<T> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}

internal sealed record NsiIdentityImportWatermarkRow(
    DateOnly AsOf,
    DateTimeOffset CapturedAtUtc,
    string SourceConfigurationSha256,
    string SourceSnapshotSha256);

internal sealed record NsiIdentityEmployeeRow(
    Guid Id,
    string Code,
    string Name,
    string? SourceDatabaseUserName,
    bool IsActive);

internal sealed record NsiIdentityFunctionalRoleRow(
    Guid Id,
    string Code,
    string Name,
    bool IsActive);

internal sealed record NsiIdentityCostCenterRow(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentId,
    bool IsGroup,
    bool IsActive,
    bool OwnerManagedLocally = false);

internal sealed record NsiIdentityEmployeeRoleRow(
    Guid EmployeeId,
    Guid FunctionalRoleId,
    bool IsActive);

internal sealed record NsiIdentityBudgetOwnerRow(
    Guid EmployeeId,
    Guid CostCenterId,
    bool IsActive);
