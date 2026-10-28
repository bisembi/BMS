-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 64501, N'SQL045 requires the explicitly owned BMS database.', 1;
IF OBJECT_ID(N'bms_web.schema_version',N'U') IS NULL THROW 64502,N'Missing schema registry.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 IF (SELECT COUNT(*) FROM bms_web.schema_version WITH(UPDLOCK,HOLDLOCK) WHERE version_no BETWEEN 1 AND 43)<>43
    OR EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no NOT BETWEEN 1 AND 43 AND version_no<>45)
    THROW 64503,N'SQL045 requires exact SQL001-SQL043; SQL044 is deliberately excluded.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=45)
 BEGIN
  IF OBJECT_ID(N'bms_web.CfoInitiatorCostCenterAssignments',N'U') IS NOT NULL
     OR OBJECT_ID(N'bms_web.BudgetApprovalSubmissions',N'U') IS NOT NULL
     OR EXISTS(SELECT 1 FROM bms_web.WorkflowPermissions WHERE DefinitionVersion=2)
     THROW 64504,N'Unregistered feature storage already exists.',1;
  CREATE TABLE bms_web.CfoInitiatorCostCenterAssignments
  (
   EmployeeId uniqueidentifier NOT NULL,
   CostCenterId uniqueidentifier NOT NULL,
   IsActive bit NOT NULL,
   CONSTRAINT PK_bms_web_CfoInitiatorCostCenterAssignments PRIMARY KEY(EmployeeId,CostCenterId),
   CONSTRAINT FK_bms_web_CfoInitiator_Employee FOREIGN KEY(EmployeeId) REFERENCES bms_web.Employees(Id),
   CONSTRAINT FK_bms_web_CfoInitiator_CostCenter FOREIGN KEY(CostCenterId) REFERENCES bms_web.CostCenters(Id)
  );
  CREATE INDEX IX_bms_web_CfoInitiator_CostCenter ON bms_web.CfoInitiatorCostCenterAssignments(CostCenterId,IsActive,EmployeeId);
  CREATE TABLE bms_web.BudgetApprovalSubmissions
  (
   Id uniqueidentifier NOT NULL,
   BudgetInstanceId uniqueidentifier NOT NULL,
   CostCenterId uniqueidentifier NOT NULL,
   SubmissionNumber int NOT NULL,
   ProposalConcurrencyStamp uniqueidentifier NOT NULL,
   ConcurrencyStamp uniqueidentifier NOT NULL,
   SubmittedByEmployeeId uniqueidentifier NOT NULL,
   SubmittedByExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
   SubmittedAtUtc datetimeoffset(7) NOT NULL,
   RequiresOwnerReview bit NOT NULL,
   PositiveExcessKzt decimal(18,2) NOT NULL,
   State nvarchar(16) NOT NULL,
   CurrentStageNumber int NULL,
   CellsJson nvarchar(max) NOT NULL,
   StagesJson nvarchar(max) NOT NULL,
   DecisionsJson nvarchar(max) NOT NULL,
   CONSTRAINT PK_bms_web_BudgetApprovalSubmissions PRIMARY KEY(Id),
   CONSTRAINT FK_bms_web_BudgetApproval_Budget FOREIGN KEY(BudgetInstanceId) REFERENCES bms_web.BudgetInstances(Id) ON DELETE CASCADE,
   CONSTRAINT FK_bms_web_BudgetApproval_CostCenter FOREIGN KEY(CostCenterId) REFERENCES bms_web.CostCenters(Id),
   CONSTRAINT FK_bms_web_BudgetApproval_Employee FOREIGN KEY(SubmittedByEmployeeId) REFERENCES bms_web.Employees(Id),
   CONSTRAINT CK_bms_web_BudgetApproval_Identity CHECK(Id<>'00000000-0000-0000-0000-000000000000' AND ProposalConcurrencyStamp<>'00000000-0000-0000-0000-000000000000'
      AND ConcurrencyStamp<>'00000000-0000-0000-0000-000000000000' AND SubmissionNumber>0),
   CONSTRAINT CK_bms_web_BudgetApproval_State CHECK((State=N'Pending' AND CurrentStageNumber BETWEEN 1 AND 4)
      OR (State IN(N'Returned',N'Rejected',N'Approved') AND CurrentStageNumber IS NULL)),
   CONSTRAINT CK_bms_web_BudgetApproval_Amount CHECK(PositiveExcessKzt>=0),
   CONSTRAINT CK_bms_web_BudgetApproval_Json CHECK(ISJSON(CellsJson)=1 AND ISJSON(StagesJson)=1 AND ISJSON(DecisionsJson)=1),
   CONSTRAINT CK_bms_web_BudgetApproval_UTC CHECK(DATEPART(TZOFFSET,SubmittedAtUtc)=0),
   CONSTRAINT CK_bms_web_BudgetApproval_Actor CHECK(DATALENGTH(SubmittedByExternalId)>0 AND DATALENGTH(SubmittedByExternalId)=DATALENGTH(LTRIM(RTRIM(SubmittedByExternalId))))
  );
  CREATE UNIQUE INDEX UX_bms_web_BudgetApproval_Submission ON bms_web.BudgetApprovalSubmissions(BudgetInstanceId,SubmissionNumber);
  CREATE UNIQUE INDEX UX_bms_web_BudgetApproval_Pending ON bms_web.BudgetApprovalSubmissions(BudgetInstanceId) WHERE State=N'Pending';
  IF NOT EXISTS(SELECT 1 FROM bms_web.FunctionalRoles WHERE Code=N'CFO_INITIATOR')
   INSERT INTO bms_web.FunctionalRoles(Id,Code,Name,IsActive) VALUES(NEWID(),N'CFO_INITIATOR',N'Инициатор ЦФО',1);
  INSERT INTO bms_web.WorkflowPermissions
   (DefinitionVersion,StatusCode,StageNumber,RoleCode,CommandCode,TargetStatusCode,TargetStageNumber,ConditionCode,IsVisible,IsEnabled,IsProvisional)
  SELECT 2,StatusCode,CASE WHEN StatusCode=N'IN_APPROVAL' THEN StageNumber+1 ELSE StageNumber END,
   RoleCode,CommandCode,TargetStatusCode,CASE WHEN StatusCode=N'IN_APPROVAL' AND TargetStageNumber>0 THEN TargetStageNumber+1 ELSE TargetStageNumber END,
   ConditionCode,IsVisible,IsEnabled,IsProvisional
  FROM bms_web.WorkflowPermissions WHERE DefinitionVersion=1;
  INSERT INTO bms_web.WorkflowPermissions
   (DefinitionVersion,StatusCode,StageNumber,RoleCode,CommandCode,TargetStatusCode,TargetStageNumber,ConditionCode,IsVisible,IsEnabled,IsProvisional)
  VALUES
   (2,N'IN_APPROVAL',1,N'BUDGET_OWNER',N'APPROVE',N'IN_APPROVAL',2,N'ROUTE_HAS_NEXT_STAGE',1,1,0),
   (2,N'IN_APPROVAL',1,N'BUDGET_OWNER',N'RETURN',N'RETURNED',0,N'',1,1,0),
   (2,N'IN_APPROVAL',1,N'BUDGET_OWNER',N'REJECT',N'REJECTED',0,N'',1,1,0),
   (2,N'IN_APPROVAL',1,N'Инициатор',N'CANCEL',N'CANCELLED',0,N'',1,1,0);
  EXEC(N'CREATE TRIGGER bms_web.TR_BudgetApproval_Immutable ON bms_web.BudgetApprovalSubmissions AFTER INSERT,UPDATE AS
  BEGIN
   SET NOCOUNT ON;
   IF EXISTS(SELECT 1 FROM inserted i JOIN bms_web.BudgetInstances b ON b.Id=i.BudgetInstanceId WHERE b.CostCenterId<>i.CostCenterId)
    THROW 64505,N''Budget review CFO mismatch.'',1;
   IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE
      i.BudgetInstanceId<>d.BudgetInstanceId OR i.CostCenterId<>d.CostCenterId OR i.SubmissionNumber<>d.SubmissionNumber
      OR i.ProposalConcurrencyStamp<>d.ProposalConcurrencyStamp OR i.SubmittedByEmployeeId<>d.SubmittedByEmployeeId
      OR i.SubmittedByExternalId COLLATE Latin1_General_100_BIN2<>d.SubmittedByExternalId COLLATE Latin1_General_100_BIN2
      OR DATALENGTH(i.SubmittedByExternalId)<>DATALENGTH(d.SubmittedByExternalId)
      OR i.SubmittedAtUtc<>d.SubmittedAtUtc OR i.RequiresOwnerReview<>d.RequiresOwnerReview OR i.PositiveExcessKzt<>d.PositiveExcessKzt
      OR i.CellsJson COLLATE Latin1_General_100_BIN2<>d.CellsJson COLLATE Latin1_General_100_BIN2 OR DATALENGTH(i.CellsJson)<>DATALENGTH(d.CellsJson)
      OR i.StagesJson COLLATE Latin1_General_100_BIN2<>d.StagesJson COLLATE Latin1_General_100_BIN2 OR DATALENGTH(i.StagesJson)<>DATALENGTH(d.StagesJson)
      OR d.State<>N''Pending'')
    THROW 64506,N''Frozen budget submission cannot be rewritten.'',1;
  END;');
  INSERT INTO bms_web.schema_version(version_no,script_name,applied_at_utc)
   VALUES(45,N'045_cfo_initiators_and_budget_approval.sql',SYSUTCDATETIME());
 END;
 IF OBJECT_ID(N'bms_web.CfoInitiatorCostCenterAssignments',N'U') IS NULL
    OR OBJECT_ID(N'bms_web.BudgetApprovalSubmissions',N'U') IS NULL
    OR OBJECT_ID(N'bms_web.TR_BudgetApproval_Immutable',N'TR') IS NULL
    OR (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CfoInitiatorCostCenterAssignments'))<>3
    OR (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.BudgetApprovalSubmissions'))<>16
    OR (SELECT COUNT(*) FROM bms_web.WorkflowPermissions WHERE DefinitionVersion=1)<>26
    OR (SELECT COUNT(*) FROM bms_web.WorkflowPermissions WHERE DefinitionVersion=2)<>30
    OR (SELECT COUNT(*) FROM bms_web.WorkflowPermissions)<>56
    OR NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=45 AND script_name COLLATE Latin1_General_100_BIN2=N'045_cfo_initiators_and_budget_approval.sql')
    THROW 64507,N'Feature schema identity mismatch.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
