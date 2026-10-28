using System.Data;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class TechnicalPersistence(BmsDbContext dbContext) :
    IBusinessAuditSink,
    IOutboxSink,
    IIdempotencyStore,
    IUnitOfWork
{
    public void Append(Guid id, DateTimeOffset occurredAtUtc, string actorExternalId,
        string objectType, string objectId, string action, string? beforeJson,
        string afterJson, string correlationId)
    {
        dbContext.BusinessAuditEntries.Add(new BusinessAuditEntry
        {
            Id = id,
            OccurredAtUtc = occurredAtUtc,
            ActorExternalId = actorExternalId,
            ObjectType = objectType,
            ObjectId = objectId,
            Action = action,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            CorrelationId = correlationId
        });
    }

    public void Enqueue(Guid id, DateTimeOffset occurredAtUtc, string messageType,
        string payloadJson, string correlationId)
    {
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = id,
            OccurredAtUtc = occurredAtUtc,
            MessageType = messageType,
            PayloadJson = payloadJson,
            CorrelationId = correlationId
        });
    }

    public async Task<StoredIdempotentResponse?> FindAsync(
        string scope, string key, CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "An idempotency lookup requires a Serializable business transaction.");
        }

        var record = await dbContext.IdempotencyRecords
            .FromSqlInterpolated($$"""
                SELECT [record].*
                FROM [bms_web].[IdempotencyRecords] AS [record]
                    WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_IdempotencyRecords]))
                WHERE [record].[Scope] = {{scope}}
                  AND [record].[Key] = {{key}}
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        return record is null
            ? null
            : new StoredIdempotentResponse(
                record.RequestHash,
                record.ResponseStatusCode,
                record.ResponseJson);
    }

    public void Add(string scope, string key, string requestHash, int responseStatusCode,
        string responseJson, DateTimeOffset createdAtUtc)
    {
        dbContext.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Scope = scope,
            Key = key,
            RequestHash = requestHash,
            ResponseStatusCode = responseStatusCode,
            ResponseJson = responseJson,
            CreatedAtUtc = createdAtUtc
        });
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ExpenseRequestVersionConflictException(exception);
        }
    }
}
