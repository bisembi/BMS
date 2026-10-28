-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/* Additive company budget compositions. No legacy Limit/Reserve/Paid or fact writes.
   Apply in a protected maintenance window. Schema47 binaries are NOT rollback-compatible.
   Root must deploy a schema48-compatible rollback package before admitting commands. */
SET XACT_ABORT ON;
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF @@TRANCOUNT<>0 THROW 64800,N'Run SQL048 outside an ambient transaction.',1;
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 64801,N'SQL048 requires the explicitly owned BMS database.',1;
IF CONVERT(nvarchar(20),DATABASEPROPERTYEX(DB_NAME(),N'Updateability'))<>N'READ_WRITE'
    THROW 64802,N'SQL048 requires a writable database.',1;
IF OBJECT_ID(N'bms_web.schema_version',N'U') IS NULL THROW 64803,N'Missing schema registry.',1;
IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=47
    AND script_name COLLATE Latin1_General_100_BIN2=N'047_budget_instance_line_list_covering.sql')
    OR EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no NOT BETWEEN 1 AND 43 AND version_no NOT IN(45,46,47,48))
    OR (SELECT COUNT(*) FROM bms_web.schema_version WHERE version_no<>48)<>46
    THROW 64804,N'SQL048 requires exact schema47 predecessors.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'bms-web:company-budget-plan',@LockMode=N'Exclusive',
    @LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 64805,N'Company budget migration lock unavailable.',1;
 DECLARE @CV048Predecessors TABLE(version_no int PRIMARY KEY,script_name nvarchar(200));
 INSERT @CV048Predecessors VALUES
 (1,N'001_bootstrap_bms_web.sql'),
 (2,N'002_budget_projection.sql'),
 (3,N'003_access_scope.sql'),
 (4,N'004_workflow_matrix.sql'),
 (5,N'005_expense_request_evidence.sql'),
 (6,N'006_expense_request_copy_source.sql'),
 (7,N'007_expense_request_line_order.sql'),
 (8,N'008_actor_idempotency_collation.sql'),
 (9,N'009_cash_flow_items_accrual_source.sql'),
 (10,N'010_expense_items_accrual_mapping.sql'),
 (11,N'011_expense_request_line_accrual_snapshot.sql'),
 (12,N'012_money_decimal18_scale2.sql'),
 (13,N'013_counterparties_reference.sql'),
 (14,N'014_contracts_reference.sql'),
 (15,N'015_employee_functional_roles.sql'),
 (16,N'016_currencies_and_daily_rates.sql'),
 (17,N'017_expense_request_currency_rate_snapshot.sql'),
 (18,N'018_expense_request_line_currency_conversion.sql'),
 (19,N'019_overpayment_deficit_storage.sql'),
 (20,N'020_treasury_payment_overpayment_storage.sql'),
 (21,N'021_budget_owner_cost_center_assignments.sql'),
 (22,N'022_expense_request_approval_route_snapshots.sql'),
 (23,N'023_employee_actor_mappings.sql'),
 (24,N'024_expense_request_workflow_activation.sql'),
 (25,N'025_expense_request_accrual_closing_cycles.sql'),
 (26,N'026_expense_request_closing_activation.sql'),
 (27,N'027_accrual_red_zone_notifications.sql'),
 (28,N'028_cash_flow_accrual_settings_version.sql'),
 (29,N'029_expense_request_substitutions.sql'),
 (30,N'030_expense_request_task_scope_index.sql'),
 (31,N'031_expense_request_attachments.sql'),
 (32,N'032_budget_instance_draft.sql'),
 (33,N'033_budget_instance_monthly_period.sql'),
 (34,N'034_budget_cost_center_directory.sql'),
 (35,N'035_budget_instance_approval.sql'),
 (36,N'036_identity_reference_import_watermark.sql'),
 (37,N'037_budget_instance_line_comment.sql'),
 (38,N'038_expense_request_numbering.sql'),
 (39,N'039_counterparty_bin.sql'),
 (40,N'040_cash_flow_activity_type.sql'),
 (41,N'041_bdds_snapshot_and_flow_direction.sql'),
 (42,N'042_cash_flow_actuals_2026.sql'),
 (43,N'043_cash_flow_item_cost_center.sql'),
 (45,N'045_cfo_initiators_and_budget_approval.sql'),
 (46,N'046_budget_instance_operation_rows.sql'),
 (47,N'047_budget_instance_line_list_covering.sql');
 DECLARE @CV048RegistryCount int;
 SELECT @CV048RegistryCount=COUNT(*) FROM bms_web.schema_version WITH(UPDLOCK,HOLDLOCK);
 IF @CV048RegistryCount<>46+CONVERT(int,CASE WHEN EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=48) THEN 1 ELSE 0 END)
 OR EXISTS(SELECT 1 FROM @CV048Predecessors p WHERE NOT EXISTS(SELECT 1 FROM bms_web.schema_version v
 WHERE v.version_no=p.version_no AND v.script_name COLLATE Latin1_General_100_BIN2=p.script_name
 AND DATALENGTH(v.script_name)=DATALENGTH(p.script_name)))
 THROW 64824,N'Company budget predecessor registry differs from the pinned manifest.',1;
 DECLARE @CV048Initial bit=CASE WHEN EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=48) THEN 0 ELSE 1 END;
 DECLARE @CV048LegacyWinners TABLE(CostCenterId uniqueidentifier,BudgetMonth date,InstanceId uniqueidentifier NULL,
 PRIMARY KEY(CostCenterId,BudgetMonth));
 IF @CV048Initial=1
 INSERT @CV048LegacyWinners
 SELECT slots.CostCenterId,slots.BudgetMonth,winner.Id
 FROM (SELECT CostCenterId,BudgetMonth FROM bms_web.BudgetPositions
 UNION SELECT b.CostCenterId,l.BudgetMonth FROM bms_web.BudgetInstances b JOIN bms_web.BudgetInstanceLines l ON l.BudgetInstanceId=b.Id
 WHERE b.Status COLLATE Latin1_General_100_BIN2=N'Approved') slots
 OUTER APPLY(SELECT TOP(1) b.Id FROM bms_web.BudgetInstances b
 WHERE b.CostCenterId=slots.CostCenterId AND b.ValidFrom<=slots.BudgetMonth AND b.ValidTo>=slots.BudgetMonth
 AND b.Status COLLATE Latin1_General_100_BIN2=N'Approved'
 ORDER BY b.ApprovalOrdinal DESC,b.ApprovedAtUtc DESC,b.Id DESC) winner;
 
 IF EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=48)
 BEGIN
    IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=48
        AND script_name COLLATE Latin1_General_100_BIN2=N'048_company_budget_versions.sql')
        THROW 64806,N'SQL048 registry drift.',1;
 END
 ELSE
 BEGIN
    IF OBJECT_ID(N'bms_web.CompanyBudgetVersions') IS NOT NULL
        OR OBJECT_ID(N'bms_web.CompanyBudgetVersionInstances') IS NOT NULL
        OR OBJECT_ID(N'bms_web.CompanyBudgetActiveVersion') IS NOT NULL
        OR OBJECT_ID(N'bms_web.CompanyBudgetActivations') IS NOT NULL
        OR OBJECT_ID(N'bms_web.CompanyBudgetApprovalBaselines') IS NOT NULL
        OR OBJECT_ID(N'bms_web.CompanyExpenseApprovalBaselines') IS NOT NULL
        OR OBJECT_ID(N'bms_web.CompanyBudgetInstanceCreations') IS NOT NULL
        THROW 64807,N'Unregistered company budget storage exists.',1;
    CREATE TABLE bms_web.CompanyBudgetInstanceCreations
    (
        BudgetInstanceId uniqueidentifier NOT NULL CONSTRAINT PK_CompanyBudgetInstanceCreations PRIMARY KEY,
        CreatedAtUtc datetimeoffset(3) NULL,
        Source nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CONSTRAINT FK_CompanyBudgetInstanceCreations_Instance FOREIGN KEY(BudgetInstanceId) REFERENCES bms_web.BudgetInstances(Id),
        CONSTRAINT CK_CompanyBudgetInstanceCreations_provenance CHECK ((Source COLLATE Latin1_General_100_BIN2 IN(N'Application',N'Audit',N'Imported') AND CreatedAtUtc IS NOT NULL AND DATEPART(TZOFFSET,CreatedAtUtc)=0)
            OR (Source COLLATE Latin1_General_100_BIN2=N'LegacyUnknown' AND CreatedAtUtc IS NULL))
    );
    -- Exact creation events only. Number counters and approval timestamps are
    -- not creation clocks. Missing legacy evidence remains NULL/LegacyUnknown.
    DECLARE @CV048CreationEvidence TABLE(BudgetInstanceId uniqueidentifier,CreatedAtUtc datetimeoffset(3),Source nvarchar(20));
    IF EXISTS(SELECT 1 FROM bms_web.BusinessAudit a JOIN bms_web.BudgetInstances b
      ON TRY_CONVERT(uniqueidentifier,a.ObjectId)=b.Id
      WHERE a.ObjectType COLLATE Latin1_General_100_BIN2=N'BudgetInstance'
      AND a.Action COLLATE Latin1_General_100_BIN2=N'BudgetInstanceDraftCreated'
      AND (ISJSON(a.AfterJson)<>1 OR DATEPART(TZOFFSET,a.OccurredAtUtc)<>0
        OR TRY_CONVERT(uniqueidentifier,CASE WHEN ISJSON(a.AfterJson)=1 THEN JSON_VALUE(a.AfterJson,'$.id') END) IS NULL
        OR TRY_CONVERT(uniqueidentifier,CASE WHEN ISJSON(a.AfterJson)=1 THEN JSON_VALUE(a.AfterJson,'$.id') END)<>b.Id))
      THROW 64827,N'Invalid legacy creation audit evidence.',1;
    INSERT @CV048CreationEvidence
    SELECT b.Id,a.OccurredAtUtc,N'Audit' FROM bms_web.BudgetInstances b JOIN bms_web.BusinessAudit a
      ON TRY_CONVERT(uniqueidentifier,a.ObjectId)=b.Id
      WHERE a.ObjectType COLLATE Latin1_General_100_BIN2=N'BudgetInstance'
      AND a.Action COLLATE Latin1_General_100_BIN2=N'BudgetInstanceDraftCreated'
      AND TRY_CONVERT(uniqueidentifier,CASE WHEN ISJSON(a.AfterJson)=1 THEN JSON_VALUE(a.AfterJson,'$.id') END)=b.Id;
    INSERT @CV048CreationEvidence
    SELECT b.Id,s.CreatedAtUtc,N'Imported' FROM bms_web.BudgetInstances b
      JOIN bms_web.BddsSnapshotBudgetInstances l ON l.BudgetInstanceId=b.Id
      JOIN bms_web.BddsSnapshots s ON s.Id=l.SnapshotId;
    IF EXISTS(SELECT 1 FROM @CV048CreationEvidence GROUP BY BudgetInstanceId HAVING MIN(CreatedAtUtc)<>MAX(CreatedAtUtc))
      THROW 64828,N'Conflicting legacy creation timestamps; migration refused without guessing.',1;
    INSERT bms_web.CompanyBudgetInstanceCreations(BudgetInstanceId,CreatedAtUtc,Source)
    SELECT b.Id,e.CreatedAtUtc,COALESCE(e.Source,CONVERT(nvarchar(20),N'LegacyUnknown')) FROM bms_web.BudgetInstances b
      LEFT JOIN (SELECT BudgetInstanceId,MIN(CreatedAtUtc) CreatedAtUtc,
        CASE WHEN MAX(CASE WHEN Source=N'Audit' THEN 1 ELSE 0 END)=1 THEN N'Audit' ELSE N'Imported' END Source
        FROM @CV048CreationEvidence GROUP BY BudgetInstanceId) e ON e.BudgetInstanceId=b.Id;
    CREATE TABLE bms_web.CompanyBudgetVersions
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CompanyBudgetVersions PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        ConcurrencyStamp uniqueidentifier NOT NULL,
        CreatedAtUtc datetimeoffset(3) NOT NULL,
        CreatedByEmployeeId uniqueidentifier NULL,
        CreatedByExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublishedAtUtc datetimeoffset(3) NULL,
        PublishedInstanceCount int NULL,
        CONSTRAINT CK_CompanyBudgetVersions_identity CHECK(Id<>'00000000-0000-0000-0000-000000000000'
            AND ConcurrencyStamp<>'00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_CompanyBudgetVersions_name CHECK(DATALENGTH(Name) BETWEEN 2 AND 400
            AND DATALENGTH(Name)=DATALENGTH(LTRIM(RTRIM(Name)))),
        CONSTRAINT CK_CompanyBudgetVersions_utc CHECK(DATEPART(TZOFFSET,CreatedAtUtc)=0
            AND ((PublishedAtUtc IS NULL AND PublishedInstanceCount IS NULL)
                OR (PublishedAtUtc IS NOT NULL AND DATEPART(TZOFFSET,PublishedAtUtc)=0
                    AND PublishedInstanceCount IS NOT NULL AND PublishedInstanceCount>=0))),
        CONSTRAINT FK_CompanyBudgetVersions_Employee FOREIGN KEY(CreatedByEmployeeId) REFERENCES bms_web.Employees(Id)
    );
    CREATE TABLE bms_web.CompanyBudgetVersionInstances
    (
        VersionId uniqueidentifier NOT NULL,
        BudgetInstanceId uniqueidentifier NOT NULL,
        CONSTRAINT PK_CompanyBudgetVersionInstances PRIMARY KEY(VersionId,BudgetInstanceId),
        CONSTRAINT FK_CompanyBudgetVersionInstances_Version FOREIGN KEY(VersionId) REFERENCES bms_web.CompanyBudgetVersions(Id),
        CONSTRAINT FK_CompanyBudgetVersionInstances_Instance FOREIGN KEY(BudgetInstanceId) REFERENCES bms_web.BudgetInstances(Id)
    );
    CREATE INDEX IX_CompanyBudgetVersionInstances_Instance ON bms_web.CompanyBudgetVersionInstances(BudgetInstanceId);
    CREATE TABLE bms_web.CompanyBudgetActiveVersion
    (
        Id tinyint NOT NULL CONSTRAINT PK_CompanyBudgetActiveVersion PRIMARY KEY,
        VersionId uniqueidentifier NOT NULL,
        ConcurrencyStamp uniqueidentifier NOT NULL,
        ActivatedAtUtc datetimeoffset(3) NOT NULL,
        ActivatedByEmployeeId uniqueidentifier NULL,
        ActivatedByExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CONSTRAINT CK_CompanyBudgetActiveVersion_single CHECK(Id=1 AND ConcurrencyStamp<>'00000000-0000-0000-0000-000000000000'
            AND DATEPART(TZOFFSET,ActivatedAtUtc)=0),
        CONSTRAINT FK_CompanyBudgetActiveVersion_Version FOREIGN KEY(VersionId) REFERENCES bms_web.CompanyBudgetVersions(Id),
        CONSTRAINT FK_CompanyBudgetActiveVersion_Employee FOREIGN KEY(ActivatedByEmployeeId) REFERENCES bms_web.Employees(Id)
    );
    CREATE TABLE bms_web.CompanyBudgetActivations
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CompanyBudgetActivations PRIMARY KEY,
        VersionId uniqueidentifier NOT NULL,
        PreviousVersionId uniqueidentifier NULL,
        ActiveConcurrencyStamp uniqueidentifier NOT NULL,
        ActivatedAtUtc datetimeoffset(3) NOT NULL,
        EmployeeId uniqueidentifier NULL,
        ActorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CONSTRAINT FK_CompanyBudgetActivations_Version FOREIGN KEY(VersionId) REFERENCES bms_web.CompanyBudgetVersions(Id),
        CONSTRAINT FK_CompanyBudgetActivations_Previous FOREIGN KEY(PreviousVersionId) REFERENCES bms_web.CompanyBudgetVersions(Id),
        CONSTRAINT FK_CompanyBudgetActivations_Employee FOREIGN KEY(EmployeeId) REFERENCES bms_web.Employees(Id),
        CONSTRAINT CK_CompanyBudgetActivations_identity CHECK(Id<>'00000000-0000-0000-0000-000000000000'
            AND ActiveConcurrencyStamp<>'00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET,ActivatedAtUtc)=0)
    );
    CREATE UNIQUE INDEX UX_CompanyBudgetActivations_Stamp ON bms_web.CompanyBudgetActivations(ActiveConcurrencyStamp);
    CREATE TABLE bms_web.CompanyBudgetApprovalBaselines
    (
        ReviewId uniqueidentifier NOT NULL CONSTRAINT PK_CompanyBudgetApprovalBaselines PRIMARY KEY,
        VersionId uniqueidentifier NOT NULL,
        ActiveConcurrencyStamp uniqueidentifier NOT NULL,
        CONSTRAINT FK_CompanyBudgetApprovalBaselines_Review FOREIGN KEY(ReviewId) REFERENCES bms_web.BudgetApprovalSubmissions(Id),
        CONSTRAINT FK_CompanyBudgetApprovalBaselines_Version FOREIGN KEY(VersionId) REFERENCES bms_web.CompanyBudgetVersions(Id),
        CONSTRAINT CK_CompanyBudgetApprovalBaselines_stamp CHECK(ActiveConcurrencyStamp<>'00000000-0000-0000-0000-000000000000')
    );
    CREATE TABLE bms_web.CompanyExpenseApprovalBaselines
    (
        ExpenseRequestId uniqueidentifier NOT NULL,
        SnapshotNumber int NOT NULL,
        VersionId uniqueidentifier NOT NULL,
        ActiveConcurrencyStamp uniqueidentifier NOT NULL,
        CONSTRAINT PK_CompanyExpenseApprovalBaselines PRIMARY KEY(ExpenseRequestId,SnapshotNumber),
        CONSTRAINT FK_CompanyExpenseApprovalBaselines_Route FOREIGN KEY(ExpenseRequestId,SnapshotNumber)
            REFERENCES bms_web.ExpenseRequestApprovalRouteSnapshots(ExpenseRequestId,SnapshotNumber),
        CONSTRAINT FK_CompanyExpenseApprovalBaselines_Version FOREIGN KEY(VersionId) REFERENCES bms_web.CompanyBudgetVersions(Id),
        CONSTRAINT CK_CompanyExpenseApprovalBaselines_stamp CHECK(SnapshotNumber>0
            AND ActiveConcurrencyStamp<>'00000000-0000-0000-0000-000000000000')
    );
    /* Preserve every old whole CFO/month Approved winner: membership contains
       ALL Approved headers and the resolver retains the exact original order.
       No BudgetInstance, line, position, reservation, paid amount or fact write. */
    DECLARE @seed uniqueidentifier=NEWID(), @stamp uniqueidentifier=NEWID(), @utc datetimeoffset(3)=SYSUTCDATETIME();
    INSERT bms_web.CompanyBudgetVersions VALUES(@seed,N'Текущий бюджет',NEWID(),@utc,NULL,N'SQL048',NULL,NULL);
    INSERT bms_web.CompanyBudgetVersionInstances SELECT @seed,Id FROM bms_web.BudgetInstances WHERE Status COLLATE Latin1_General_100_BIN2=N'Approved';
    UPDATE bms_web.CompanyBudgetVersions SET PublishedAtUtc=@utc,
        PublishedInstanceCount=(SELECT COUNT(*) FROM bms_web.CompanyBudgetVersionInstances WHERE VersionId=@seed) WHERE Id=@seed;
    INSERT bms_web.CompanyBudgetActiveVersion VALUES(1,@seed,@stamp,@utc,NULL,N'SQL048');
    INSERT bms_web.CompanyBudgetActivations VALUES(NEWID(),@seed,NULL,@stamp,@utc,NULL,N'SQL048');
    INSERT bms_web.schema_version(version_no,script_name) VALUES(48,N'048_company_budget_versions.sql');
 END;
 IF OBJECT_ID(N'bms_web.TR_CompanyCreation_Immutable',N'TR') IS NULL
 EXEC(N'CREATE TRIGGER bms_web.TR_CompanyCreation_Immutable ON bms_web.CompanyBudgetInstanceCreations AFTER UPDATE,DELETE AS
 BEGIN SET NOCOUNT ON;
  IF EXISTS(SELECT 1 FROM inserted) THROW 64825,N''Document creation provenance cannot be changed.'',1;
  IF EXISTS(SELECT 1 FROM deleted d JOIN bms_web.BudgetInstances b ON b.Id=d.BudgetInstanceId
    WHERE b.Status COLLATE Latin1_General_100_BIN2<>N''Draft''
      OR EXISTS(SELECT 1 FROM bms_web.CompanyBudgetVersionInstances m WHERE m.BudgetInstanceId=b.Id))
    THROW 64826,N''Referenced or approved creation provenance cannot be deleted.'',1;
 END');
 /* New tables are immutable after publication. Existing approval evidence remains byte-identical. */
 IF OBJECT_ID(N'bms_web.TR_CompanyBudgetVersions_Immutable',N'TR') IS NULL
 EXEC(N'CREATE TRIGGER bms_web.TR_CompanyBudgetVersions_Immutable ON bms_web.CompanyBudgetVersions AFTER UPDATE,DELETE AS
 BEGIN SET NOCOUNT ON;
  IF EXISTS(SELECT 1 FROM deleted d WHERE d.PublishedAtUtc IS NOT NULL)
    THROW 64811,N''Published company versions cannot be changed or deleted.'',1;
  IF EXISTS(SELECT 1 FROM inserted i WHERE i.PublishedAtUtc IS NOT NULL AND EXISTS
      (SELECT 1 FROM bms_web.CompanyBudgetVersionInstances m JOIN bms_web.BudgetInstances b ON b.Id=m.BudgetInstanceId
       WHERE m.VersionId=i.Id AND (b.Status COLLATE Latin1_General_100_BIN2<>N''Approved''
            OR b.ApprovalOrdinal IS NULL OR b.ApprovedAtUtc IS NULL)))
    THROW 64812,N''Only Approved documents can be published.'',1;
  IF EXISTS(SELECT 1 FROM inserted i WHERE i.PublishedAtUtc IS NOT NULL
      AND i.PublishedInstanceCount<>(SELECT COUNT(*) FROM bms_web.CompanyBudgetVersionInstances m WHERE m.VersionId=i.Id))
    THROW 64821,N''Published member count is inconsistent.'',1;
 END');
 IF OBJECT_ID(N'bms_web.TR_CompanyBudgetMembers_Immutable',N'TR') IS NULL
 EXEC(N'CREATE TRIGGER bms_web.TR_CompanyBudgetMembers_Immutable ON bms_web.CompanyBudgetVersionInstances AFTER INSERT,UPDATE,DELETE AS
 BEGIN SET NOCOUNT ON;
  IF EXISTS(SELECT 1 FROM (SELECT VersionId FROM inserted UNION SELECT VersionId FROM deleted) x
      JOIN bms_web.CompanyBudgetVersions v ON v.Id=x.VersionId WHERE v.PublishedAtUtc IS NOT NULL)
    THROW 64813,N''Published company version membership is immutable.'',1;
 END');
 IF OBJECT_ID(N'bms_web.TR_CompanyBudgetActive_Valid',N'TR') IS NULL
 EXEC(N'CREATE TRIGGER bms_web.TR_CompanyBudgetActive_Valid ON bms_web.CompanyBudgetActiveVersion AFTER INSERT,UPDATE,DELETE AS
 BEGIN SET NOCOUNT ON;
  IF (SELECT COUNT(*) FROM bms_web.CompanyBudgetActiveVersion)<>1
    OR EXISTS(SELECT 1 FROM inserted a JOIN bms_web.CompanyBudgetVersions v ON v.Id=a.VersionId WHERE v.PublishedAtUtc IS NULL)
    OR EXISTS(SELECT 1 FROM inserted a JOIN bms_web.CompanyBudgetVersionInstances m ON m.VersionId=a.VersionId
        JOIN bms_web.BudgetInstances b ON b.Id=m.BudgetInstanceId WHERE b.Status COLLATE Latin1_General_100_BIN2<>N''Approved''
            OR b.ApprovalOrdinal IS NULL OR b.ApprovedAtUtc IS NULL)
    THROW 64814,N''Exactly one published Approved company version must remain active.'',1;
 END');
 IF OBJECT_ID(N'bms_web.TR_CompanyBudgetActivation_Immutable',N'TR') IS NULL
 EXEC(N'CREATE TRIGGER bms_web.TR_CompanyBudgetActivation_Immutable ON bms_web.CompanyBudgetActivations AFTER UPDATE,DELETE AS
 BEGIN THROW 64815,N''Company budget activation history is immutable.'',1; END');
 IF OBJECT_ID(N'bms_web.TR_CompanyBudgetBaseline_Immutable',N'TR') IS NULL
 EXEC(N'CREATE TRIGGER bms_web.TR_CompanyBudgetBaseline_Immutable ON bms_web.CompanyBudgetApprovalBaselines AFTER UPDATE,DELETE AS
 BEGIN THROW 64816,N''Company budget approval provenance is immutable.'',1; END');
 IF OBJECT_ID(N'bms_web.TR_CompanyExpenseBaseline_Immutable',N'TR') IS NULL
 EXEC(N'CREATE TRIGGER bms_web.TR_CompanyExpenseBaseline_Immutable ON bms_web.CompanyExpenseApprovalBaselines AFTER UPDATE,DELETE AS
 BEGIN THROW 64817,N''Expense approval provenance is immutable.'',1; END');
 IF OBJECT_ID(N'bms_web.TR_CompanyPublishedBudgetHeader_Immutable',N'TR') IS NULL
 EXEC(N'CREATE TRIGGER bms_web.TR_CompanyPublishedBudgetHeader_Immutable ON bms_web.BudgetInstances AFTER UPDATE,DELETE AS
 BEGIN SET NOCOUNT ON;
  IF EXISTS(SELECT 1 FROM deleted d JOIN bms_web.CompanyBudgetVersionInstances m ON m.BudgetInstanceId=d.Id
      JOIN bms_web.CompanyBudgetVersions v ON v.Id=m.VersionId WHERE v.PublishedAtUtc IS NOT NULL)
    THROW 64818,N''A document in a published company version is immutable.'',1;
 END');
 IF OBJECT_ID(N'bms_web.TR_CompanyPublishedBudgetLines_Immutable',N'TR') IS NULL
 EXEC(N'CREATE TRIGGER bms_web.TR_CompanyPublishedBudgetLines_Immutable ON bms_web.BudgetInstanceLines AFTER INSERT,UPDATE,DELETE AS
 BEGIN SET NOCOUNT ON;
  IF EXISTS(SELECT 1 FROM (SELECT BudgetInstanceId FROM inserted UNION SELECT BudgetInstanceId FROM deleted) x
      JOIN bms_web.CompanyBudgetVersionInstances m ON m.BudgetInstanceId=x.BudgetInstanceId
      JOIN bms_web.CompanyBudgetVersions v ON v.Id=m.VersionId WHERE v.PublishedAtUtc IS NOT NULL)
    THROW 64819,N''Lines in a published company version are immutable.'',1;
 END');
 IF OBJECT_ID(N'bms_web.fn_CompanyBudgetWinner',N'IF') IS NULL
 EXEC(N'CREATE FUNCTION bms_web.fn_CompanyBudgetWinner(@version uniqueidentifier,@center uniqueidentifier,@month date)
 RETURNS TABLE AS RETURN
 (SELECT TOP(1) b.Id FROM bms_web.CompanyBudgetVersionInstances m
  JOIN bms_web.BudgetInstances b ON b.Id=m.BudgetInstanceId
  WHERE m.VersionId=@version AND b.CostCenterId=@center AND b.ValidFrom<=@month AND b.ValidTo>=@month
    AND b.Status COLLATE Latin1_General_100_BIN2=N''Approved''
  ORDER BY b.ApprovalOrdinal DESC,b.ApprovedAtUtc DESC,b.Id DESC)');
 IF OBJECT_ID(N'bms_web.v_CompanyBudgetExecutionPositions',N'V') IS NULL
 EXEC(N'CREATE VIEW bms_web.v_CompanyBudgetExecutionPositions AS
 WITH keys AS
 (SELECT CostCenterId,CashFlowItemId,BudgetMonth FROM bms_web.BudgetPositions
  UNION SELECT b.CostCenterId,l.CashFlowItemId,l.BudgetMonth
    FROM bms_web.CompanyBudgetActiveVersion a JOIN bms_web.CompanyBudgetVersionInstances m ON m.VersionId=a.VersionId
    JOIN bms_web.BudgetInstances b ON b.Id=m.BudgetInstanceId JOIN bms_web.BudgetInstanceLines l ON l.BudgetInstanceId=b.Id
    WHERE l.FlowDirection COLLATE Latin1_General_100_BIN2=N''Payment'')
 SELECT k.CostCenterId,k.CashFlowItemId,k.BudgetMonth,
    CONVERT(decimal(18,2),ISNULL(limits.AmountKzt,0)) AS LimitAmountKzt,
    CONVERT(decimal(18,2),ISNULL(p.ReservedAmountKzt,0)) AS ReservedAmountKzt,
    CONVERT(decimal(18,2),ISNULL(p.PaidAmountKzt,0)) AS PaidAmountKzt
 FROM keys k CROSS JOIN bms_web.CompanyBudgetActiveVersion a
 LEFT JOIN bms_web.BudgetPositions p ON p.CostCenterId=k.CostCenterId AND p.CashFlowItemId=k.CashFlowItemId AND p.BudgetMonth=k.BudgetMonth
 OUTER APPLY bms_web.fn_CompanyBudgetWinner(a.VersionId,k.CostCenterId,k.BudgetMonth) winner
 OUTER APPLY(SELECT SUM(l.LimitAmountKzt) AmountKzt FROM bms_web.BudgetInstanceLines l
    WHERE l.BudgetInstanceId=winner.Id AND l.CashFlowItemId=k.CashFlowItemId AND l.BudgetMonth=k.BudgetMonth
    AND l.FlowDirection COLLATE Latin1_General_100_BIN2=N''Payment'') limits');
/* SQL048 COMPANY VERSION SHAPE BEGIN */
DECLARE @CV048Ready bit=1;
DECLARE @CV048Tables TABLE(Name sysname NOT NULL,ObjectId int NULL,Columns int NOT NULL,ForeignKeys int NOT NULL,Checks int NOT NULL);
INSERT @CV048Tables VALUES
 (N'CompanyBudgetInstanceCreations',OBJECT_ID(N'bms_web.CompanyBudgetInstanceCreations',N'U'),3,1,1),
 (N'CompanyBudgetVersions',OBJECT_ID(N'bms_web.CompanyBudgetVersions',N'U'),8,1,3),
 (N'CompanyBudgetVersionInstances',OBJECT_ID(N'bms_web.CompanyBudgetVersionInstances',N'U'),2,2,0),
 (N'CompanyBudgetActiveVersion',OBJECT_ID(N'bms_web.CompanyBudgetActiveVersion',N'U'),6,2,1),
 (N'CompanyBudgetActivations',OBJECT_ID(N'bms_web.CompanyBudgetActivations',N'U'),7,3,1),
 (N'CompanyBudgetApprovalBaselines',OBJECT_ID(N'bms_web.CompanyBudgetApprovalBaselines',N'U'),3,2,1),
 (N'CompanyExpenseApprovalBaselines',OBJECT_ID(N'bms_web.CompanyExpenseApprovalBaselines',N'U'),4,2,1);
DECLARE @CV048Columns TABLE(TableName sysname,Ordinal int,Name sysname,TypeName sysname,Length int,Nullable bit);
INSERT @CV048Columns VALUES
 (N'CompanyBudgetInstanceCreations',1,N'BudgetInstanceId',N'uniqueidentifier',16,0),
 (N'CompanyBudgetInstanceCreations',2,N'CreatedAtUtc',N'datetimeoffset',9,1),
 (N'CompanyBudgetInstanceCreations',3,N'Source',N'nvarchar',40,0),
 (N'CompanyBudgetVersions',1,N'Id',N'uniqueidentifier',16,0),
 (N'CompanyBudgetVersions',2,N'Name',N'nvarchar',400,0),
 (N'CompanyBudgetVersions',3,N'ConcurrencyStamp',N'uniqueidentifier',16,0),
 (N'CompanyBudgetVersions',4,N'CreatedAtUtc',N'datetimeoffset',9,0),
 (N'CompanyBudgetVersions',5,N'CreatedByEmployeeId',N'uniqueidentifier',16,1),
 (N'CompanyBudgetVersions',6,N'CreatedByExternalId',N'nvarchar',512,0),
 (N'CompanyBudgetVersions',7,N'PublishedAtUtc',N'datetimeoffset',9,1),
 (N'CompanyBudgetVersions',8,N'PublishedInstanceCount',N'int',4,1),
 (N'CompanyBudgetVersionInstances',1,N'VersionId',N'uniqueidentifier',16,0),
 (N'CompanyBudgetVersionInstances',2,N'BudgetInstanceId',N'uniqueidentifier',16,0),
 (N'CompanyBudgetActiveVersion',1,N'Id',N'tinyint',1,0),
 (N'CompanyBudgetActiveVersion',2,N'VersionId',N'uniqueidentifier',16,0),
 (N'CompanyBudgetActiveVersion',3,N'ConcurrencyStamp',N'uniqueidentifier',16,0),
 (N'CompanyBudgetActiveVersion',4,N'ActivatedAtUtc',N'datetimeoffset',9,0),
 (N'CompanyBudgetActiveVersion',5,N'ActivatedByEmployeeId',N'uniqueidentifier',16,1),
 (N'CompanyBudgetActiveVersion',6,N'ActivatedByExternalId',N'nvarchar',512,0),
 (N'CompanyBudgetActivations',1,N'Id',N'uniqueidentifier',16,0),
 (N'CompanyBudgetActivations',2,N'VersionId',N'uniqueidentifier',16,0),
 (N'CompanyBudgetActivations',3,N'PreviousVersionId',N'uniqueidentifier',16,1),
 (N'CompanyBudgetActivations',4,N'ActiveConcurrencyStamp',N'uniqueidentifier',16,0),
 (N'CompanyBudgetActivations',5,N'ActivatedAtUtc',N'datetimeoffset',9,0),
 (N'CompanyBudgetActivations',6,N'EmployeeId',N'uniqueidentifier',16,1),
 (N'CompanyBudgetActivations',7,N'ActorExternalId',N'nvarchar',512,0),
 (N'CompanyBudgetApprovalBaselines',1,N'ReviewId',N'uniqueidentifier',16,0),
 (N'CompanyBudgetApprovalBaselines',2,N'VersionId',N'uniqueidentifier',16,0),
 (N'CompanyBudgetApprovalBaselines',3,N'ActiveConcurrencyStamp',N'uniqueidentifier',16,0),
 (N'CompanyExpenseApprovalBaselines',1,N'ExpenseRequestId',N'uniqueidentifier',16,0),
 (N'CompanyExpenseApprovalBaselines',2,N'SnapshotNumber',N'int',4,0),
 (N'CompanyExpenseApprovalBaselines',3,N'VersionId',N'uniqueidentifier',16,0),
 (N'CompanyExpenseApprovalBaselines',4,N'ActiveConcurrencyStamp',N'uniqueidentifier',16,0);
IF EXISTS(SELECT 1 FROM @CV048Tables t LEFT JOIN sys.tables a ON a.object_id=t.ObjectId
 WHERE a.object_id IS NULL OR a.temporal_type<>0 OR a.is_memory_optimized<>0 OR a.is_filetable<>0
 OR (SELECT COUNT(*) FROM sys.columns WHERE object_id=t.ObjectId)<>t.Columns
 OR (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=t.ObjectId)<>t.ForeignKeys
 OR (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=t.ObjectId)<>t.Checks
 OR EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=t.ObjectId AND (is_disabled=1 OR is_not_trusted=1 OR is_not_for_replication=1 OR delete_referential_action<>0 OR update_referential_action<>0))
 OR EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=t.ObjectId AND (is_disabled=1 OR is_not_trusted=1 OR is_not_for_replication=1)))
 SET @CV048Ready=0;
IF EXISTS(SELECT 1 FROM @CV048Columns e JOIN @CV048Tables t ON t.Name=e.TableName
 LEFT JOIN sys.columns c ON c.object_id=t.ObjectId AND c.column_id=e.Ordinal
 WHERE c.column_id IS NULL OR c.name COLLATE Latin1_General_100_BIN2<>e.Name
 OR TYPE_NAME(c.user_type_id) COLLATE Latin1_General_100_BIN2<>e.TypeName
 OR c.max_length<>e.Length OR c.is_nullable<>e.Nullable OR c.is_identity=1 OR c.is_computed=1
 OR c.default_object_id<>0 OR (e.TypeName=N'datetimeoffset' AND c.scale<>3)
 OR ((e.Name LIKE N'%ExternalId' OR e.Name=N'Source') AND ISNULL(c.collation_name,N'')<>N'Latin1_General_100_BIN2'))
 SET @CV048Ready=0;
IF OBJECT_ID(N'tempdb..#CV048CreationChecks') IS NOT NULL
 OR OBJECT_ID(N'tempdb..#CV048VersionChecks') IS NOT NULL
 OR OBJECT_ID(N'tempdb..#CV048ActiveChecks') IS NOT NULL
 OR OBJECT_ID(N'tempdb..#CV048ActivationChecks') IS NOT NULL
 OR OBJECT_ID(N'tempdb..#CV048BudgetBaselineChecks') IS NOT NULL
 OR OBJECT_ID(N'tempdb..#CV048ExpenseBaselineChecks') IS NOT NULL
 THROW 64823,N'Company budget canonical temp name is already in use.',1;
CREATE TABLE #CV048CreationChecks
(
 CreatedAtUtc datetimeoffset(3) NULL,Source nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
 CHECK ((Source COLLATE Latin1_General_100_BIN2 IN(N'Application',N'Audit',N'Imported') AND CreatedAtUtc IS NOT NULL AND DATEPART(TZOFFSET,CreatedAtUtc)=0)
            OR (Source COLLATE Latin1_General_100_BIN2=N'LegacyUnknown' AND CreatedAtUtc IS NULL))
);
CREATE TABLE #CV048VersionChecks
(
 Id uniqueidentifier NOT NULL,ConcurrencyStamp uniqueidentifier NOT NULL,Name nvarchar(200) NOT NULL,
 CreatedAtUtc datetimeoffset(3) NOT NULL,PublishedAtUtc datetimeoffset(3) NULL,PublishedInstanceCount int NULL,
 CHECK(Id<>'00000000-0000-0000-0000-000000000000' AND ConcurrencyStamp<>'00000000-0000-0000-0000-000000000000'),
 CHECK(DATALENGTH(Name) BETWEEN 2 AND 400 AND DATALENGTH(Name)=DATALENGTH(LTRIM(RTRIM(Name)))),
 CHECK(DATEPART(TZOFFSET,CreatedAtUtc)=0
 AND ((PublishedAtUtc IS NULL AND PublishedInstanceCount IS NULL)
 OR (PublishedAtUtc IS NOT NULL AND DATEPART(TZOFFSET,PublishedAtUtc)=0
 AND PublishedInstanceCount IS NOT NULL AND PublishedInstanceCount>=0)))
);
CREATE TABLE #CV048ActiveChecks
(
 Id tinyint NOT NULL,ConcurrencyStamp uniqueidentifier NOT NULL,ActivatedAtUtc datetimeoffset(3) NOT NULL,
 CHECK(Id=1 AND ConcurrencyStamp<>'00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET,ActivatedAtUtc)=0)
);
CREATE TABLE #CV048ActivationChecks
(
 Id uniqueidentifier NOT NULL,ActiveConcurrencyStamp uniqueidentifier NOT NULL,ActivatedAtUtc datetimeoffset(3) NOT NULL,
 CHECK(Id<>'00000000-0000-0000-0000-000000000000'
 AND ActiveConcurrencyStamp<>'00000000-0000-0000-0000-000000000000' AND DATEPART(TZOFFSET,ActivatedAtUtc)=0)
);
CREATE TABLE #CV048BudgetBaselineChecks
(
 ActiveConcurrencyStamp uniqueidentifier NOT NULL,
 CHECK(ActiveConcurrencyStamp<>'00000000-0000-0000-0000-000000000000')
);
CREATE TABLE #CV048ExpenseBaselineChecks
(
 SnapshotNumber int NOT NULL,ActiveConcurrencyStamp uniqueidentifier NOT NULL,
 CHECK(SnapshotNumber>0 AND ActiveConcurrencyStamp<>'00000000-0000-0000-0000-000000000000')
);
DECLARE @CV048Checks TABLE(TableName sysname,Name sysname,ExpectedParentColumnId int,CanonicalObject sysname,CanonicalDefinition nvarchar(max));
INSERT @CV048Checks VALUES
 (N'CompanyBudgetInstanceCreations',N'CK_CompanyBudgetInstanceCreations_provenance',0,N'tempdb..#CV048CreationChecks',NULL),
 (N'CompanyBudgetVersions',N'CK_CompanyBudgetVersions_identity',0,N'tempdb..#CV048VersionChecks',NULL),
 (N'CompanyBudgetVersions',N'CK_CompanyBudgetVersions_name',2,N'tempdb..#CV048VersionChecks',NULL),
 (N'CompanyBudgetVersions',N'CK_CompanyBudgetVersions_utc',0,N'tempdb..#CV048VersionChecks',NULL),
 (N'CompanyBudgetActiveVersion',N'CK_CompanyBudgetActiveVersion_single',0,N'tempdb..#CV048ActiveChecks',NULL),
 (N'CompanyBudgetActivations',N'CK_CompanyBudgetActivations_identity',0,N'tempdb..#CV048ActivationChecks',NULL),
 (N'CompanyBudgetApprovalBaselines',N'CK_CompanyBudgetApprovalBaselines_stamp',3,N'tempdb..#CV048BudgetBaselineChecks',NULL),
 (N'CompanyExpenseApprovalBaselines',N'CK_CompanyExpenseApprovalBaselines_stamp',0,N'tempdb..#CV048ExpenseBaselineChecks',NULL);
UPDATE e SET CanonicalDefinition=c.definition FROM @CV048Checks e JOIN tempdb.sys.check_constraints c
 ON c.parent_object_id=OBJECT_ID(e.CanonicalObject)
 AND ((e.Name=N'CK_CompanyBudgetVersions_identity' AND c.definition LIKE N'%ConcurrencyStamp%' AND c.definition NOT LIKE N'%Published%')
 OR (e.Name=N'CK_CompanyBudgetVersions_name' AND c.definition LIKE N'%DATALENGTH%')
 OR (e.Name=N'CK_CompanyBudgetVersions_utc' AND c.definition LIKE N'%Published%')
 OR e.TableName<>N'CompanyBudgetVersions');
IF EXISTS(SELECT 1 FROM @CV048Checks e JOIN @CV048Tables t ON t.Name=e.TableName
 LEFT JOIN sys.check_constraints c ON c.parent_object_id=t.ObjectId AND c.name COLLATE Latin1_General_100_BIN2=e.Name
 WHERE c.object_id IS NULL OR c.parent_column_id<>e.ExpectedParentColumnId OR c.is_system_named<>0 OR e.CanonicalDefinition IS NULL
 OR REPLACE(REPLACE(REPLACE(REPLACE(c.definition,N' ',N''),CHAR(13),N''),CHAR(10),N''),CHAR(9),N'') COLLATE Latin1_General_100_BIN2
 <> REPLACE(REPLACE(REPLACE(REPLACE(e.CanonicalDefinition,N' ',N''),CHAR(13),N''),CHAR(10),N''),CHAR(9),N'') COLLATE Latin1_General_100_BIN2)
 SET @CV048Ready=0;
DROP TABLE #CV048CreationChecks;
DROP TABLE #CV048VersionChecks;
DROP TABLE #CV048ActiveChecks;
DROP TABLE #CV048ActivationChecks;
DROP TABLE #CV048BudgetBaselineChecks;
DROP TABLE #CV048ExpenseBaselineChecks;
DECLARE @CV048Indexes TABLE(TableName sysname,Name sysname,IsPrimary bit,IsUnique bit,Keys nvarchar(200));
INSERT @CV048Indexes VALUES
 (N'CompanyBudgetInstanceCreations',N'PK_CompanyBudgetInstanceCreations',1,1,N'BudgetInstanceId'),
 (N'CompanyBudgetVersions',N'PK_CompanyBudgetVersions',1,1,N'Id'),
 (N'CompanyBudgetVersionInstances',N'PK_CompanyBudgetVersionInstances',1,1,N'VersionId,BudgetInstanceId'),
 (N'CompanyBudgetVersionInstances',N'IX_CompanyBudgetVersionInstances_Instance',0,0,N'BudgetInstanceId'),
 (N'CompanyBudgetActiveVersion',N'PK_CompanyBudgetActiveVersion',1,1,N'Id'),
 (N'CompanyBudgetActivations',N'PK_CompanyBudgetActivations',1,1,N'Id'),
 (N'CompanyBudgetActivations',N'UX_CompanyBudgetActivations_Stamp',0,1,N'ActiveConcurrencyStamp'),
 (N'CompanyBudgetApprovalBaselines',N'PK_CompanyBudgetApprovalBaselines',1,1,N'ReviewId'),
 (N'CompanyExpenseApprovalBaselines',N'PK_CompanyExpenseApprovalBaselines',1,1,N'ExpenseRequestId,SnapshotNumber');
IF EXISTS(SELECT 1 FROM @CV048Tables t WHERE (SELECT COUNT(*) FROM sys.indexes WHERE object_id=t.ObjectId AND index_id>0)
 <> (SELECT COUNT(*) FROM @CV048Indexes WHERE TableName=t.Name))
 OR EXISTS(SELECT 1 FROM @CV048Indexes e JOIN @CV048Tables t ON t.Name=e.TableName
 LEFT JOIN sys.indexes i ON i.object_id=t.ObjectId AND i.name COLLATE Latin1_General_100_BIN2=e.Name
 WHERE i.index_id IS NULL OR i.is_primary_key<>e.IsPrimary OR i.is_unique<>e.IsUnique OR i.is_disabled=1
 OR i.type<>CASE WHEN e.IsPrimary=1 THEN 1 ELSE 2 END OR i.is_unique_constraint<>0 OR i.ignore_dup_key<>0
 OR i.is_hypothetical=1 OR i.has_filter=1 OR i.filter_definition IS NOT NULL
 OR EXISTS(SELECT 1 FROM sys.index_columns WHERE object_id=t.ObjectId AND index_id=i.index_id AND is_descending_key=1)
 OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=t.ObjectId AND index_id=i.index_id AND is_included_column=1)<>0
 OR STUFF((SELECT N','+c.name FROM sys.index_columns k JOIN sys.columns c ON c.object_id=k.object_id AND c.column_id=k.column_id
     WHERE k.object_id=t.ObjectId AND k.index_id=i.index_id AND k.key_ordinal>0 ORDER BY k.key_ordinal FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,1,N'') COLLATE Latin1_General_100_BIN2<>e.Keys COLLATE Latin1_General_100_BIN2)
 SET @CV048Ready=0;
DECLARE @CV048ForeignKeys TABLE(TableName sysname,Name sysname,Ordinal int,ColumnName sysname,Target sysname,TargetColumn sysname);
INSERT @CV048ForeignKeys VALUES
 (N'CompanyBudgetInstanceCreations',N'FK_CompanyBudgetInstanceCreations_Instance',1,N'BudgetInstanceId',N'BudgetInstances',N'Id'),
 (N'CompanyBudgetVersions',N'FK_CompanyBudgetVersions_Employee',1,N'CreatedByEmployeeId',N'Employees',N'Id'),
 (N'CompanyBudgetVersionInstances',N'FK_CompanyBudgetVersionInstances_Version',1,N'VersionId',N'CompanyBudgetVersions',N'Id'),
 (N'CompanyBudgetVersionInstances',N'FK_CompanyBudgetVersionInstances_Instance',1,N'BudgetInstanceId',N'BudgetInstances',N'Id'),
 (N'CompanyBudgetActiveVersion',N'FK_CompanyBudgetActiveVersion_Version',1,N'VersionId',N'CompanyBudgetVersions',N'Id'),
 (N'CompanyBudgetActiveVersion',N'FK_CompanyBudgetActiveVersion_Employee',1,N'ActivatedByEmployeeId',N'Employees',N'Id'),
 (N'CompanyBudgetActivations',N'FK_CompanyBudgetActivations_Version',1,N'VersionId',N'CompanyBudgetVersions',N'Id'),
 (N'CompanyBudgetActivations',N'FK_CompanyBudgetActivations_Previous',1,N'PreviousVersionId',N'CompanyBudgetVersions',N'Id'),
 (N'CompanyBudgetActivations',N'FK_CompanyBudgetActivations_Employee',1,N'EmployeeId',N'Employees',N'Id'),
 (N'CompanyBudgetApprovalBaselines',N'FK_CompanyBudgetApprovalBaselines_Review',1,N'ReviewId',N'BudgetApprovalSubmissions',N'Id'),
 (N'CompanyBudgetApprovalBaselines',N'FK_CompanyBudgetApprovalBaselines_Version',1,N'VersionId',N'CompanyBudgetVersions',N'Id'),
 (N'CompanyExpenseApprovalBaselines',N'FK_CompanyExpenseApprovalBaselines_Route',1,N'ExpenseRequestId',N'ExpenseRequestApprovalRouteSnapshots',N'ExpenseRequestId'),
 (N'CompanyExpenseApprovalBaselines',N'FK_CompanyExpenseApprovalBaselines_Route',2,N'SnapshotNumber',N'ExpenseRequestApprovalRouteSnapshots',N'SnapshotNumber'),
 (N'CompanyExpenseApprovalBaselines',N'FK_CompanyExpenseApprovalBaselines_Version',1,N'VersionId',N'CompanyBudgetVersions',N'Id');
IF EXISTS(SELECT TableName COLLATE Latin1_General_100_BIN2,Name COLLATE Latin1_General_100_BIN2,Ordinal,
 ColumnName COLLATE Latin1_General_100_BIN2,Target COLLATE Latin1_General_100_BIN2,TargetColumn COLLATE Latin1_General_100_BIN2 FROM @CV048ForeignKeys
 EXCEPT SELECT t.Name COLLATE Latin1_General_100_BIN2,f.name COLLATE Latin1_General_100_BIN2,k.constraint_column_id,
 c.name COLLATE Latin1_General_100_BIN2,OBJECT_NAME(f.referenced_object_id) COLLATE Latin1_General_100_BIN2,r.name COLLATE Latin1_General_100_BIN2
 FROM @CV048Tables t JOIN sys.foreign_keys f ON f.parent_object_id=t.ObjectId
 JOIN sys.foreign_key_columns k ON k.constraint_object_id=f.object_id
 JOIN sys.columns c ON c.object_id=k.parent_object_id AND c.column_id=k.parent_column_id
 JOIN sys.columns r ON r.object_id=k.referenced_object_id AND r.column_id=k.referenced_column_id
 WHERE OBJECT_SCHEMA_NAME(f.referenced_object_id)=N'bms_web')
 OR EXISTS(SELECT t.Name COLLATE Latin1_General_100_BIN2,f.name COLLATE Latin1_General_100_BIN2,k.constraint_column_id,
 c.name COLLATE Latin1_General_100_BIN2,OBJECT_NAME(f.referenced_object_id) COLLATE Latin1_General_100_BIN2,r.name COLLATE Latin1_General_100_BIN2
 FROM @CV048Tables t JOIN sys.foreign_keys f ON f.parent_object_id=t.ObjectId
 JOIN sys.foreign_key_columns k ON k.constraint_object_id=f.object_id
 JOIN sys.columns c ON c.object_id=k.parent_object_id AND c.column_id=k.parent_column_id
 JOIN sys.columns r ON r.object_id=k.referenced_object_id AND r.column_id=k.referenced_column_id
 EXCEPT SELECT TableName COLLATE Latin1_General_100_BIN2,Name COLLATE Latin1_General_100_BIN2,Ordinal,
 ColumnName COLLATE Latin1_General_100_BIN2,Target COLLATE Latin1_General_100_BIN2,TargetColumn COLLATE Latin1_General_100_BIN2 FROM @CV048ForeignKeys)
 SET @CV048Ready=0;
-- Module definition hashes are appended below; they pin executable immutability
-- and whole-header/no-fallback selection rather than just object presence.
DECLARE @CV048Modules TABLE(Name sysname,ParentName sysname,TypeName char(2),Events int,DefinitionHash varbinary(32));
INSERT @CV048Modules VALUES
 (N'TR_CompanyCreation_Immutable',N'CompanyBudgetInstanceCreations','TR',2,0xE778E2103D903B43A65A3A6B7CF6DE3313869CD1DF4D9D558745A71879398AB2),
 (N'TR_CompanyBudgetVersions_Immutable',N'CompanyBudgetVersions','TR',2,0x4601714E0966A4103DB349B9A0561049B68BE7A408EDFD0905B31808BBDB4340),
 (N'TR_CompanyBudgetMembers_Immutable',N'CompanyBudgetVersionInstances','TR',3,0xB854A4761B038CB100C860C157A00F6F919845800DB822C25714496770F55C33),
 (N'TR_CompanyBudgetActive_Valid',N'CompanyBudgetActiveVersion','TR',3,0x1F24873931BCF4D7DC36F985064A1963348C5702DAE49A7A2691909BF61663BE),
 (N'TR_CompanyBudgetActivation_Immutable',N'CompanyBudgetActivations','TR',2,0x9BD2884ED4189A7733B31D81D5A2D8A379154BC236A126978C5F9DC9EC552C7C),
 (N'TR_CompanyBudgetBaseline_Immutable',N'CompanyBudgetApprovalBaselines','TR',2,0x64495E262790CD9CE29ED46D6D6F95914D0D37C8DA0E996BB61D654347AACFD5),
 (N'TR_CompanyExpenseBaseline_Immutable',N'CompanyExpenseApprovalBaselines','TR',2,0xE0561F12D64188517A2793FFE35E1381D30BB2C3DC67CE9FFABD4F26E113CCCA),
 (N'TR_CompanyPublishedBudgetHeader_Immutable',N'BudgetInstances','TR',2,0x0E800B0E133481C151EEBB1F3AED394AA81779F8846909B1680169B8E2C94878),
 (N'TR_CompanyPublishedBudgetLines_Immutable',N'BudgetInstanceLines','TR',3,0x454BFB7CB3EB0DDB401D4E75A4E94061610A70262761244B555FE719AE469A5D),
 (N'fn_CompanyBudgetWinner',N'','IF',0,0x7884AFBCD32113492701C1DDD9576E41D482340E5932B7CD7589170830355DCE),
 (N'v_CompanyBudgetExecutionPositions',N'','V',0,0xED04CE90CE1089DDECED84180CDD3D8CB64B4C493363827C4A348A5DF00856CB);
IF EXISTS(SELECT 1 FROM @CV048Modules e LEFT JOIN sys.objects o
 ON o.object_id=OBJECT_ID(N'bms_web.'+e.Name) AND o.type COLLATE Latin1_General_100_BIN2=e.TypeName COLLATE Latin1_General_100_BIN2
 -- Exact UTF-16 DDL bytes preserve BIN2 string literals; do not uppercase
 -- or strip whitespace inside literals while hashing executable finance rules.
 WHERE o.object_id IS NULL OR HASHBYTES('SHA2_256',OBJECT_DEFINITION(o.object_id))<>e.DefinitionHash
 OR OBJECT_DEFINITION(o.object_id) IS NULL
 OR (e.TypeName='TR' AND NOT EXISTS(SELECT 1 FROM sys.triggers t WHERE t.object_id=o.object_id
 AND t.parent_id=OBJECT_ID(N'bms_web.'+e.ParentName) AND t.parent_class=1 AND t.is_disabled=0
 AND t.is_instead_of_trigger=0 AND t.is_not_for_replication=0
 AND (SELECT COUNT(*) FROM sys.trigger_events WHERE object_id=o.object_id)=e.Events)))
 OR EXISTS(SELECT 1 FROM @CV048Tables t JOIN sys.triggers tr ON tr.parent_id=t.ObjectId
 WHERE tr.is_ms_shipped=0 AND NOT EXISTS(SELECT 1 FROM @CV048Modules e
 WHERE e.Name COLLATE Latin1_General_100_BIN2=tr.name COLLATE Latin1_General_100_BIN2
 AND e.ParentName COLLATE Latin1_General_100_BIN2=t.Name COLLATE Latin1_General_100_BIN2))
 SET @CV048Ready=0;
DECLARE @CV048DataReady bit=0;
IF @CV048Ready=1
 EXEC sys.sp_executesql N'SELECT @ok=CASE WHEN
 (SELECT COUNT(*) FROM bms_web.CompanyBudgetActiveVersion)=1
 AND NOT EXISTS(SELECT 1 FROM bms_web.BudgetInstances b WHERE NOT EXISTS
   (SELECT 1 FROM bms_web.CompanyBudgetInstanceCreations c WHERE c.BudgetInstanceId=b.Id))
 AND NOT EXISTS(SELECT 1 FROM bms_web.CompanyBudgetActiveVersion a
 LEFT JOIN bms_web.CompanyBudgetVersions v ON v.Id=a.VersionId
 WHERE a.Id<>1 OR v.Id IS NULL OR v.PublishedAtUtc IS NULL OR v.PublishedInstanceCount IS NULL
 OR v.PublishedInstanceCount<>(SELECT COUNT(*) FROM bms_web.CompanyBudgetVersionInstances m WHERE m.VersionId=v.Id))
 AND NOT EXISTS(SELECT 1 FROM bms_web.CompanyBudgetVersions v WHERE v.PublishedAtUtc IS NOT NULL
 AND (v.PublishedInstanceCount IS NULL OR v.PublishedInstanceCount<>(SELECT COUNT(*) FROM bms_web.CompanyBudgetVersionInstances m WHERE m.VersionId=v.Id)
 OR EXISTS(SELECT 1 FROM bms_web.CompanyBudgetVersionInstances m WHERE m.VersionId=v.Id
 AND NOT EXISTS(SELECT 1 FROM bms_web.BudgetInstances b WHERE b.Id=m.BudgetInstanceId
 AND b.Status COLLATE Latin1_General_100_BIN2=N''Approved'' AND b.ApprovalOrdinal IS NOT NULL AND b.ApprovedAtUtc IS NOT NULL))))
 AND EXISTS(SELECT 1 FROM bms_web.CompanyBudgetActiveVersion a JOIN bms_web.CompanyBudgetActivations h
 ON h.VersionId=a.VersionId AND h.ActiveConcurrencyStamp=a.ConcurrencyStamp)
 THEN 1 ELSE 0 END;',N'@ok bit OUTPUT',@ok=@CV048DataReady OUTPUT;
IF @CV048DataReady<>1 SET @CV048Ready=0;
/* SQL048 COMPANY VERSION SHAPE END */
 IF @CV048Ready<>1 THROW 64822,N'Company budget physical storage or active data drift.',1;
 -- Identical whole-header/month winners imply identical gross receipt,
 -- payment and per-article limits: all source line bytes remain untouched.
 IF @CV048Initial=1 AND
 (EXISTS(SELECT 1 FROM @CV048LegacyWinners old CROSS JOIN bms_web.CompanyBudgetActiveVersion a
 OUTER APPLY bms_web.fn_CompanyBudgetWinner(a.VersionId,old.CostCenterId,old.BudgetMonth) winner
 WHERE old.InstanceId<>winner.Id OR (old.InstanceId IS NULL AND winner.Id IS NOT NULL)
 OR (old.InstanceId IS NOT NULL AND winner.Id IS NULL))
 OR EXISTS(SELECT Id FROM bms_web.BudgetInstances WHERE Status COLLATE Latin1_General_100_BIN2=N'Approved'
 EXCEPT SELECT m.BudgetInstanceId FROM bms_web.CompanyBudgetVersionInstances m JOIN bms_web.CompanyBudgetActiveVersion a ON a.VersionId=m.VersionId)
 OR EXISTS(SELECT m.BudgetInstanceId FROM bms_web.CompanyBudgetVersionInstances m JOIN bms_web.CompanyBudgetActiveVersion a ON a.VersionId=m.VersionId
 EXCEPT SELECT Id FROM bms_web.BudgetInstances WHERE Status COLLATE Latin1_General_100_BIN2=N'Approved'))
 THROW 64825,N'Initial company budget seed does not preserve legacy whole-month plans and limits.',1;
 IF (SELECT COUNT(*) FROM bms_web.CompanyBudgetActiveVersion)<>1
    OR OBJECT_ID(N'bms_web.fn_CompanyBudgetWinner',N'IF') IS NULL
    OR OBJECT_ID(N'bms_web.v_CompanyBudgetExecutionPositions',N'V') IS NULL
    OR EXISTS(SELECT 1 FROM bms_web.CompanyBudgetActiveVersion a JOIN bms_web.CompanyBudgetVersions v ON v.Id=a.VersionId WHERE v.PublishedAtUtc IS NULL)
    THROW 64820,N'Company budget postcondition failed.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
