using System.Text.Json;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

internal sealed partial class ExpenseRequestClosingService
{
    private async Task<ExpenseRequestClosingCommandResult> ExecuteInTransactionAsync<TCommand>(
        Guid id,
        string commandCode,
        TCommand command,
        string idempotencyKey,
        string actorExternalId,
        CancellationToken cancellationToken)
        where TCommand : notnull
    {
        var scope = BuildIdempotencyScope(id, commandCode, actorExternalId);
        var requestHash = ComputeRequestHash(id, commandCode, command);
        var stored = await idempotencyStore.FindAsync(
            scope,
            idempotencyKey,
            cancellationToken);
        var request = await RequireLockedRequestAsync(id, cancellationToken);
        var now = RequireUtcNow();
        var authority = await RequireClosingAuthorityAsync(
            request,
            commandCode,
            actorExternalId,
            DateOnly.FromDateTime(now.UtcDateTime),
            cancellationToken);
        if (stored is not null &&
            !string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }
        if (stored is not null)
        {
            return new ExpenseRequestClosingCommandResult(
                DeserializeStoredResponse(id, stored) with
                { CanExecuteClosingCommands = CanExecuteClosingForRole(authority.RoleCode, request.Status) },
                true,
                stored.ResponseStatusCode);
        }

        var expectedVersion = command switch
        {
            CloseExpenseRequestPartiallyCommand partial => partial.ExpectedVersion,
            CloseExpenseRequestFullyCommand full => full.ExpectedVersion,
            ReopenExpenseRequestCommand reopen => reopen.ExpectedVersion,
            _ => throw new InvalidOperationException("Closing command payload is unsupported."),
        };
        if (request.ConcurrencyStamp != expectedVersion)
        {
            throw new ExpenseRequestVersionConflictException();
        }

        var before = ExpenseRequestClosingProjection.FromDomain(
            request,
            canExecuteClosingCommands: true);
        var cycleBefore = request.AccrualCycleId;
        ClosingExecution execution = command switch
        {
            CloseExpenseRequestPartiallyCommand partial => await ClosePartiallyInTransactionAsync(
                request,
                partial,
                now,
                cancellationToken),
            CloseExpenseRequestFullyCommand full => await CloseFullyInTransactionAsync(
                request,
                full,
                now,
                cancellationToken),
            ReopenExpenseRequestCommand reopen => await ReopenInTransactionAsync(
                request,
                reopen,
                now,
                cancellationToken),
            _ => throw new InvalidOperationException("Closing command payload is unsupported."),
        };

        var response = ExpenseRequestClosingProjection.FromDomain(
            request,
            canExecuteClosingCommands: CanExecuteClosingForRole(authority.RoleCode, request.Status));
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        var beforeJson = JsonSerializer.Serialize(before, JsonOptions);
        var auditPayload = new ClosingAuditPayload(
            commandCode,
            authority.RoleCode,
            authority.EmployeeId,
            execution.Reason,
            execution.DocumentsReceivedDate,
            execution.ControlAmount,
            execution.AllocatedAmount,
            execution.AllocatedAmountKzt,
            execution.ExcessAmount,
            cycleBefore,
            request.AccrualCycleId,
            execution.Lines,
            response);
        var auditJson = JsonSerializer.Serialize(auditPayload, JsonOptions);
        var correlationId = Guid.NewGuid().ToString("N");
        auditSink.Append(
            Guid.NewGuid(),
            now,
            actorExternalId,
            "ExpenseRequest",
            request.Id.ToString("D"),
            AuditAction(commandCode),
            beforeJson,
            auditJson,
            correlationId);
        outboxSink.Enqueue(
            Guid.NewGuid(),
            now,
            OutboxMessageType(commandCode),
            auditJson,
            correlationId);
        idempotencyStore.Add(
            scope,
            idempotencyKey,
            requestHash,
            StatusOk,
            responseJson,
            now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await CheckAccrualNotificationsAsync(id, commandCode, actorExternalId, now, cancellationToken);
        return new ExpenseRequestClosingCommandResult(response, false, StatusOk);
    }

    private async Task<ClosingExecution> ClosePartiallyInTransactionAsync(
        ExpenseRequest request,
        CloseExpenseRequestPartiallyCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.Status != ExpenseRequestStatus.Paid)
        {
            throw new ExpenseRequestClosingBlockedException();
        }

        var payment = await RequireFullPaymentEvidenceAsync(request, cancellationToken);
        var cycle = await ResolvePaidCycleAsync(request, now, cancellationToken);
        var balances = BuildBalances(request, cycle.Snapshot);
        var manual = command.LineAllocations?.Select(item =>
            new AccrualClosingRequestedLineAllocation(item.LineId, item.Amount)).ToArray();
        var plan = AccrualClosingAllocationPlanner.Plan(
            command.ControlAmount,
            balances,
            manual);
        request.ApplyPartialClosing(
            command.ExpectedVersion,
            now,
            command.DocumentsReceivedDate,
            cycle.Snapshot?.CycleId,
            payment.FirstFullyPaidAtUtc,
            command.ControlAmount,
            plan.Lines);
        AddNewCycleAfterSuccessfulMutation(cycle);

        return new ClosingExecution(
            RequireNormalizedReason(command.Reason),
            command.DocumentsReceivedDate,
            command.ControlAmount,
            plan.AllocatedAmount,
            plan.AllocatedAmountKzt,
            plan.ExcessAmount,
            plan.Lines);
    }

