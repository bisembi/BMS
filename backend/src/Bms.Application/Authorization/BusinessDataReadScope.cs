using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.Authorization;

/// <summary>Visibility only. Never use this policy to authorize a mutation.</summary>
public static class BusinessDataReadPolicy
{
    public const string ChiefAccountantRoleCode = "CHIEF_ACCOUNTANT";

    public static IReadOnlyList<string> GlobalRoleCodes { get; } = Array.AsReadOnly(new[]
    {
        ExpenseRequestWorkflowCodes.AuditorRoleCode,
        ExpenseRequestWorkflowCodes.TreasurerRoleCode,
        ExpenseRequestWorkflowCodes.FinancialControllerRoleCode,
        ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode,
        ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode,
        ExpenseRequestWorkflowCodes.AccountantRoleCode,
        ChiefAccountantRoleCode,
        ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode,
    });
    public static bool HasGlobalRole(EmployeeFunctionalRoleResolution roles) =>
        GlobalRoleCodes.Any(roles.HasExactRole);
}

public sealed class BusinessDataReadScope
{
    public static BusinessDataReadScope All { get; } = new(true, []);
    public static BusinessDataReadScope None { get; } = new(false, []);
    public BusinessDataReadScope(bool canReadAll, IEnumerable<Guid> costCenterIds, IEnumerable<Guid>? nsiOrganizationIds = null,
        IEnumerable<Guid>? payrollRequestIds = null)
    {
        ArgumentNullException.ThrowIfNull(costCenterIds);
        var ids = costCenterIds.ToArray();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new BudgetOwnerCreationAuthorityUnavailableException();
        CanReadAll = canReadAll;
        CostCenterIds = Array.AsReadOnly(ids.Order().ToArray());
        var organizations = (nsiOrganizationIds ?? []).ToArray();
        if (organizations.Any(id => id == Guid.Empty) || organizations.Distinct().Count() != organizations.Length)
            throw new BudgetOwnerCreationAuthorityUnavailableException();
        NsiOrganizationIds = Array.AsReadOnly(organizations.Order().ToArray());
        var requests = (payrollRequestIds ?? []).ToArray();
        if (requests.Any(id => id == Guid.Empty) || requests.Distinct().Count() != requests.Length)
            throw new BudgetOwnerCreationAuthorityUnavailableException();
        PayrollRequestIds = Array.AsReadOnly(requests.Order().ToArray());
    }
    public bool CanReadAll { get; }
    public IReadOnlyList<Guid> CostCenterIds { get; }
    public IReadOnlyList<Guid> NsiOrganizationIds { get; }
    public IReadOnlyList<Guid> PayrollRequestIds { get; }
    public bool HasAny => CanReadAll || CostCenterIds.Count > 0 || NsiOrganizationIds.Count > 0 || PayrollRequestIds.Count > 0;
    public bool CanReadNsiOrganization(Guid? organizationId) => organizationId is Guid id && NsiOrganizationIds.Contains(id);
    public bool Contains(Guid id) => id != Guid.Empty && (CanReadAll || CostCenterIds.Contains(id));
    // Cards are indivisible; null line CFOs inherit the header. A foreign line
    // denies the whole card, including totals, payments and attachments.
    public bool Contains(ExpenseRequest request) =>
        Contains(request.DefaultCostCenterId, request.Lines.Select(line => line.CostCenterId))
        || (request.Status == ExpenseRequestStatus.AwaitingNsi && CanReadNsiOrganization(request.OrganizationId))
        || (request.PaymentOperation?.IsPayrollAdvance == true && PayrollRequestIds.Contains(request.Id));
    public bool Contains(Guid? headerCostCenterId, IEnumerable<Guid?> lineCostCenterIds) => CanReadAll
        || (headerCostCenterId is Guid header && Contains(header)
            && lineCostCenterIds.All(id => !id.HasValue || Contains(id.Value)));
}
