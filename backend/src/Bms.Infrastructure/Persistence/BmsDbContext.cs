using Bms.Domain.Budgeting;
using Bms.Application.Authorization;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class BmsDbContext(DbContextOptions<BmsDbContext> options,
    IActiveRoleSelection? activeRoleSelection = null,
    Bms.Application.Abstractions.IClock? clock = null) : DbContext(options)
{
    internal DateTimeOffset StaffNow471 => clock?.UtcNow ?? DateTimeOffset.UtcNow;
    internal bool BudgetRegisterEnabled343 { get; set; }
    internal bool BudgetRegisterReadOnly343 { get; set; }
    internal bool BudgetRegisterFinancialWrites343 { get; set; }
    internal bool BudgetRegisterLimitsOnly454 { get; set; }
    internal HashSet<Guid> BudgetRegisterTouchedRequests343 { get; } = [];

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (activeRoleSelection?.Substitution471 is { } substitution)
        {
            await StaffSubstitutionGuard471.ValidateAsync(this, cancellationToken);
            var audits = ChangeTracker.Entries<BusinessAuditEntry>().Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity).ToArray();
            foreach (var audit in audits)
            {
                if (audit.ActorExternalId != substitution.DeputyActorExternalId)
                    throw new Bms.Application.Common.ActorForbiddenException();
                if (!StaffSubstitutionActions471.Local.Any(a => a.Id == audit.Id))
                    StaffSubstitutionActions471.Add(new StaffSubstitutionAction471 { Id = audit.Id,
                        DocumentId = substitution.DocumentId, DeputyEmployeeId = substitution.DeputyEmployeeId,
                        PrincipalEmployeeId = substitution.PrincipalEmployeeId, RoleCode = substitution.RoleCode,
                        ActorExternalId = substitution.DeputyActorExternalId, AtUtc = audit.OccurredAtUtc,
                        ObjectType = audit.ObjectType, ObjectId = audit.ObjectId, Action = audit.Action });
            }
        }
        var financial=ChangeTracker.Entries().Any(e=>e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            && e.Entity is ExpenseRequest or ExpenseRequestLine or BudgetPosition or BudgetRequestProjection
                or BudgetInstance or BudgetInstanceLine or BudgetAdjustmentEntity or CompanyBudgetActiveVersion
                or CompanyBudgetVersion or CompanyBudgetVersionInstance or CompanyBudgetActivation
                or AccountingPaymentLink or AccountingPaymentLinkLine or ExpenseRequestPaymentFact);
        if(financial&&BudgetRegisterEnabled343&&BudgetRegisterReadOnly343)
            throw new InvalidOperationException("A read-only budget-register transaction cannot write financial data.");
        foreach(var entry in ChangeTracker.Entries<ExpenseRequest>().Where(e=>e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            BudgetRegisterTouchedRequests343.Add(entry.Entity.Id);
        var count=await base.SaveChangesAsync(acceptAllChangesOnSuccess,cancellationToken);
        if(financial)BudgetRegisterFinancialWrites343=true;
        return count;
    }

    public DbSet<AttachmentUploadSession> AttachmentUploadSessions => Set<AttachmentUploadSession>();
    public DbSet<StaffSubstitutionDocument471> StaffSubstitutions471 => Set<StaffSubstitutionDocument471>();
    public DbSet<StaffSubstitutionCommand471> StaffSubstitutionCommands471 => Set<StaffSubstitutionCommand471>();
    public DbSet<StaffSubstitutionAction471> StaffSubstitutionActions471 => Set<StaffSubstitutionAction471>();
    public DbSet<AttachmentFile> AttachmentFiles => Set<AttachmentFile>();
    internal IActiveRoleSelection? ActiveRoleSelection => activeRoleSelection;
    public DbSet<CompanyBudgetVersion> CompanyBudgetVersions => Set<CompanyBudgetVersion>();
    public DbSet<CompanyBudgetVersionDeletion> CompanyBudgetVersionDeletions => Set<CompanyBudgetVersionDeletion>();
    public DbSet<CompanyBudgetInstanceCreation> CompanyBudgetInstanceCreations => Set<CompanyBudgetInstanceCreation>();
    public DbSet<CompanyBudgetVersionInstance> CompanyBudgetVersionInstances => Set<CompanyBudgetVersionInstance>();
    public DbSet<CompanyBudgetActiveVersion> CompanyBudgetActiveVersions => Set<CompanyBudgetActiveVersion>();
    public DbSet<CompanyBudgetActivation> CompanyBudgetActivations => Set<CompanyBudgetActivation>();
    public DbSet<CompanyBudgetApprovalBaseline> CompanyBudgetApprovalBaselines => Set<CompanyBudgetApprovalBaseline>();
    public DbSet<CompanyExpenseApprovalBaseline> CompanyExpenseApprovalBaselines => Set<CompanyExpenseApprovalBaseline>();
    public DbSet<ExpenseRequest> ExpenseRequests => Set<ExpenseRequest>();
    public DbSet<ExpenseRequestLine> ExpenseRequestLines => Set<ExpenseRequestLine>();
    public DbSet<ExpenseRequestAttachmentEntity> ExpenseRequestAttachments =>
        Set<ExpenseRequestAttachmentEntity>();
    public DbSet<BudgetPosition> BudgetPositions => Set<BudgetPosition>();
    public DbSet<BudgetRequestProjection> BudgetRequestProjections => Set<BudgetRequestProjection>();
    public DbSet<BudgetInstance> BudgetInstances => Set<BudgetInstance>();
    public DbSet<BudgetAdjustmentEntity> BudgetAdjustments => Set<BudgetAdjustmentEntity>();
    public DbSet<BudgetApprovalSubmission> BudgetApprovalSubmissions => Set<BudgetApprovalSubmission>();
    public DbSet<BudgetInstanceLine> BudgetInstanceLines => Set<BudgetInstanceLine>();
    public DbSet<BddsSnapshot> BddsSnapshots => Set<BddsSnapshot>();
    public DbSet<BddsSnapshotRow> BddsSnapshotRows => Set<BddsSnapshotRow>();
    public DbSet<BddsSnapshotBudgetInstance> BddsSnapshotBudgetInstances => Set<BddsSnapshotBudgetInstance>();
    public DbSet<CashFlowActualsSnapshot> CashFlowActualsSnapshots => Set<CashFlowActualsSnapshot>();
    public DbSet<CashFlowActualsMonth> CashFlowActualsMonths => Set<CashFlowActualsMonth>();
    public DbSet<CashFlowActualsArticle> CashFlowActualsArticles => Set<CashFlowActualsArticle>();
    public DbSet<CashFlowActualsRow> CashFlowActualsRows => Set<CashFlowActualsRow>();
    public DbSet<BudgetCostCenterSettings> BudgetCostCenterSettings => Set<BudgetCostCenterSettings>();
    public DbSet<BudgetPrefixOwner> BudgetPrefixOwners => Set<BudgetPrefixOwner>();
    public DbSet<BudgetCostCenterCounter> BudgetCostCenterCounters => Set<BudgetCostCenterCounter>();
    public DbSet<ExpenseRequestCostCenterCounter> ExpenseRequestCostCenterCounters =>
        Set<ExpenseRequestCostCenterCounter>();
    public DbSet<ExpenseRequestNumberLeaseRow> ExpenseRequestNumberLeases => Set<ExpenseRequestNumberLeaseRow>();
    public DbSet<BudgetCostCenterMembership> BudgetCostCenterMemberships => Set<BudgetCostCenterMembership>();
    public DbSet<BusinessAuditEntry> BusinessAuditEntries => Set<BusinessAuditEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OrganizationReference> Organizations => Set<OrganizationReference>();
    public DbSet<CostCenterReference> CostCenters => Set<CostCenterReference>();
    public DbSet<CostCenterPurpose457> CostCenterPurposes457 => Set<CostCenterPurpose457>();
    public DbSet<CashFlowItemReference> CashFlowItems => Set<CashFlowItemReference>();
    public DbSet<ExpenseItemReference> ExpenseItems => Set<ExpenseItemReference>();
    public DbSet<CounterpartyReference> Counterparties => Set<CounterpartyReference>();
    public DbSet<ContractReference> Contracts => Set<ContractReference>();
    public DbSet<CurrencyReference> Currencies => Set<CurrencyReference>();
    public DbSet<CurrencyRateReference> CurrencyRates => Set<CurrencyRateReference>();
    public DbSet<ExpenseRequestPaymentFact> ExpenseRequestPaymentFacts =>
        Set<ExpenseRequestPaymentFact>();
    public DbSet<AccountingPaymentLink> AccountingPaymentLinks => Set<AccountingPaymentLink>();
    public DbSet<AccountingPaymentLinkLine> AccountingPaymentLinkLines => Set<AccountingPaymentLinkLine>();
    public DbSet<OverpaymentSource> OverpaymentSources => Set<OverpaymentSource>();
    public DbSet<OverpaymentAllocation> OverpaymentAllocations => Set<OverpaymentAllocation>();
    public DbSet<OverpaymentDeficit> OverpaymentDeficits => Set<OverpaymentDeficit>();
    public DbSet<OverpaymentDeficitSettlement> OverpaymentDeficitSettlements =>
        Set<OverpaymentDeficitSettlement>();
    public DbSet<ManualOverpaymentDeficitCorrection> ManualOverpaymentDeficitCorrections =>
        Set<ManualOverpaymentDeficitCorrection>();
    public DbSet<ActorOrganizationGrant> ActorOrganizationGrants => Set<ActorOrganizationGrant>();
    public DbSet<ActorCostCenterAccess> ActorCostCenterAccess => Set<ActorCostCenterAccess>();

    // Read-only projection of employee identities and functional-role assignments.
    // Runtime readers consume the imported mapping; merely storing an employee or
    // mapping never grants a role or an organization/cost-center scope by itself.
    public DbSet<EmployeeReference> Employees => Set<EmployeeReference>();
    public DbSet<FunctionalRoleReference> FunctionalRoles => Set<FunctionalRoleReference>();
    public DbSet<EmployeeFunctionalRole> EmployeeFunctionalRoles => Set<EmployeeFunctionalRole>();
    public DbSet<EmployeeActorMapping> EmployeeActorMappings => Set<EmployeeActorMapping>();
    public DbSet<ExpenseRequestSubstitution> ExpenseRequestSubstitutions =>
        Set<ExpenseRequestSubstitution>();
    public DbSet<BudgetOwnerCostCenterAssignment> BudgetOwnerCostCenterAssignments =>
        Set<BudgetOwnerCostCenterAssignment>();
    public DbSet<CfoInitiatorCostCenterAssignment> CfoInitiatorCostCenterAssignments =>
        Set<CfoInitiatorCostCenterAssignment>();

    // Dormant immutable storage for future submission route snapshots. It has
    // no reader, writer, endpoint or workflow authority until OPEN-01/OPEN-02
    // are formally activated.
    public DbSet<PersistedApprovalRouteSnapshot> ApprovalRouteSnapshots =>
        Set<PersistedApprovalRouteSnapshot>();
    public DbSet<PersistedApprovalRouteStage> ApprovalRouteStages =>
        Set<PersistedApprovalRouteStage>();
    public DbSet<PersistedApprovalRouteStageParticipant> ApprovalRouteStageParticipants =>
        Set<PersistedApprovalRouteStageParticipant>();

    // Immutable evidence consumed by the active accrual-closing commands.
    // The application service owns authorization, locking and transaction scope.
    public DbSet<PersistedAccrualClosingCycleSnapshot> AccrualClosingCycleSnapshots =>
        Set<PersistedAccrualClosingCycleSnapshot>();
    public DbSet<PersistedAccrualClosingCycleLine> AccrualClosingCycleLines =>
        Set<PersistedAccrualClosingCycleLine>();

    public DbSet<AccrualNotification> AccrualNotifications => Set<AccrualNotification>();
    public DbSet<AccrualNotificationIssue> AccrualNotificationIssues => Set<AccrualNotificationIssue>();

    // Workflow lives in data because OPEN-01 (status names) and OPEN-02 (the
    // Status x Role x Command matrix) are not approved by the business yet.
    public DbSet<DocumentStatus> DocumentStatuses => Set<DocumentStatus>();
    public DbSet<WorkflowCommand> WorkflowCommands => Set<WorkflowCommand>();
    public DbSet<WorkflowPermission> WorkflowPermissions => Set<WorkflowPermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BmsDbContext).Assembly);
        modelBuilder.Entity<CompanyBudgetVersion>().HasQueryFilter(v =>
            !CompanyBudgetVersionDeletions.Any(d => d.VersionId == v.Id));
        modelBuilder.Entity<ExpenseRequest>().HasQueryFilter(request =>
            !BusinessAuditEntries.Any(entry => entry.ObjectType == "ExpenseRequest"
                && entry.ObjectId == request.Id.ToString() && entry.Action == "ExpenseRequestDraftDeleted"));
        // Append-only audit is the durable tombstone. Physical document,
        // approval snapshots and their immutable baseline FKs stay intact.
        modelBuilder.Entity<BudgetInstance>().HasQueryFilter(instance =>
            !BusinessAuditEntries.Any(entry => entry.ObjectType == "BudgetInstance"
                && entry.ObjectId == instance.Id.ToString() && entry.Action == "BudgetInstanceDraftDeleted"));
    }

}
