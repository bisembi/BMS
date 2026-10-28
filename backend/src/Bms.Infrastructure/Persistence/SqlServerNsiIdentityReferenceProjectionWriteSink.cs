using System.Data;
using System.Data.Common;
using System.Text.Json;
using Bms.Application.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed class SqlServerNsiIdentityReferenceProjectionWriteSink(
    BmsDbContext dbContext) : INsiIdentityReferenceProjectionWriteSink
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

    public bool HasPendingChanges => dbContext.ChangeTracker.HasChanges();

    public bool HasTrackedProjectionEntities => dbContext.ChangeTracker.Entries().Any(entry =>
        entry.Entity is EmployeeReference or
            FunctionalRoleReference or
            EmployeeFunctionalRole or
            CostCenterReference or
            BudgetOwnerCostCenterAssignment);

    public async Task<bool> CodesMatchDatabaseSemanticsAsync(
        NsiIdentityReferenceProjectionWritePlan plan,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(current);
        var employees = current.Employees.ToDictionary(item => item.Id, item => item.Code);
        foreach (var write in plan.EmployeeWrites)
        {
            employees[write.Item.Id] = write.Item.Code;
        }

        var roles = current.FunctionalRoles.ToDictionary(item => item.Id, item => item.Code);
        foreach (var write in plan.FunctionalRoleWrites)
        {
            roles[write.Item.Id] = write.Item.TargetCode;
        }

        var costCenters = current.CostCenters.ToDictionary(item => item.Id, item => item.Code);
        foreach (var write in plan.CostCenterWrites)
        {
            costCenters[write.Item.Id] = write.Item.Code;
        }

        await using var command = CreateCommand(NsiIdentityReferenceProjectionWriteSql.CodeSemantics);
        AddParameter(
            command,
            "@employeeCodes",
            JsonSerializer.Serialize(employees.Values.Order(StringComparer.Ordinal)),
            DbType.String);
        AddParameter(
            command,
            "@roleCodes",
            JsonSerializer.Serialize(roles.Values.Order(StringComparer.Ordinal)),
            DbType.String);
        AddParameter(
            command,
            "@costCenterCodes",
            JsonSerializer.Serialize(costCenters.Values.Order(StringComparer.OrdinalIgnoreCase)),
            DbType.String);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool value && value;
    }

    public Task<int> DeactivateBudgetOwnersAsync(
        IReadOnlyList<NsiIdentityRelationKey> keys,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        ExecuteRelationsAsync(
            keys,
            NsiIdentityReferenceProjectionWriteSql.DeactivateBudgetOwner,
            "@employeeId",
            "@costCenterId",
            cancellationToken);

    public Task<int> DeactivateEmployeeRolesAsync(
        IReadOnlyList<NsiIdentityRelationKey> keys,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        ExecuteRelationsAsync(
            keys,
            NsiIdentityReferenceProjectionWriteSql.DeactivateEmployeeRole,
            "@employeeId",
            "@functionalRoleId",
            cancellationToken);

    public Task<int> DeactivateEmployeesAsync(
        IReadOnlyList<Guid> ids,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        ExecuteIdsAsync(
            ids,
            NsiIdentityReferenceProjectionWriteSql.DeactivateEmployee,
            cancellationToken);

    public Task<int> DeactivateFunctionalRolesAsync(
        IReadOnlyList<Guid> ids,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        ExecuteIdsAsync(
            ids,
            NsiIdentityReferenceProjectionWriteSql.DeactivateFunctionalRole,
            cancellationToken);

    public Task<int> DeactivateCostCentersAsync(
        IReadOnlyList<Guid> ids,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        ExecuteIdsAsync(
            ids,
            NsiIdentityReferenceProjectionWriteSql.DeactivateCostCenter,
            cancellationToken);

    public Task<int> VacateEmployeeCodesAsync(
        IReadOnlyList<NsiIdentityCodeVacate> vacates,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        VacateCodesAsync(
            vacates,
            NsiIdentityReferenceProjectionWriteSql.VacateEmployeeCode,
            cancellationToken);

    public Task<int> WriteEmployeesAsync(
        IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityEmployeeImportItem>> writes,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        ExecuteAssignmentsAsync(
            writes,
            async (write, token) =>
            {
                await using var command = CreateCommand(
                    write.Operation is NsiReferenceImportOperation.Insert
                        ? NsiIdentityReferenceProjectionWriteSql.InsertEmployee
                        : NsiIdentityReferenceProjectionWriteSql.UpdateEmployee);
                AddParameter(command, "@id", write.Item.Id, DbType.Guid);
                AddParameter(command, "@code", write.Item.Code, DbType.String);
                AddParameter(command, "@name", write.Item.Name, DbType.String);
                AddParameter(
                    command,
                    "@sourceDatabaseUserName",
                    write.Item.SourceDatabaseUserName,
                    DbType.String);
                AddParameter(command, "@isActive", write.Item.IsActive, DbType.Boolean);
                return await command.ExecuteNonQueryAsync(token);
            },
            cancellationToken);

    public Task<int> VacateFunctionalRoleCodesAsync(
        IReadOnlyList<NsiIdentityCodeVacate> vacates,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        VacateCodesAsync(
            vacates,
            NsiIdentityReferenceProjectionWriteSql.VacateFunctionalRoleCode,
            cancellationToken);

    public Task<int> WriteFunctionalRolesAsync(
        IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityFunctionalRoleImportItem>> writes,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        ExecuteAssignmentsAsync(
            writes,
            async (write, token) =>
            {
                await using var command = CreateCommand(
                    write.Operation is NsiReferenceImportOperation.Insert
                        ? NsiIdentityReferenceProjectionWriteSql.InsertFunctionalRole
                        : NsiIdentityReferenceProjectionWriteSql.UpdateFunctionalRole);
                AddParameter(command, "@id", write.Item.Id, DbType.Guid);
                AddParameter(command, "@code", write.Item.TargetCode, DbType.String);
                AddParameter(command, "@name", write.Item.Name, DbType.String);
                AddParameter(command, "@isActive", write.Item.IsActive, DbType.Boolean);
                return await command.ExecuteNonQueryAsync(token);
            },
            cancellationToken);

    public Task<int> VacateCostCenterCodesAsync(
        IReadOnlyList<NsiIdentityCodeVacate> vacates,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        VacateCodesAsync(
            vacates,
            NsiIdentityReferenceProjectionWriteSql.VacateCostCenterCode,
            cancellationToken);

    public Task<int> WriteCostCentersAsync(
        IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityCostCenterImportItem>> writes,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        ExecuteAssignmentsAsync(
            writes,
            async (write, token) =>
            {
                await using var command = CreateCommand(
                    write.Operation is NsiReferenceImportOperation.Insert
                        ? NsiIdentityReferenceProjectionWriteSql.InsertCostCenter
                        : NsiIdentityReferenceProjectionWriteSql.UpdateCostCenter);
                AddParameter(command, "@id", write.Item.Id, DbType.Guid);
                AddParameter(command, "@code", write.Item.Code, DbType.String);
                AddParameter(command, "@name", write.Item.Name, DbType.String);
                AddParameter(command, "@parentId", write.Item.ParentId, DbType.Guid);
                AddParameter(command, "@isGroup", write.Item.IsGroup, DbType.Boolean);
                AddParameter(command, "@isActive", write.Item.IsActive, DbType.Boolean);
                return await command.ExecuteNonQueryAsync(token);
            },
            cancellationToken);

    public Task<int> WriteEmployeeRolesAsync(
        IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityEmployeeRoleImportItem>> writes,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        ExecuteAssignmentsAsync(
            writes,
            async (write, token) =>
            {
                await using var command = CreateCommand(
                    write.Operation is NsiReferenceImportOperation.Insert
                        ? NsiIdentityReferenceProjectionWriteSql.InsertEmployeeRole
                        : NsiIdentityReferenceProjectionWriteSql.UpdateEmployeeRole);
                AddParameter(command, "@employeeId", write.Item.EmployeeId, DbType.Guid);
                AddParameter(
                    command,
                    "@functionalRoleId",
                    write.Item.FunctionalRoleId,
                    DbType.Guid);
                AddParameter(command, "@isActive", write.Item.IsActive, DbType.Boolean);
                return await command.ExecuteNonQueryAsync(token);
            },
            cancellationToken);

    public Task<int> WriteBudgetOwnersAsync(
        IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityBudgetOwnerImportItem>> writes,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken) =>
        ExecuteAssignmentsAsync(
            writes,
            async (write, token) =>
            {
                await using var command = CreateCommand(
                    write.Operation is NsiReferenceImportOperation.Insert
                        ? NsiIdentityReferenceProjectionWriteSql.InsertBudgetOwner
                        : NsiIdentityReferenceProjectionWriteSql.UpdateBudgetOwner);
                AddParameter(command, "@employeeId", write.Item.EmployeeId, DbType.Guid);
                AddParameter(command, "@costCenterId", write.Item.CostCenterId, DbType.Guid);
                return await command.ExecuteNonQueryAsync(token);
            },
            cancellationToken);

    public async Task<int> WriteWatermarkAsync(
        NsiIdentityReferenceImportWatermark watermark,
        NsiIdentityReferenceImportWatermark? current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(watermark);
        await using var command = CreateCommand(
            current is null
                ? NsiIdentityReferenceProjectionWriteSql.InsertWatermark
                : NsiIdentityReferenceProjectionWriteSql.UpdateWatermark);
        AddParameter(
            command,
            "@asOf",
            watermark.AsOf.ToDateTime(TimeOnly.MinValue),
            DbType.Date);
        AddParameter(
            command,
            "@capturedAtUtc",
            watermark.CapturedAtUtc,
            DbType.DateTimeOffset);
        AddParameter(
            command,
            "@sourceConfigurationSha256",
            watermark.SourceConfigurationSha256,
            DbType.AnsiStringFixedLength);
        AddParameter(
            command,
            "@sourceSnapshotSha256",
            watermark.SourceSnapshotSha256,
            DbType.AnsiStringFixedLength);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> ExecuteIdsAsync(
        IReadOnlyList<Guid> ids,
        string commandText,
        CancellationToken cancellationToken)
    {
        var affected = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = CreateCommand(commandText);
            AddParameter(command, "@id", id, DbType.Guid);
            affected = checked(affected + await command.ExecuteNonQueryAsync(cancellationToken));
        }
        return affected;
    }

    private async Task<int> ExecuteRelationsAsync(
        IReadOnlyList<NsiIdentityRelationKey> keys,
        string commandText,
        string firstParameterName,
        string secondParameterName,
        CancellationToken cancellationToken)
    {
        var affected = 0;
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = CreateCommand(commandText);
            AddParameter(command, firstParameterName, key.FirstId, DbType.Guid);
            AddParameter(command, secondParameterName, key.SecondId, DbType.Guid);
            affected = checked(affected + await command.ExecuteNonQueryAsync(cancellationToken));
        }
        return affected;
    }

    private async Task<int> VacateCodesAsync(
        IReadOnlyList<NsiIdentityCodeVacate> vacates,
        string commandText,
        CancellationToken cancellationToken)
    {
        var affected = 0;
        foreach (var vacate in vacates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = CreateCommand(commandText);
            AddParameter(command, "@id", vacate.Id, DbType.Guid);
            AddParameter(command, "@code", vacate.TemporaryCode, DbType.String);
            affected = checked(affected + await command.ExecuteNonQueryAsync(cancellationToken));
        }
        return affected;
    }

    private static async Task<int> ExecuteAssignmentsAsync<T>(
        IReadOnlyList<NsiIdentityImportAssignment<T>> writes,
        Func<NsiIdentityImportAssignment<T>, CancellationToken, Task<int>> execute,
        CancellationToken cancellationToken)
        where T : class
    {
        var affected = 0;
        foreach (var write in writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            affected = checked(affected + await execute(write, cancellationToken));
        }
        return affected;
    }

    private DbCommand CreateCommand(string commandText)
    {
        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        if (transaction is null || transaction.IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "A Serializable transaction is required for the identity reference projection.");
        }

        var connection = transaction.Connection;
        if (connection is null || connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "The identity reference projection transaction connection is unavailable.");
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object? value,
        DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

internal static class NsiIdentityReferenceProjectionWriteSql
{
    internal const string CodeSemantics = """
        DECLARE @databaseCollation sysname = CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation'));
        DECLARE @costCenterCollation sysname =
        (
            SELECT [collation_name]
            FROM sys.columns
            WHERE [object_id] = OBJECT_ID(N'bms_web.CostCenters', N'U')
              AND [name] = N'Code'
        );
        SELECT CONVERT(bit, CASE WHEN
            COL_LENGTH(N'bms_web.Employees', N'Code') IS NOT NULL
            AND COL_LENGTH(N'bms_web.FunctionalRoles', N'Code') IS NOT NULL
            AND @costCenterCollation = @databaseCollation
            AND (SELECT [collation_name] FROM sys.columns
                 WHERE [object_id] = OBJECT_ID(N'bms_web.Employees', N'U')
                   AND [name] = N'Code') = N'Latin1_General_100_BIN2'
            AND (SELECT [collation_name] FROM sys.columns
                 WHERE [object_id] = OBJECT_ID(N'bms_web.FunctionalRoles', N'U')
                   AND [name] = N'Code') = N'Latin1_General_100_BIN2'
            AND N'a' COLLATE DATABASE_DEFAULT = N'A' COLLATE DATABASE_DEFAULT
            AND N'a' COLLATE DATABASE_DEFAULT <> N'á' COLLATE DATABASE_DEFAULT
            AND (SELECT COUNT_BIG(*) FROM OPENJSON(@employeeCodes)) =
                (SELECT COUNT_BIG(DISTINCT CONVERT(nvarchar(20), [value]) COLLATE Latin1_General_100_BIN2)
                 FROM OPENJSON(@employeeCodes))
            AND (SELECT COUNT_BIG(*) FROM OPENJSON(@roleCodes)) =
                (SELECT COUNT_BIG(DISTINCT CONVERT(nvarchar(30), [value]) COLLATE Latin1_General_100_BIN2)
                 FROM OPENJSON(@roleCodes))
            AND (SELECT COUNT_BIG(*) FROM OPENJSON(@costCenterCodes)) =
                (SELECT COUNT_BIG(DISTINCT CONVERT(nvarchar(100), [value]) COLLATE DATABASE_DEFAULT)
                 FROM OPENJSON(@costCenterCodes))
        THEN 1 ELSE 0 END)
        """;

    internal const string DeactivateBudgetOwner = """
        UPDATE bms_web.BudgetOwnerCostCenterAssignments
        SET IsActive = CAST(0 AS bit)
        WHERE EmployeeId = @employeeId AND CostCenterId = @costCenterId
          AND IsActive = CAST(1 AS bit)
        """;
    internal const string DeactivateEmployeeRole = """
        UPDATE bms_web.EmployeeFunctionalRoles
        SET IsActive = CAST(0 AS bit)
        WHERE EmployeeId = @employeeId AND FunctionalRoleId = @functionalRoleId
          AND IsActive = CAST(1 AS bit)
        """;
    internal const string DeactivateEmployee = """
        UPDATE bms_web.Employees SET IsActive = CAST(0 AS bit)
        WHERE Id = @id AND IsActive = CAST(1 AS bit)
          AND NOT EXISTS
          (
              SELECT 1
              FROM bms_web.EmployeeActorMappings
              WHERE EmployeeId = @id AND IsActive = CAST(1 AS bit)
          )
        """;
    internal const string DeactivateFunctionalRole = """
        UPDATE bms_web.FunctionalRoles SET IsActive = CAST(0 AS bit)
        WHERE Id = @id AND IsActive = CAST(1 AS bit)
        """;
    internal const string DeactivateCostCenter = """
        UPDATE bms_web.CostCenters SET IsActive = CAST(0 AS bit)
        WHERE Id = @id AND IsActive = CAST(1 AS bit)
        """;
    internal const string VacateEmployeeCode =
        "UPDATE bms_web.Employees SET Code = @code WHERE Id = @id";
    internal const string VacateFunctionalRoleCode =
        "UPDATE bms_web.FunctionalRoles SET Code = @code WHERE Id = @id";
    internal const string VacateCostCenterCode =
        "UPDATE bms_web.CostCenters SET Code = @code WHERE Id = @id";

    internal const string InsertEmployee = """
        INSERT INTO bms_web.Employees (Id, Code, Name, SourceDatabaseUserName, IsActive)
        VALUES (@id, @code, @name, @sourceDatabaseUserName, @isActive)
        """;
    internal const string UpdateEmployee = """
        UPDATE bms_web.Employees
        SET Code = @code, Name = @name,
            SourceDatabaseUserName = @sourceDatabaseUserName, IsActive = @isActive
        WHERE Id = @id
        """;
    internal const string InsertFunctionalRole = """
        INSERT INTO bms_web.FunctionalRoles (Id, Code, Name, IsActive)
        VALUES (@id, @code, @name, @isActive)
        """;
    internal const string UpdateFunctionalRole = """
        UPDATE bms_web.FunctionalRoles
        SET Code = @code, Name = @name, IsActive = @isActive
        WHERE Id = @id
        """;
    internal const string InsertCostCenter = """
        INSERT INTO bms_web.CostCenters (Id, Code, Name, ParentId, IsGroup, IsActive)
        VALUES (@id, @code, @name, @parentId, @isGroup, @isActive)
        """;
    internal const string UpdateCostCenter = """
        UPDATE bms_web.CostCenters
        SET Code = @code, Name = @name, ParentId = @parentId,
            IsGroup = @isGroup, IsActive = @isActive
        WHERE Id = @id
        """;
    internal const string InsertEmployeeRole = """
        INSERT INTO bms_web.EmployeeFunctionalRoles
            (EmployeeId, FunctionalRoleId, IsActive)
        VALUES (@employeeId, @functionalRoleId, @isActive)
        """;
    internal const string UpdateEmployeeRole = """
        UPDATE bms_web.EmployeeFunctionalRoles SET IsActive = @isActive
        WHERE EmployeeId = @employeeId AND FunctionalRoleId = @functionalRoleId
        """;
    internal const string InsertBudgetOwner = """
        INSERT INTO bms_web.BudgetOwnerCostCenterAssignments
            (EmployeeId, CostCenterId, IsActive)
        VALUES (@employeeId, @costCenterId, CAST(1 AS bit))
        """;
    internal const string UpdateBudgetOwner = """
        UPDATE bms_web.BudgetOwnerCostCenterAssignments SET IsActive = CAST(1 AS bit)
        WHERE EmployeeId = @employeeId AND CostCenterId = @costCenterId
        """;
    internal const string InsertWatermark = """
        INSERT INTO bms_web.NsiIdentityReferenceImportWatermark
            (SingletonId, AsOf, CapturedAtUtc,
             SourceConfigurationSha256, SourceSnapshotSha256)
        VALUES (1, @asOf, @capturedAtUtc,
                @sourceConfigurationSha256, @sourceSnapshotSha256)
        """;
    internal const string UpdateWatermark = """
        UPDATE bms_web.NsiIdentityReferenceImportWatermark
        SET AsOf = @asOf, CapturedAtUtc = @capturedAtUtc,
            SourceConfigurationSha256 = @sourceConfigurationSha256,
            SourceSnapshotSha256 = @sourceSnapshotSha256
        WHERE SingletonId = 1
        """;
}
