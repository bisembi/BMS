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
IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 64601, N'SQL046 requires the explicitly owned BMS database.', 1;
IF OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL THROW 64602, N'Missing schema registry.', 1;
BEGIN TRY
 BEGIN TRANSACTION;
 IF (SELECT COUNT(*) FROM bms_web.schema_version WITH(UPDLOCK,HOLDLOCK) WHERE version_no BETWEEN 1 AND 43)<>43
    OR NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=45
       AND script_name COLLATE Latin1_General_100_BIN2=N'045_cfo_initiators_and_budget_approval.sql')
    OR EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no NOT BETWEEN 1 AND 43 AND version_no NOT IN(45,46))
    THROW 64603, N'SQL046 requires exact SQL001-SQL043 plus SQL045.', 1;
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.BudgetInstanceLines')
    AND name=N'UX_bms_web_BudgetInstanceLines_Key' AND type=2 AND is_primary_key=0
    AND is_unique_constraint=0 AND is_disabled=0 AND is_hypothetical=0 AND has_filter=0 AND ignore_dup_key=0)
    OR (SELECT STRING_AGG(c.name,N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
        FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        WHERE i.object_id=OBJECT_ID(N'bms_web.BudgetInstanceLines') AND i.name=N'UX_bms_web_BudgetInstanceLines_Key'
        AND ic.key_ordinal>0) COLLATE Latin1_General_100_BIN2<>N'BudgetInstanceId,CashFlowItemId,BudgetMonth,FlowDirection'
    OR EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id
        WHERE i.object_id=OBJECT_ID(N'bms_web.BudgetInstanceLines') AND i.name=N'UX_bms_web_BudgetInstanceLines_Key'
        AND (ic.is_included_column=1 OR ic.is_descending_key=1))
    THROW 64604,N'Unexpected analytical index shape.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=46)
 BEGIN
  IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.BudgetInstanceLines')
      AND name=N'UX_bms_web_BudgetInstanceLines_Key' AND is_unique=1)
      THROW 64605,N'Unregistered nonunique analytical index.',1;
  CREATE NONCLUSTERED INDEX UX_bms_web_BudgetInstanceLines_Key ON bms_web.BudgetInstanceLines
      (BudgetInstanceId,CashFlowItemId,BudgetMonth,FlowDirection) WITH(DROP_EXISTING=ON);
  INSERT INTO bms_web.schema_version(version_no,script_name,applied_at_utc)
      VALUES(46,N'046_budget_instance_operation_rows.sql',SYSUTCDATETIME());
 END;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=46
      AND script_name COLLATE Latin1_General_100_BIN2=N'046_budget_instance_operation_rows.sql'
      AND DATALENGTH(script_name)=DATALENGTH(N'046_budget_instance_operation_rows.sql'))
    OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.BudgetInstanceLines')
      AND name=N'UX_bms_web_BudgetInstanceLines_Key' AND is_unique=0)
      THROW 64606,N'SQL046 postcondition failed.',1;
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
