using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>Acquire before the first physical request/line read. Uses exactly
/// the established projection namespace, owner and timeout; no new retry.</summary>
internal static class RequestApplicationLock
{
    internal static async Task AcquireAsync(BmsDbContext db, Guid id,
        bool exclusive, CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A request lock requires an active transaction.");
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode=@mode,
                @LockOwner=N'Transaction', @LockTimeout=15000;
            SELECT @result;
            """;
        var resource = command.CreateParameter();
        resource.ParameterName = "@resource"; resource.DbType = DbType.String;
        resource.Value = BusinessManagedLockResources.ExpenseRequest(id); command.Parameters.Add(resource);
        var mode = command.CreateParameter();
        mode.ParameterName = "@mode"; mode.DbType = DbType.String;
        mode.Value = exclusive ? "Exclusive" : "Shared"; command.Parameters.Add(mode);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull || Convert.ToInt32(result) < 0)
            throw new InvalidOperationException("A consistent request lock could not be acquired.");
    }
}