    private async Task<ClosingExecution> CloseFullyInTransactionAsync(
        ExpenseRequest request,
        CloseExpenseRequestFullyCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.Status != ExpenseRequestStatus.Paid)
        {
            throw new ExpenseRequestClosingBlockedException();
        }

        var payment = await RequireFullPaymentEvidenceAsync(request, cancellationToken);
        var cycle = await ResolvePaidCycleAsync(request, now, cancellationToken);
        var beforeLines = ExpenseRequestClosingProjection.FromDomain(
            request,
            canExecuteClosingCommands: true).Lines;
        var remainingAmount = SumRepresentable(
            beforeLines.Select(line => line.RemainingAmount),
            "Full closing amount");
        var remainingAmountKzt = SumRepresentable(
            beforeLines.Select(line => line.RemainingAmountKzt),
            "Full closing KZT amount");
        request.ApplyFullClosing(
            command.ExpectedVersion,
            now,
            command.DocumentsReceivedDate,
            cycle.Snapshot?.CycleId,
            payment.FirstFullyPaidAtUtc);
        AddNewCycleAfterSuccessfulMutation(cycle);

        var allocations = beforeLines
            .Where(line => line.RemainingAmount > 0m)
            .Select(line => new AccrualClosingLineAllocation(
                line.LineId,
                line.SortOrder,
                line.RemainingAmount,
                line.RemainingAmountKzt,
                0m,
                0m))
            .ToArray();
        return new ClosingExecution(
            RequireNormalizedReason(command.Reason),
            command.DocumentsReceivedDate,
            remainingAmount,
            remainingAmount,
            remainingAmountKzt,
            0m,
            allocations);
    }

    private async Task<ClosingExecution> ReopenInTransactionAsync(
        ExpenseRequest request,
        ReopenExpenseRequestCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.Status != ExpenseRequestStatus.Closed || request.FullyPaidAtUtc is null)
        {
            throw new ExpenseRequestClosingBlockedException();
        }

        var qualifiedLines = request.Lines
            .Where(line => line.AccrualSnapshotFormsManagerialAccrual == true)
            .ToArray();
        Guid? nextCycleId = null;
        int? nextCycleNumber = null;
        if (qualifiedLines.Length > 0)
        {
            if (request.AccrualCycleId is not { } currentCycleId)
            {
                throw new ExpenseRequestClosingUnavailableException();
            }

            var currentCycle = await cycleStore.FindAsync(
                request.Id,
                currentCycleId,
                cancellationToken) ?? throw new ExpenseRequestClosingUnavailableException();
            EnsureCycleMatchesRequest(currentCycle, request);
            nextCycleId = Guid.NewGuid();
            nextCycleNumber = await cycleStore.NextCycleNumberAsync(
                request.Id,
                cancellationToken);
        }
        else if (request.AccrualCycleId is not null)
        {
            throw new ExpenseRequestClosingUnavailableException();
        }

        request.ApplyReopen(command.ExpectedVersion, now, nextCycleId);
        if (nextCycleId is { } cycleId && nextCycleNumber is { } cycleNumber)
        {
            var assessment = AccrualClosingSourceSnapshotBuilder.Capture(request);
            if (assessment.State != AccrualClosingSourceState.Ready ||
                assessment.Snapshot is not { } source)
            {
                throw new ExpenseRequestClosingUnavailableException();
            }

            cycleStore.Add(AccrualClosingCycleSnapshot.Capture(
                cycleId,
                cycleNumber,
                now,
                source));
        }

        return new ClosingExecution(
            RequireNormalizedReason(command.Reason),
            DocumentsReceivedDate: null,
            ControlAmount: null,
            AllocatedAmount: 0m,
            AllocatedAmountKzt: 0m,
            ExcessAmount: 0m,
            Lines: []);
    }

    private async Task<ExpenseRequestFullPaymentEvidence> RequireFullPaymentEvidenceAsync(
        ExpenseRequest request,
        CancellationToken cancellationToken)
    {
        var currencyId = await paymentFactReader.FindCurrencyIdAsync(
            request.CurrencyCode,
            cancellationToken);
        if (currencyId is not { } resolvedCurrencyId || resolvedCurrencyId == Guid.Empty)
        {
            throw new ExpenseRequestClosingUnavailableException();
        }

        var events = await paymentFactReader.ReadForRequestAsync(
            request.Id,
            resolvedCurrencyId,
            cancellationToken);
        return ExpenseRequestFullPaymentEvidenceResolver.Resolve(
            request.Id,
            resolvedCurrencyId,
            SumRepresentable(
                request.Lines.Select(line => line.Amount),
                "Expense-request obligation"),
            events,
            request.FullyPaidAtUtc,
            await paymentFactReader.ReadApprovedRoundingAsync(request.Id, cancellationToken));
    }

    private async Task<ResolvedClosingCycle> ResolvePaidCycleAsync(
        ExpenseRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var assessment = AccrualClosingSourceSnapshotBuilder.Capture(request);
        if (assessment.State == AccrualClosingSourceState.NoManagerialAccrualLines)
        {
            if (request.AccrualCycleId is not null)
            {
                throw new ExpenseRequestClosingUnavailableException();
            }

            return new ResolvedClosingCycle(Snapshot: null, IsNew: false);
        }
        if (assessment.State != AccrualClosingSourceState.Ready ||
            assessment.Snapshot is not { } source)
        {
            throw new ExpenseRequestClosingUnavailableException();
        }

        if (request.AccrualCycleId is { } existingCycleId)
        {
            var existing = await cycleStore.FindAsync(
                request.Id,
                existingCycleId,
                cancellationToken) ?? throw new ExpenseRequestClosingUnavailableException();
            EnsureCycleMatchesRequest(existing, request);
            return new ResolvedClosingCycle(existing, IsNew: false);
        }

        var cycleId = Guid.NewGuid();
        var cycleNumber = await cycleStore.NextCycleNumberAsync(
            request.Id,
            cancellationToken);
        return new ResolvedClosingCycle(
            AccrualClosingCycleSnapshot.Capture(
                cycleId,
                cycleNumber,
                now,
                source),
            IsNew: true);
    }

    private void AddNewCycleAfterSuccessfulMutation(ResolvedClosingCycle cycle)
    {
        if (cycle is { IsNew: true, Snapshot: { } snapshot })
        {
            cycleStore.Add(snapshot);
        }
    }

    private static IReadOnlyList<AccrualClosingLineBalance> BuildBalances(
        ExpenseRequest request,
        AccrualClosingCycleSnapshot? cycle)
    {
        if (cycle is null)
        {
            return [];
        }

        EnsureCycleMatchesRequest(cycle, request);
        var requestLines = request.Lines.ToDictionary(line => line.Id);
        return cycle.Lines.Select(line =>
        {
            var current = requestLines[line.LineId];
            return new AccrualClosingLineBalance(
                line.LineId,
                line.SortOrder,
                line.GrossAmount,
                line.GrossAmountKzt,
                current.ConfirmedClosedAmount,
                current.ConfirmedClosedAmountKzt);
        }).ToArray();
    }

    private static void EnsureCycleMatchesRequest(
        AccrualClosingCycleSnapshot cycle,
        ExpenseRequest request)
    {
        if (!cycle.MatchesRequest(request))
        {
            throw new ExpenseRequestClosingUnavailableException();
        }
    }

    private async Task<ExpenseRequest> RequireLockedRequestAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await repository.FindForWorkflowUpdateAsync(id, cancellationToken) ??
        throw new ExpenseRequestNotFoundException();

    private async Task<(Guid EmployeeId, string RoleCode)> RequireClosingAuthorityAsync(
        ExpenseRequest request,
        string commandCode,
        string actorExternalId,
        DateOnly accessDate,
        CancellationToken cancellationToken)
    {
        var actor = await employeeActorResolver.ResolveAsync(
            actorExternalId,
            cancellationToken);
        if (!actor.HasActiveEmployee || actor.EmployeeId is not { } employeeId)
        {
            throw new ActorForbiddenException();
        }

        var roles = await employeeFunctionalRoleResolver.ResolveAsync(
            employeeId,
            cancellationToken);
        var roleCode = ClosingRoleForCommand(roles, commandCode);
        if (roleCode is null)
        {
            throw new ActorForbiddenException();
        }
        if (request.OrganizationId is not { } organizationId || organizationId == Guid.Empty)
        {
            throw new ExpenseRequestClosingUnavailableException();
        }
        if (!await accessScope.CanUseAsync(
                actorExternalId,
                organizationId,
                [],
                accessDate,
                cancellationToken))
        {
            throw new ActorForbiddenException();
        }

        return (employeeId, roleCode);
    }

    private DateTimeOffset RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now == default ||
            now.Offset != TimeSpan.Zero ||
            now.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ExpenseRequestClosingUnavailableException();
        }

        return now;
    }

    private static decimal SumRepresentable(
        IEnumerable<decimal> amounts,
        string fieldName)
    {
        var total = 0m;
        foreach (var amount in amounts)
        {
            try
            {
                total = checked(total + amount);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    $"{fieldName} exceeds decimal storage range.");
            }
        }

        MoneyAmountPolicy.EnsureNonNegative(total, fieldName);
        return total;
    }

    private static string RequireNormalizedReason(string? reason) =>
        reason ?? throw new InvalidOperationException(
            "A validated closing command must carry its normalized reason.");

    private static string AuditAction(string commandCode) => commandCode switch
    {
        ExpenseRequestWorkflowCodes.ClosePartialCommandCode => "ExpenseRequestClosedPartially",
        ExpenseRequestWorkflowCodes.CloseFullCommandCode => "ExpenseRequestClosedFully",
        ExpenseRequestWorkflowCodes.ReopenCommandCode => "ExpenseRequestReopened",
        _ => throw new InvalidOperationException("Closing audit action is unsupported."),
    };

    private static string OutboxMessageType(string commandCode) => commandCode switch
    {
        ExpenseRequestWorkflowCodes.ClosePartialCommandCode =>
            "expense-request.closed-partially.v1",
        ExpenseRequestWorkflowCodes.CloseFullCommandCode =>
            "expense-request.closed.v1",
        ExpenseRequestWorkflowCodes.ReopenCommandCode =>
            "expense-request.reopened.v1",
        _ => throw new InvalidOperationException("Closing outbox message is unsupported."),
    };

    private sealed record ClosingExecution(
        string Reason,
        DateOnly? DocumentsReceivedDate,
        decimal? ControlAmount,
        decimal AllocatedAmount,
        decimal AllocatedAmountKzt,
        decimal ExcessAmount,
        IReadOnlyList<AccrualClosingLineAllocation> Lines);

    private sealed record ResolvedClosingCycle(
        AccrualClosingCycleSnapshot? Snapshot,
        bool IsNew);

    private sealed record ClosingAuditPayload(
        string CommandCode,
        string FunctionalRoleCode,
        Guid EmployeeId,
        string Reason,
        DateOnly? DocumentsReceivedDate,
        decimal? ControlAmount,
        decimal AllocatedAmount,
        decimal AllocatedAmountKzt,
        decimal ExcessAmount,
        Guid? CycleIdBefore,
        Guid? CycleIdAfter,
        IReadOnlyList<AccrualClosingLineAllocation> Lines,
        ExpenseRequestClosingStateDto Closing);
}
