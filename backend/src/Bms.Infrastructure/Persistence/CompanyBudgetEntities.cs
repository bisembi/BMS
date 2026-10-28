namespace Bms.Infrastructure.Persistence;

// A deletion hides a version without severing immutable approval/baseline references.
public sealed class CompanyBudgetVersionDeletion
{
    public Guid VersionId { get; set; }
    public Guid EmployeeId { get; set; }
    public string ActorExternalId { get; set; } = string.Empty;
    public DateTimeOffset DeletedAtUtc { get; set; }
}

public sealed class CompanyBudgetInstanceCreation
{
    public Guid BudgetInstanceId { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public string Source { get; set; } = "LegacyUnknown";
}

public sealed class CompanyBudgetVersion
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ConcurrencyStamp { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByEmployeeId { get; set; }
    public string CreatedByExternalId { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public int? PublishedInstanceCount { get; set; }
}

public sealed class CompanyBudgetVersionInstance
{
    public Guid VersionId { get; set; }
    public Guid BudgetInstanceId { get; set; }
}

public sealed class CompanyBudgetActiveVersion
{
    public byte Id { get; set; } = 1;
    public Guid VersionId { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTimeOffset ActivatedAtUtc { get; set; }
    public Guid? ActivatedByEmployeeId { get; set; }
    public string ActivatedByExternalId { get; set; } = string.Empty;
}

public sealed class CompanyBudgetApprovalBaseline
{
    public Guid ReviewId { get; set; }
    public Guid VersionId { get; set; }
    public Guid ActiveConcurrencyStamp { get; set; }
}

public sealed class CompanyExpenseApprovalBaseline
{
    public Guid ExpenseRequestId { get; set; }
    public int SnapshotNumber { get; set; }
    public Guid VersionId { get; set; }
    public Guid ActiveConcurrencyStamp { get; set; }
}

public sealed class CompanyBudgetActivation
{
    public Guid Id { get; set; }
    public Guid VersionId { get; set; }
    public Guid? PreviousVersionId { get; set; }
    public Guid ActiveConcurrencyStamp { get; set; }
    public DateTimeOffset ActivatedAtUtc { get; set; }
    public Guid? EmployeeId { get; set; }
    public string ActorExternalId { get; set; } = string.Empty;
}
