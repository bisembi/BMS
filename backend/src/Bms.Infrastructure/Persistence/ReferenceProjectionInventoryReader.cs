using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.Integration;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class ReferenceProjectionInventoryReader(BmsDbContext dbContext) :
    IReferenceProjectionInventoryReader
{
    internal const string CommandText = """
        SELECT
            (SELECT COUNT_BIG(*) FROM bms_web.Employees) AS EmployeeCount,
            (SELECT COUNT_BIG(*) FROM bms_web.Employees WHERE IsActive = CAST(1 AS bit)) AS ActiveEmployeeCount,
            (SELECT COUNT_BIG(*) FROM bms_web.FunctionalRoles) AS FunctionalRoleCount,
            (SELECT COUNT_BIG(*) FROM bms_web.FunctionalRoles WHERE IsActive = CAST(1 AS bit)) AS ActiveFunctionalRoleCount,
            (SELECT COUNT_BIG(*) FROM bms_web.EmployeeFunctionalRoles) AS EmployeeFunctionalRoleCount,
            (SELECT COUNT_BIG(*) FROM bms_web.EmployeeFunctionalRoles WHERE IsActive = CAST(1 AS bit)) AS ActiveEmployeeFunctionalRoleCount,
            (
                SELECT COUNT_BIG(*)
                FROM bms_web.EmployeeFunctionalRoles AS assignment
                LEFT JOIN bms_web.Employees AS employee ON employee.Id = assignment.EmployeeId
                WHERE assignment.IsActive = CAST(1 AS bit)
                  AND (employee.Id IS NULL OR employee.IsActive = CAST(0 AS bit))
            ) AS ActiveAssignmentsWithInactiveEmployeeCount,
            (
                SELECT COUNT_BIG(*)
                FROM bms_web.EmployeeFunctionalRoles AS assignment
                LEFT JOIN bms_web.FunctionalRoles AS role ON role.Id = assignment.FunctionalRoleId
                WHERE assignment.IsActive = CAST(1 AS bit)
                  AND (role.Id IS NULL OR role.IsActive = CAST(0 AS bit))
            ) AS ActiveAssignmentsWithInactiveFunctionalRoleCount;
        """;

    public async Task<ReferenceProjectionInventoryRecord> ReadAsync(
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        try
        {
            if (closeConnection)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = CommandText;
            var commandTimeout = dbContext.Database.GetCommandTimeout();
            if (commandTimeout.HasValue)
            {
                command.CommandTimeout = commandTimeout.Value;
            }

            await using var dataReader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleResult | CommandBehavior.SingleRow,
                cancellationToken);
            if (!await dataReader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "Reference projection inventory query returned no safe state.");
            }

            var result = new ReferenceProjectionInventoryRecord(
                dataReader.GetInt64(0),
                dataReader.GetInt64(1),
                dataReader.GetInt64(2),
                dataReader.GetInt64(3),
                dataReader.GetInt64(4),
                dataReader.GetInt64(5),
                dataReader.GetInt64(6),
                dataReader.GetInt64(7));
            if (await dataReader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "Reference projection inventory query returned multiple safe states.");
            }

            return result;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
