using System.Text.Json.Serialization;
using Bms.Application.Authorization;
using Bms.Domain.Common;

namespace Bms.Application.Budgeting;

public sealed record CompanyBudgetInstanceDto(Guid Id, string Number, Guid CostCenterId,
    string CostCenterCode, string CostCenterName, DateOnly ValidFrom, DateOnly ValidTo,
    string Status, Guid ConcurrencyStamp);
public sealed record CompanyBudgetOverlapDto(Guid CostCenterId, string CostCenterName,
    DateOnly BudgetMonth, IReadOnlyList<Guid> BudgetInstanceIds, Guid? WinnerBudgetInstanceId, string Warning);
public sealed record CompanyBudgetVersionItemDto(Guid Id, string Name, Guid ConcurrencyStamp,
    bool IsActive, bool IsPublished, int InstanceCount, bool CanEdit, bool CanActivate, bool IsPartial = false,
    bool CanDelete = false, bool CanEditActiveComposition = false);
public sealed record CompanyBudgetVersionDto(Guid Id, string Name, Guid ConcurrencyStamp,
    bool IsActive, bool IsPublished, int InstanceCount, bool CanEdit, bool CanActivate,
    IReadOnlyList<Guid> BudgetInstanceIds, IReadOnlyList<CompanyBudgetInstanceDto> Instances,
    IReadOnlyList<CompanyBudgetOverlapDto> Overlaps, Guid ActiveVersionId, Guid ActiveConcurrencyStamp,
    bool IsPartial = false, bool ScopeLimited = false, bool CanDelete = false, bool CanEditActiveComposition = false);
public sealed record CompanyBudgetVersionPageDto(IReadOnlyList<CompanyBudgetVersionItemDto> Items,
    int Skip, int Take, int Total, bool CanCreate, Guid ActiveVersionId, Guid ActiveConcurrencyStamp, bool ScopeLimited = false);
public sealed record CompanyBudgetOptionsDto(IReadOnlyList<CompanyBudgetInstanceDto> Items,
    int Skip, int Take, int Total, bool CanCreate, bool CanActivate, Guid ActiveVersionId,
    Guid ActiveConcurrencyStamp, IReadOnlyList<BddsCostCenterDto> CostCenters);
public sealed record CompanyBudgetVersionIdentity(Guid VersionId, Guid ActiveConcurrencyStamp);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCompanyBudgetVersionCommand(string Name, IReadOnlyList<Guid> BudgetInstanceIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCompanyBudgetVersionCommand(string Name, IReadOnlyList<Guid> BudgetInstanceIds, Guid ExpectedConcurrencyStamp);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateActiveCompanyBudgetCompositionCommand(IReadOnlyList<Guid> BudgetInstanceIds,
    Guid ExpectedConcurrencyStamp, Guid ExpectedActiveConcurrencyStamp);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviseCompanyBudgetVersionCommand(string Name, IReadOnlyList<Guid> BudgetInstanceIds, Guid ExpectedConcurrencyStamp);
public sealed record CompanyBudgetVersionHistoryDto(Guid AuditId, DateTimeOffset OccurredAtUtc, string Action,
    Guid? SourceVersionId, string? SourceVersionName, int? PreviousInstanceCount, int InstanceCount);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivateCompanyBudgetVersionCommand(Guid ExpectedConcurrencyStamp, Guid ExpectedActiveConcurrencyStamp);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteCompanyBudgetVersionCommand(Guid ExpectedConcurrencyStamp,
    Guid? ReplacementVersionId = null, Guid? ExpectedReplacementConcurrencyStamp = null,
    Guid? ExpectedActiveConcurrencyStamp = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompanyBudgetVersionDeletionReceipt(Guid Id, string Name, bool Deleted, int RetainedInstanceCount);
public sealed class CompanyBudgetVersionNotFoundException : Exception;
public sealed class CompanyBudgetVersionConflictException : Exception;
public sealed class CompanyBudgetPlanUnavailableException : Exception;
public sealed class BudgetInstanceInCompanyVersionException : Exception;

public static class CompanyBudgetCompositionPolicy
{
    public static string Validate(string? name, IReadOnlyList<Guid>? ids)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name != name.Trim() || name.Any(char.IsControl)
            || ids is null || ids.Count is < 1 or > 1000 || ids.Any(id => id == Guid.Empty)
            || ids.Distinct().Count() != ids.Count)
            throw new DomainRuleViolationException("A named version requires 1–1000 distinct budget documents.");
        return name;
    }
}

public interface ICompanyBudgetVersionStore
{
    Task<CompanyBudgetVersionPageDto> ListAsync(int skip, int take, BusinessDataReadScope scope,
        bool manage, bool activate, CancellationToken token);
    Task<CompanyBudgetOptionsDto> OptionsAsync(string query, Guid? center, int skip, int take,
        BusinessDataReadScope scope, bool manage, bool activate, CancellationToken token);
    Task<CompanyBudgetVersionDto> ReadAsync(Guid id, BusinessDataReadScope scope, bool manage, bool activate, CancellationToken token);
    Task<IReadOnlyList<CompanyBudgetVersionHistoryDto>> HistoryAsync(Guid id, int skip, int take, CancellationToken token)
        => throw new NotSupportedException();
    Task CreateAsync(Guid id, CreateCompanyBudgetVersionCommand command, Guid employee, string external,
        DateTimeOffset now, CancellationToken token);
    Task UpdateAsync(Guid id, UpdateCompanyBudgetVersionCommand command, CancellationToken token);
    Task UpdateActiveCompositionAsync(Guid id, UpdateActiveCompanyBudgetCompositionCommand command, Guid employee,
        string external, DateTimeOffset now, CancellationToken token) => throw new NotSupportedException();
    Task ActivateAsync(Guid id, ActivateCompanyBudgetVersionCommand command, Guid employee, string external,
        DateTimeOffset now, CancellationToken token);
    // Implementations must flush only the target membership and header inside
    // the caller's exclusive Serializable company-budget transaction.
    Task<CompanyBudgetVersionDeletionReceipt> DeleteAsync(Guid id, DeleteCompanyBudgetVersionCommand command,
        CancellationToken token) => throw new NotSupportedException();
    Task<CompanyBudgetVersionDeletionReceipt> DeleteAsync(Guid id, DeleteCompanyBudgetVersionCommand command,
        Guid employee, string external, DateTimeOffset now, CancellationToken token) => DeleteAsync(id, command, token);
}
