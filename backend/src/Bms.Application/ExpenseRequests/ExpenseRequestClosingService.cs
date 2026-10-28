using System.Security.Cryptography;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Accruals.Notifications;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

/// <summary>
/// Executes the approved document-closing commands under a locked request and
/// one Serializable transaction. Authorization, payment evidence, immutable
/// cycle data, aggregate mutation, audit, outbox and idempotency are committed
/// together; replay revalidates current role and organization access.
/// </summary>
internal sealed partial class ExpenseRequestClosingService(
    IExpenseRequestRepository repository,
    IExpenseRequestPaymentFactReader paymentFactReader,
    IAccrualClosingCycleSnapshotStore cycleStore,
    IAccessScope accessScope,
    EmployeeActorResolver employeeActorResolver,
    EmployeeFunctionalRoleResolver employeeFunctionalRoleResolver,
    IBusinessAuditSink auditSink,
    IOutboxSink outboxSink,
    IIdempotencyStore idempotencyStore,
    IBusinessTransactionRunner transactionRunner,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentActor currentActor,
    IAccrualNotificationControl notificationControl) : IExpenseRequestClosingService
{
    private const int MaximumIdempotencyKeyLength = 127;
    private const int MaximumIdempotencyScopeLength = 323;
    private const int StatusOk = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ExpenseRequestClosingCommandResult> ClosePartiallyAsync(
        Guid id,
        CloseExpenseRequestPartiallyCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = NormalizeRequiredReason(command.Reason);
        ValidateExpectedVersion(command.ExpectedVersion);
        if (command.DocumentsReceivedDate == default)
        {
            throw new DomainRuleViolationException(
                "Documents-received date is required.");
        }
        MoneyAmountPolicy.EnsurePositive(command.ControlAmount, "Closing control amount");
        var allocations = NormalizeManualAllocations(command.LineAllocations);
        return ExecuteAsync(
            id,
            ExpenseRequestWorkflowCodes.ClosePartialCommandCode,
            command with { Reason = reason, LineAllocations = allocations },
            idempotencyKey,
            cancellationToken);
    }

    public Task<ExpenseRequestClosingCommandResult> CloseFullyAsync(
        Guid id,
        CloseExpenseRequestFullyCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = NormalizeRequiredReason(command.Reason);
        ValidateExpectedVersion(command.ExpectedVersion);
        if (command.DocumentsReceivedDate == default)
        {
            throw new DomainRuleViolationException(
                "Documents-received date is required.");
        }

        return ExecuteAsync(
            id,
            ExpenseRequestWorkflowCodes.CloseFullCommandCode,
            command with { Reason = reason },
            idempotencyKey,
            cancellationToken);
    }

    public Task<ExpenseRequestClosingCommandResult> ReopenAsync(
        Guid id,
        ReopenExpenseRequestCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = NormalizeRequiredReason(command.Reason);
        ValidateExpectedVersion(command.ExpectedVersion);
        return ExecuteAsync(
            id,
            ExpenseRequestWorkflowCodes.ReopenCommandCode,
            command with { Reason = reason },
            idempotencyKey,
            cancellationToken);
    }

    public async Task<ExpenseRequestClosingStateDto?> ResolveIdempotencyAsync(
        Guid id,
        string commandCode,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorExternalId = RequireBusinessActor();
        ValidateCommandCode(commandCode);
        ValidateIdempotencyKey(idempotencyKey);
        if (id == Guid.Empty)
        {
            throw new ExpenseRequestNotFoundException();
        }

        try
        {
            return await transactionRunner.ExecuteAsync<ExpenseRequestClosingStateDto?>(
                async transactionCancellationToken =>
                {
                    var stored = await idempotencyStore.FindAsync(
                        BuildIdempotencyScope(id, commandCode, actorExternalId),
                        idempotencyKey,
                        transactionCancellationToken);
                    if (stored is null)
                    {
                        return null;
                    }

                    var request = await RequireLockedRequestAsync(
                        id,
                        transactionCancellationToken);
                    var accessDate = DateOnly.FromDateTime(RequireUtcNow().UtcDateTime);
                    var authority = await RequireClosingAuthorityAsync(
                        request,
                        commandCode,
                        actorExternalId,
                        accessDate,
                        transactionCancellationToken);
                    return DeserializeStoredResponse(id, stored) with
                    { CanExecuteClosingCommands = CanExecuteClosingForRole(authority.RoleCode, request.Status) };
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsPublicException(exception))
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExpenseRequestClosingUnavailableException(exception);
        }
    }

    private async Task<ExpenseRequestClosingCommandResult> ExecuteAsync<TCommand>(
        Guid id,
        string commandCode,
        TCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
        where TCommand : notnull
    {
        var actorExternalId = RequireBusinessActor();
        ValidateCommandCode(commandCode);
        ValidateIdempotencyKey(idempotencyKey);
        if (id == Guid.Empty)
        {
            throw new ExpenseRequestNotFoundException();
        }

        try
        {
            return await transactionRunner.ExecuteAsync(
                transactionCancellationToken => ExecuteInTransactionAsync(
                    id,
                    commandCode,
                    command,
                    idempotencyKey,
                    actorExternalId,
                    transactionCancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsPublicException(exception))
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExpenseRequestClosingUnavailableException(exception);
        }
    }

    private string RequireBusinessActor()
    {
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        if (currentActor.IsBmsAdministrator)
        {
            throw new ActorForbiddenException();
        }

        return actorExternalId;
    }

    private static bool IsPublicException(Exception exception) => exception is
        ActorNotAuthenticatedException or
        ActorForbiddenException or
        ExpenseRequestNotFoundException or
        ExpenseRequestVersionConflictException or
        IdempotencyConflictException or
        InvalidIdempotencyKeyException or
        ExpenseRequestClosingBlockedException or
        ExpenseRequestClosingUnavailableException or
        DomainRuleViolationException;

    private static IReadOnlyList<ExpenseRequestClosingLineAllocationCommand>? NormalizeManualAllocations(
        IReadOnlyList<ExpenseRequestClosingLineAllocationCommand>? allocations)
    {
        if (allocations is null)
        {
            return null;
        }
        if (allocations.Any(item => item is null || item.LineId == Guid.Empty))
        {
            throw new DomainRuleViolationException(
                "Closing line allocations are invalid.");
        }

        var ordered = allocations
            .OrderBy(item => item.LineId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select(item => item.LineId).Distinct().Count() != ordered.Length)
        {
            throw new DomainRuleViolationException(
                "Closing line allocations must be unique.");
        }
        foreach (var allocation in ordered)
        {
            MoneyAmountPolicy.EnsureNonNegative(
                allocation.Amount,
                "Closing line allocation");
        }

        return Array.AsReadOnly(ordered);
    }

    private static string NormalizeRequiredReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainRuleViolationException("Closing reason is required.");
        }

        var normalized = reason.Trim();
        if (normalized.Length > 2_000 || normalized.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("Closing reason is invalid.");
        }

        return normalized;
    }

    private static void ValidateExpectedVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty)
        {
            throw new ExpenseRequestVersionConflictException();
        }
    }

    private static void ValidateCommandCode(string commandCode)
    {
        if (string.IsNullOrWhiteSpace(commandCode) ||
            !ExpenseRequestClosingCommandCodes.All.Contains(commandCode))
        {
            throw new DomainRuleViolationException(
                "The requested closing command is not available.");
        }
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > MaximumIdempotencyKeyLength ||
            !string.Equals(idempotencyKey, idempotencyKey.Trim(), StringComparison.Ordinal) ||
            idempotencyKey.Any(char.IsControl))
        {
            throw new InvalidIdempotencyKeyException("Idempotency-Key is invalid.");
        }
    }

    private static string BuildIdempotencyScope(
        Guid requestId,
        string commandCode,
        string actorExternalId)
    {
        var discriminator = commandCode switch
        {
            ExpenseRequestWorkflowCodes.ClosePartialCommandCode => 'P',
            ExpenseRequestWorkflowCodes.CloseFullCommandCode => 'F',
            ExpenseRequestWorkflowCodes.ReopenCommandCode => 'R',
            _ => throw new InvalidOperationException("Closing command scope is unsupported."),
        };
        var scope = $"expense-requests:closing:{discriminator}:{requestId:D}:{actorExternalId}";
        if (scope.Length > MaximumIdempotencyScopeLength ||
            !string.Equals(scope, scope.Trim(), StringComparison.Ordinal) ||
            scope.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "The canonical idempotency scope exceeds the persisted contract.");
        }

        return scope;
    }

    private static string ComputeRequestHash<TCommand>(
        Guid requestId,
        string commandCode,
        TCommand command)
        where TCommand : notnull
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(
            new ClosingCommandEnvelope<TCommand>(
                "expense-request-closing-v1",
                requestId,
                commandCode,
                command),
            JsonOptions);
        return Convert.ToHexString(SHA256.HashData(canonical));
    }

    private static ExpenseRequestClosingStateDto DeserializeStoredResponse(
        Guid expectedRequestId,
        StoredIdempotentResponse stored)
    {
        if (stored.ResponseStatusCode != StatusOk)
        {
            throw new ExpenseRequestClosingUnavailableException();
        }

        return DeserializeStoredResponse(expectedRequestId, stored.ResponseJson);
    }

    private static ExpenseRequestClosingStateDto DeserializeStoredResponse(
        Guid expectedRequestId,
        string responseJson)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ExpenseRequestClosingStateDto>(
                responseJson,
                JsonOptions) ?? throw new InvalidOperationException();
            if (response.ExpenseRequestId != expectedRequestId ||
                response.ConcurrencyStamp == Guid.Empty ||
                !ExpenseRequestStatusCode.TryParse(response.Status, out _) ||
                response.CurrencyCode is not { Length: 3 } ||
                response.CurrencyCode.Any(character => character is < 'A' or > 'Z') ||
                response.Lines is null ||
                response.ConfirmedClosingControlAmount < 0m ||
                !MoneyAmountPolicy.IsRepresentable(response.ConfirmedClosingControlAmount) ||
                (response.LatestDocumentsReceivedDate is { } documentsDate &&
                 documentsDate == default) ||
                (response.FullyPaidAtUtc is { } fullyPaidAtUtc &&
                 (fullyPaidAtUtc == default ||
                  fullyPaidAtUtc.Offset != TimeSpan.Zero ||
                  fullyPaidAtUtc.Ticks % TimeSpan.TicksPerMillisecond != 0)) ||
                !StoredLinesAreValid(response.Lines))
            {
                throw new InvalidOperationException();
            }

            return response with
            {
                Lines = Array.AsReadOnly(response.Lines.ToArray()),
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new ExpenseRequestClosingUnavailableException(exception);
        }
    }

    private static bool StoredLinesAreValid(
        IReadOnlyList<ExpenseRequestClosingLineDto> lines)
    {
        var lineIds = new HashSet<Guid>();
        var sortOrders = new HashSet<int>();
        var previousSortOrder = 0;
        foreach (var line in lines)
        {
            if (line is null ||
                line.LineId == Guid.Empty ||
                !lineIds.Add(line.LineId) ||
                line.SortOrder <= previousSortOrder ||
                !sortOrders.Add(line.SortOrder) ||
                line.OriginalAmount <= 0m ||
                line.OriginalAmountKzt < 0m ||
                line.ConfirmedClosedAmount < 0m ||
                line.ConfirmedClosedAmountKzt < 0m ||
                line.RemainingAmount < 0m ||
                line.RemainingAmountKzt < 0m ||
                !MoneyAmountPolicy.IsRepresentable(line.OriginalAmount) ||
                !MoneyAmountPolicy.IsRepresentable(line.OriginalAmountKzt) ||
                !MoneyAmountPolicy.IsRepresentable(line.ConfirmedClosedAmount) ||
                !MoneyAmountPolicy.IsRepresentable(line.ConfirmedClosedAmountKzt) ||
                !MoneyAmountPolicy.IsRepresentable(line.RemainingAmount) ||
                !MoneyAmountPolicy.IsRepresentable(line.RemainingAmountKzt) ||
                line.ConfirmedClosedAmount + line.RemainingAmount != line.OriginalAmount ||
                line.ConfirmedClosedAmountKzt + line.RemainingAmountKzt !=
                    line.OriginalAmountKzt ||
                (line.RemainingAmount == 0m && line.RemainingAmountKzt != 0m))
            {
                return false;
            }

            previousSortOrder = line.SortOrder;
        }

        return true;
    }

    private sealed record ClosingCommandEnvelope<TCommand>(
        string Contract,
        Guid ExpenseRequestId,
        string CommandCode,
        TCommand Command);
}
