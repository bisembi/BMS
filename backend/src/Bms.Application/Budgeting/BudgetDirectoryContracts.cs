using System.Text.Json.Serialization;

namespace Bms.Application.Budgeting;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BudgetDirectoryMemberCommand(
    [property: JsonRequired] Guid EmployeeId,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] bool IsActive);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateBudgetDirectoryCommand(
    [property: JsonRequired] Guid? ExpectedConcurrencyStamp,
    [property: JsonRequired] string Abbreviation,
    [property: JsonRequired] IReadOnlyList<BudgetDirectoryMemberCommand> Members);

public sealed record BudgetDirectoryMemberDto(
    Guid EmployeeId, string Code, string Name, string Kind, bool IsActive, bool EmployeeIsActive);

public sealed record BudgetDirectorySettingsDto(
    Guid CostCenterId, string Code, string Name, string? Abbreviation,
    Guid? ConcurrencyStamp, bool NumberingInitialized, IReadOnlyList<BudgetDirectoryMemberDto> Members,
    bool OnlyCostDepartment = false, Guid? PurposeConcurrencyStamp = null);

public sealed record BudgetDirectoryOptionDto(Guid Id, string Code, string Name, bool IsActive,
    bool OnlyCostDepartment = false, Guid? PurposeConcurrencyStamp = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetCostCenterPurpose457Command(
    [property: JsonRequired] Guid? ExpectedConcurrencyStamp,
    [property: JsonRequired] bool OnlyCostDepartment);

public sealed record BudgetDirectoryOptionPageDto(
    IReadOnlyList<BudgetDirectoryOptionDto> Items, int Skip, int Take, int Total);

public sealed class BudgetDirectoryNotFoundException : Exception;
public sealed class BudgetPrefixConflictException : Exception;
public sealed class BudgetNumberingNotInitializedException : Exception;
