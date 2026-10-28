using System.Text.Json;
using System.Text.Json.Serialization;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ReferenceData;

namespace Bms.Application.ReferenceData;

public sealed partial class CashFlowAccrualSettingsService
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record StoredSettingsResponse(
        [property: JsonRequired] Guid EmployeeId,
        [property: JsonRequired] Guid ExpectedVersion,
        [property: JsonRequired] CashFlowAccrualSettingsDto Settings,
        [property: JsonRequired] bool Changed);

    private async Task<CashFlowAccrualSettingsSnapshot> ReadVisibleAsync(Guid id, CancellationToken token)
    {
        if (id == Guid.Empty) throw new CashFlowAccrualSettingsNotFoundException();
        var current = await store.FindAsync(id, token) ?? throw new CashFlowAccrualSettingsNotFoundException();
        if (current.Id != id) throw new CashFlowAccrualSettingsUnavailableException();
        if (!actor.HasFullBusinessDataAccess && (!current.IsActive || current.IsGroup))
            throw new CashFlowAccrualSettingsNotFoundException();
        ValidateDto(ToDto(current, false));
        if (current.ExpenseItemId != current.ExpenseItem?.Id || current.CostCenterId != current.CostCenter?.Id)
            throw new CashFlowAccrualSettingsUnavailableException();
        return current;
    }

    private static CashFlowAccrualSettingsDto ToDto(CashFlowAccrualSettingsSnapshot row, bool canEdit) =>
        new(row.Id, row.Code, row.Name, row.IsActive, row.IsGroup, row.FormsManagerialAccrual,
            row.ExpenseItem, row.Version, canEdit, row.CostCenter, row.ActivityType, row.GroupName, row.FlowDirection);

    private static CashFlowAccrualSettingsCommandResult ReadReplay(StoredIdempotentResponse stored,
        Guid id, Guid employeeId, UpdateCashFlowAccrualSettingsCommand command)
    {
        if (stored.ResponseStatusCode != 200) throw new CashFlowAccrualSettingsUnavailableException();
        var result = JsonSerializer.Deserialize<StoredSettingsResponse>(stored.ResponseJson, JsonOptions)
            ?? throw new CashFlowAccrualSettingsUnavailableException();
        var dto = result.Settings;
        ValidateDto(dto);
        if (result.EmployeeId != employeeId || result.ExpectedVersion != command.ExpectedVersion
            || dto.Id != id || dto.IsGroup || !dto.CanEdit
            || dto.FormsManagerialAccrual != command.FormsManagerialAccrual
            || dto.ExpenseItem?.Id != command.ExpenseItemId
            || (command.FlowDirectionSpecified && dto.FlowDirection != command.FlowDirection)
            || (command.NameSpecified && !string.Equals(dto.Name, command.Name, StringComparison.Ordinal))
            || (command.CostCenterIdSpecified && dto.CostCenter?.Id != command.CostCenterId)
            || (command.ClassificationSpecified && (dto.ActivityType != command.ActivityType
                || !string.Equals(dto.GroupName, command.GroupName, StringComparison.OrdinalIgnoreCase)))
            || (result.Changed ? dto.Version == command.ExpectedVersion : dto.Version != command.ExpectedVersion))
            throw new CashFlowAccrualSettingsUnavailableException();
        return new(dto, result.Changed, true);
    }

    private static void ValidateDto(CashFlowAccrualSettingsDto? dto)
    {
        if (dto is null || dto.Id == Guid.Empty || dto.Version == Guid.Empty
            || !IsText(dto.Code, 20) || !IsText(dto.Name, 150)
            || dto.FlowDirection is not (null or "Payment" or "Receipt")
            || (dto.ActivityType is { } activity && !CashFlowActivityType.IsDefined(activity))
            || (dto.GroupName is not null && !IsText(dto.GroupName, 150))
            || (dto.ExpenseItem is { } expense
                && (expense.Id == Guid.Empty || !IsText(expense.Code, 9)
                    || !IsText(expense.Name, 250) || expense.ParentId is not null))
            || (dto.CostCenter is { } center && (center.Id == Guid.Empty
                || !IsText(center.Code, 100) || !IsText(center.Name, 500) || (dto.IsGroup)))
            || (dto.FormsManagerialAccrual ? dto.ExpenseItem is null : dto.ExpenseItem is not null))
            throw new CashFlowAccrualSettingsUnavailableException();
    }

    private static bool IsText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && !value.Any(char.IsControl);

    private static void RequireKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 127 || key != key.Trim() || key.Any(char.IsControl))
            throw new InvalidIdempotencyKeyException("Idempotency-Key is invalid.");
    }

    private static bool MustWrap(Exception exception) => !ProcessFatalExceptionClassifier.IsFatal(exception)
        && exception is not (OperationCanceledException or ActorNotAuthenticatedException or ActorForbiddenException
            or CashFlowAccrualSettingsNotFoundException or CashFlowAccrualSettingsVersionConflictException
            or CashFlowAccrualSettingsUnavailableException or DomainRuleViolationException
            or InvalidIdempotencyKeyException or IdempotencyConflictException);
}
