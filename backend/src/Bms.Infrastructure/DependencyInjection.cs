using Bms.Application.Treasury;
using Bms.Application.Accruals;
using Bms.Application.Accruals.Notifications;
using Bms.Application.Abstractions;
using Bms.Application.Audit;
using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Bms.Application.ExpenseRequests;
using Bms.Application.Integration;
using Bms.Application.OverpaymentDeficits;
using Bms.Application.ReferenceData;
using Bms.Infrastructure.Health;
using Bms.Infrastructure.Attachments;
using Bms.Infrastructure.Persistence;
using Bms.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bms.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBmsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BmsDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:BmsDatabase is required. Do not place production credentials in source control.");
        }

        var commandTimeoutSeconds = DatabaseCommandTimeoutConfiguration.Resolve(configuration);
        services.AddDbContext<BmsDbContext>(options =>
            options.UseSqlServer(connectionString, sqlServer =>
            {
                sqlServer.CommandTimeout(commandTimeoutSeconds);
                sqlServer.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            }));
        services.AddScoped<IExpenseRequestRepository, ExpenseRequestRepository>();
        services.AddScoped<TaskMailSource>();
        services.AddScoped<IStaffSubstitutionStore471, StaffSubstitutionStore471>();
        services.AddScoped<Bms.Application.Tasks.ReturnedRequestNotices377>();
        services.AddScoped<Bms.Application.Tasks.IReturnedRequestNoticeReader377,ReturnedRequestNoticeReader377>();
        services.AddScoped<TaskMailQueue>();
        services.AddScoped<Bms.Infrastructure.Mail.ITaskMailRecipientDirectory,ActiveDirectoryEmployeeDirectory>();
        services.AddSingleton<Bms.Infrastructure.Mail.ITaskMailTransport,Bms.Infrastructure.Mail.SmtpTaskMailTransport>();
        services.AddScoped<INsiWorkQueueScopeReader, NsiWorkQueueScopeReader>();
        services.AddScoped<IPayrollRequestScope372, PayrollRequestScope372>();
        services.AddSingleton(new PayrollAdvanceCreationPolicy372(configuration.GetValue("Bms:PayrollAdvance:CreationEnabled", true)));
        services.AddScoped<TreasuryPaymentDateService>();
        services.AddScoped<ITreasuryPaymentDateService>(provider => provider.GetRequiredService<TreasuryPaymentDateService>());
        services.AddScoped<ITreasuryPaymentDateJournal>(provider => provider.GetRequiredService<TreasuryPaymentDateService>());
        services.AddScoped<IExpenseRequestNumberAllocator, ExpenseRequestNumberAllocator>();
        services.AddScoped<ExpenseRequestAttachmentStore>();
        services.AddSingleton<AttachmentFileStorage>();
        services.AddScoped<AttachmentPreviewService>();
        services.AddScoped<IAttachmentPreviewCache, AttachmentPreviewCache>();
        services.AddSingleton<IAttachmentPreviewRenderer, AttachmentPreviewRenderer>();
        services.AddScoped<AttachmentUploadService>();
        services.AddScoped<IExpenseRequestAttachmentUploadService>(provider => provider.GetRequiredService<AttachmentUploadService>());
        services.AddScoped<IExpenseRequestAttachmentBinder>(provider => provider.GetRequiredService<AttachmentUploadService>());
        services.AddScoped<IExpenseRequestAttachmentStore>(provider =>
            provider.GetRequiredService<ExpenseRequestAttachmentStore>());
        services.AddScoped<IExpenseRequestAttachmentReader>(provider =>
            provider.GetRequiredService<ExpenseRequestAttachmentStore>());
        services.AddSingleton<IExpenseRequestAttachmentContentScanner>(
            AttachmentScannerConfiguration.Resolve(configuration));
        services.AddScoped<IExpenseRequestExportRepository, ExpenseRequestExportRepository>();
        services.AddScoped<IExpenseRequestHistoryRepository, ExpenseRequestHistoryRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddSingleton<AccountingReplicaLocation>();
        services.AddScoped<IAccountingPaymentSourceReader, AccountingPaymentSourceReader>();
        services.AddScoped<IAccountingPaymentCoverageReader, AccountingPaymentCoverageReader>();
        services.AddScoped<AccountingPaymentReconciliation>();
        var accountingPayments=AccountingPaymentSettings.Read(configuration);
        services.AddSingleton(accountingPayments);
        services.AddSingleton(new SourceCashBudgetControlPolicy(accountingPayments.BudgetControlEnabled));
        if(accountingPayments.BudgetControlEnabled)
            services.AddScoped<IBudgetCashConsumptionReader, BudgetCashConsumptionReader>();
        services.AddScoped<IDirectDdsReplicaReader, DirectDdsReplicaReader>();
        services.AddScoped<IExecutionReportReader, ExecutionReportReader>();
        services.AddScoped<ICompanyBudgetVersionStore, CompanyBudgetVersionStore>();
        services.AddScoped<CompanyBudgetVersionService>();
        services.AddScoped<IExpenseRequestPaymentFactReader, ExpenseRequestPaymentFactReader>();
        services.AddScoped<IExpenseRequestListBusinessColumnsReader, ExpenseRequestListBusinessColumnsReader>();
        services.AddScoped<IExpenseRequestContractValidator, ExpenseRequestContractValidator>();
        services.AddScoped<IAccrualDepartmentCatalog, AccrualDepartmentCatalog>();
        services.AddScoped<IExpensePaymentOperationCatalog, ExpensePaymentOperationCatalog>();
        services.AddSingleton(new ApprovalAccrualEditPolicy(configuration.GetValue("BmsAccrualEditing:Enabled",true)));
        services.AddScoped<IPaymentCalendarReader, EfCorePaymentCalendarReader>();
        services.AddScoped<IPaymentCalendarExportReader, EfCorePaymentCalendarReader>();
        services.AddScoped<IDatedPaymentCalendarReader, EfCoreDatedPaymentCalendarReader>();
        services.AddScoped<IGoodsCalendarArticles458, GoodsCalendarArticles458>();
        services.AddScoped<IGoodsCalendarActuals469, GoodsCalendarOddsReader469>();
        services.AddScoped<GoodsCalendarService458>();
        services.AddSingleton<IDatedPaymentCalendarTimeZoneProvider>(
            new ConfiguredDatedPaymentCalendarTimeZoneProvider(configuration));
        services.AddScoped<IActiveAccrualReportReader, EfCoreActiveAccrualReportReader>();
        services.AddScoped<ISubmissionBudgetSnapshotReader, SubmissionBudgetSnapshotReader>();
        services.AddScoped<IAccrualSnapshotSourceResolver, SqlServerAccrualSnapshotSourceResolver>();
        services.AddScoped<IAccrualSnapshotMaterializer, EfCoreAccrualSnapshotMaterializer>();
        services.AddScoped<ICurrencyRateResolver, SqlServerCurrencyRateResolver>();
        var clock = new SystemClock();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ICurrencyRateUsagePolicy>(
            new ConfiguredCurrencyRateUsagePolicy(configuration, clock));
        var agingPolicy = new ConfiguredAccrualAgingPolicy(configuration, clock);
        services.AddSingleton<IAccrualAgingPolicy>(agingPolicy);
        var notificationSettings = AccrualNotificationConfiguration.Resolve(configuration);
        if (notificationSettings.Enabled && agingPolicy.GetCurrentContext() is null)
        {
            throw new InvalidOperationException("Enabled accrual control requires a configured business calendar.");
        }
        services.AddSingleton(notificationSettings);
        services.AddSingleton<AccrualNotificationRunState>();
        if (notificationSettings.Enabled)
        {
            services.AddScoped<IAccrualNotificationControl, EfCoreAccrualNotificationControl>();
        }
        else
        {
            services.AddSingleton<IAccrualNotificationControl>(DisabledAccrualNotificationControl.Instance);
        }
        services.AddScoped<IAccrualNotificationSweep, EfCoreAccrualNotificationSweep>();
        services.AddScoped<IAccrualNotificationReader, EfCoreAccrualNotificationReader>();
        services.AddScoped<AccrualNotificationReadService>();
        services.AddScoped<IAccrualNotificationAdministrationReader, EfCoreAccrualNotificationAdministrationReader>();
        services.AddScoped<AccrualNotificationAdministrationService>();
        services.AddScoped<IAccessScope, SqlServerAccessScope>();
        services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
        services.AddScoped<IExpenseRequestInitiatorDirectory, ExpenseRequestInitiatorDirectory>();
        services.AddScoped<IReferenceDataMaintenanceStore, ReferenceDataMaintenanceStore>();
        services.AddScoped<IOrganizationDirectoryRepository, OrganizationDirectoryRepository>();
        services.AddScoped<OrganizationDirectoryService>();
        services.AddScoped<
            IBudgetOwnerCostCenterAssignmentDirectoryReader,
            BudgetOwnerCostCenterAssignmentDirectoryReader>();
        services.AddScoped<
            IBudgetOwnerCostCenterAssignmentDirectoryWriter,
            BudgetOwnerCostCenterAssignmentDirectoryWriter>();
        services.AddScoped<
            IBudgetOwnerCostCenterAuthorityReader,
            BudgetOwnerCostCenterAuthorityReader>();
        services.AddScoped<IOutboxMonitorRepository, OutboxMonitorRepository>();
        services.AddScoped<IOutboxBacklogSummaryReader, OutboxBacklogSummaryReader>();
        services.AddScoped<OutboxBacklogSummaryService>();
        services.AddScoped<IReferenceProjectionInventoryReader, ReferenceProjectionInventoryReader>();
        services.AddScoped<
            IEmployeeFunctionalRoleAssignmentReader,
            EmployeeFunctionalRoleAssignmentReader>();
        services.AddScoped<IEmployeeActorMappingReader, EmployeeActorMappingReader>();
        services.AddScoped<IEmployeeActorDisplayNamesReader, EmployeeActorDisplayNamesReader>();
        services.AddScoped<IAuthorizationDirectoryStore, AuthorizationDirectoryStore>();
        services.AddScoped<BudgetOwnerRoleSynchronizer>();
        services.AddScoped<ICfoDirectoryStore, CfoDirectoryStore>();
        services.AddScoped<CfoDirectoryService>();
        services.AddScoped<ICostCenterEditorStore, CostCenterEditorStore>();
        services.AddScoped<CostCenterEditorService>();
        services.AddScoped<ICfoWorkflowAuthorityReader, CfoWorkflowAuthorityReader>();
        services.AddScoped<CfoWorkflowAuthority>();
        services.AddScoped<IActiveDirectoryEmployeeDirectory, ActiveDirectoryEmployeeDirectory>();
        services.AddScoped<IBudgetCostCenterMembershipReader, BudgetCostCenterMembershipReader>();
        services.AddScoped<BudgetDocumentAuthority>();
        services.AddScoped<IExpenseRequestSubstitutionReader, ExpenseRequestSubstitutionReader>();
        services.AddScoped<ExpenseRequestAccessMoment>();
        services.AddScoped<INsiReferenceProjectionReader, SqlServerNsiReferenceProjectionReader>();
        services.AddScoped<INsiIdentityReferenceProjectionReader>(provider =>
            new SqlServerNsiIdentityReferenceProjectionReader(
                provider.GetRequiredService<BmsDbContext>()));
        services.AddScoped<IBusinessAuditMonitorRepository, BusinessAuditMonitorRepository>();
        services.AddScoped<IChangeJournalReader, ChangeJournalReader>();
        services.AddScoped<IUsageSessionStore, UsageSessionStore>();
        services.AddScoped<ChangeJournalService>();
        services.AddScoped<IExchangeJournalReader, ExchangeJournalReader>();
        services.AddScoped<ExchangeJournalService>();
        services.AddScoped<IBusinessAuditDetailRepository, BusinessAuditDetailRepository>();
        services.AddScoped<IBusinessAuditSearchRepository, BusinessAuditSearchRepository>();
        services.AddScoped<IDatabaseReadinessMonitorReader, DatabaseReadinessMonitorReader>();
        services.AddScoped<IBusinessTransactionRunner, SqlServerBusinessTransactionRunner>();
        services.AddScoped<INsiIdentityReferenceImportExecutor>(provider =>
            new NsiIdentityReferenceImportExecutor(
                provider.GetRequiredService<INsiIdentityReferenceProjectionReader>(),
                provider.GetRequiredService<IBusinessTransactionRunner>(),
                () => new SqlServerNsiIdentityReferenceProjectionWriteSink(
                    provider.GetRequiredService<BmsDbContext>())));
        services.AddSingleton<IDatabaseReadinessProbe, SqlServerDatabaseReadinessProbe>();
        services.AddScoped<TechnicalPersistence>();
        services.AddScoped<IBusinessAuditSink>(provider => provider.GetRequiredService<TechnicalPersistence>());
        services.AddScoped<IOutboxSink>(provider => provider.GetRequiredService<TechnicalPersistence>());
        services.AddScoped<IIdempotencyStore>(provider => provider.GetRequiredService<TechnicalPersistence>());
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<TechnicalPersistence>());
        services.AddScoped<IExpenseRequestWorkflowDefinitionReader, ExpenseRequestWorkflowDefinitionReader>();
        services.AddScoped<IBudgetReservationReader, EfCoreBudgetReservationReader>();
        services.AddScoped<IApprovalRouteParticipantReader, ApprovalRouteParticipantReader>();
        services.AddScoped<IApprovalRouteSnapshotStore, ApprovalRouteSnapshotStore>();
        services.AddScoped<IAccrualClosingCycleSnapshotStore, AccrualClosingCycleSnapshotStore>();
        services.AddScoped<ExpenseRequestService>();
        services.AddScoped<ExpenseRequestAttachmentService>();
        services.AddScoped<IExpenseRequestPaymentDetailsReader, ExpenseRequestPaymentDetailsReader>();
        services.AddScoped<ExpenseRequestPaymentDetailsService>();
        services.AddScoped<ExpenseRequestWorkflowService>();
        services.AddScoped<ExpenseInitiatorRouteResolver>();
        services.AddScoped<TreasuryApprovalRestart>();
        services.AddScoped<IExpenseApprovalRoutePresentation, ExpenseApprovalRoutePresentation>();
        services.AddScoped<IExpensePaymentRegisterReader, ExpensePaymentRegisterReader>();
        services.AddScoped<IExpenseRequestClosingService, ExpenseRequestClosingService>();
        services.AddScoped<IManualDeficitCorrectionStore, ManualDeficitCorrectionStore>();
        services.AddScoped<ManualDeficitCorrectionAuthority>();
        services.AddScoped<IManualDeficitCorrectionService, ManualDeficitCorrectionService>();
        services.AddScoped<IManualDeficitReader, ManualDeficitReader>();
        services.AddScoped<ManualDeficitReadService>();
        services.AddScoped<ExpenseRequestDraftCurrencyService>();
        services.AddScoped<CurrencySubmissionReadiness>();
        services.AddScoped<IExpenseCashFlowAssignmentValidator, ExpenseCashFlowAssignmentValidator>();
        services.AddScoped<ExpenseRequestExportService>();
        services.AddScoped<IDebtorsReader470, DebtorsReader470>();
        services.AddScoped<DebtorsService470>();
        services.AddScoped<ExpenseRequestSubmissionPreflightService>();
        services.AddScoped<ExpenseRequestHistoryService>();
        services.AddScoped<BudgetPositionService>();
        services.AddScoped<IRequestBudgetExplanationReader442,RequestBudgetExplanationReader442>();
        services.AddScoped<RequestBudgetExplanationService442>();
        services.AddScoped<IBudgetExecutionRegisterReader343,BudgetExecutionRegisterReader343>();
        services.AddScoped<BudgetExecutionRegisterService343>();
        services.AddScoped<ExpenseDraftBudgetPreviewService>();
        services.AddScoped<BudgetExecutionExportService>();
        services.AddScoped<BudgetReservationService>();
        services.AddScoped<IBudgetInstanceStore, BudgetInstanceStore>();
        services.AddScoped<BudgetInstanceService>();
        services.AddScoped<Bms.Application.Tasks.IMyApprovalTaskReader355, MyApprovalTaskReader355>();
        services.AddScoped<Bms.Application.Tasks.MyApprovalTasks355>();
        services.AddScoped<BudgetAdjustmentService>();
        services.AddScoped<IBudgetInstanceHistoryRepository, BudgetInstanceHistoryRepository>();
        services.AddScoped<BudgetInstanceHistoryService>();
        services.AddScoped<IBudgetApprovalStore, BudgetApprovalStore>();
        services.AddScoped<BudgetApprovalWorkflowService>();
        services.AddScoped<IBddsReportStore, BddsReportStore>();
        services.AddScoped<BddsReportService>();
        services.AddScoped<IBddsDrillStore, BddsDrillStore>();
        services.AddScoped<BddsDrillService>();
        services.AddScoped<IUnbudgetedArticlesStore, UnbudgetedArticlesStore>();
        services.AddScoped<UnbudgetedArticlesService>();
        services.AddScoped<ICashFlowActualsStore, CashFlowActualsStore>();
        services.AddScoped<BudgetActualsService>();
        services.AddScoped<IBudgetDirectoryStore, BudgetDirectoryStore>();
        services.AddScoped<BudgetDirectoryService>();
        services.AddScoped<PaymentCalendarService>();
        services.AddScoped<PaymentCalendarExportService>();
        services.AddScoped<DatedPaymentCalendarService>();
        services.AddScoped<DatedPaymentCalendarExportService>();
        services.AddScoped<ActiveAccrualReportService>();
        services.AddScoped<OpiuReportService>();
        services.AddScoped<ActiveAccrualExportService>();
        services.AddScoped<ReferenceDataService>();
        services.AddScoped<ReferenceDataMaintenanceService>();
        services.AddScoped<ExpenseItemDirectoryService>();
        services.AddScoped<ICashFlowAccrualSettingsStore, CashFlowAccrualSettingsStore>();
        services.AddScoped<CashFlowAccrualSettingsAuthority>();
        services.AddScoped<CashFlowAccrualSettingsService>();
        services.AddScoped<ICashFlowCatalogOrderStore, CashFlowCatalogOrderStore>();
        services.AddScoped<CashFlowCatalogOrderService>();
        services.AddScoped<ApprovalMatrixService>();
        services.AddScoped<BudgetOwnerCostCenterAssignmentDirectoryService>();
        services.AddScoped<BudgetOwnerCostCenterAssignmentMaintenanceService>();
        services.AddScoped<OutboxMonitorService>();
        services.AddScoped<DatabaseReadinessMonitorService>();
        services.AddScoped<ReferenceProjectionInventoryService>();
        services.AddScoped<EmployeeFunctionalRoleResolver>();
        services.AddScoped<EmployeeActorResolver>();
        services.AddScoped<ReferenceDataMaintenanceAuthority>();
        services.AddScoped<IBusinessAuditReadAuthority, BusinessAuditReadAuthority>();
        services.AddScoped<ApprovalRouteParticipantResolver>();
        services.AddScoped<BudgetOwnerCreationAuthority>();
        services.AddScoped<AuthorizationDirectoryService>();
        services.AddScoped<AuthorizationDirectoryRoleService>();
        services.AddScoped<ExpenseRequestSubstitutionAuthority>();
        services.AddScoped<BusinessAuditMonitorService>();
        services.AddScoped<BusinessAuditDetailService>();
        services.AddScoped<BusinessAuditSearchService>();
        return services;
    }
}
