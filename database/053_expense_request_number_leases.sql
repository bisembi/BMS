-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME()<>CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 65301,N'the explicitly owned BMS database required.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @Lock int;
 EXEC @Lock=sys.sp_getapplock @Resource=N'BMS:SQL053:NumberLeases',@LockMode=N'Exclusive',
  @LockOwner=N'Transaction',@LockTimeout=15000;
 IF @Lock<0 THROW 65302,N'SQL053 lock unavailable.',1;
 IF OBJECT_ID(N'bms_web.schema_version',N'U') IS NULL
  OR NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=52
   AND script_name COLLATE Latin1_General_100_BIN2=N'052_cash_flow_item_display_order.sql'
   AND DATALENGTH(script_name)=DATALENGTH(N'052_cash_flow_item_display_order.sql'))
  THROW 65303,N'Accepted schema52 predecessor required.',1;
 IF EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=53
   AND (script_name COLLATE Latin1_General_100_BIN2<>N'053_expense_request_number_leases.sql'
    OR DATALENGTH(script_name)<>DATALENGTH(N'053_expense_request_number_leases.sql')))
  THROW 65304,N'SQL053 owner differs.',1;
 IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=53)
 BEGIN
  IF (SELECT COUNT(*) FROM bms_web.schema_version)<>51
   OR OBJECT_ID(N'bms_web.ExpenseRequestNumberLeases') IS NOT NULL
   THROW 65305,N'Unexpected predecessor objects.',1;
  CREATE TABLE bms_web.ExpenseRequestNumberLeases (
   CostCenterId uniqueidentifier NOT NULL,
   Ordinal bigint NOT NULL,
   LeaseId uniqueidentifier NOT NULL,
   ActorExternalId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
   Number nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
   ExpiresAtUtc datetimeoffset(7) NOT NULL,
   Consumed bit NOT NULL,
   CONSTRAINT PK_ExpenseRequestNumberLeases PRIMARY KEY CLUSTERED(CostCenterId,Ordinal),
   CONSTRAINT CK_ExpenseRequestNumberLeases_Ordinal CHECK(Ordinal>0),
   CONSTRAINT FK_ExpenseRequestNumberLeases_CostCenters_CostCenterId
    FOREIGN KEY(CostCenterId) REFERENCES bms_web.CostCenters(Id));
  CREATE UNIQUE INDEX IX_ExpenseRequestNumberLeases_LeaseId ON bms_web.ExpenseRequestNumberLeases(LeaseId);
  CREATE UNIQUE INDEX IX_ExpenseRequestNumberLeases_Number ON bms_web.ExpenseRequestNumberLeases(Number);
  INSERT bms_web.schema_version(version_no,script_name) VALUES(53,N'053_expense_request_number_leases.sql');
 END;
DECLARE @NumberLeaseSchema53Ready bit=0;
DECLARE @NumberLeaseObject int=OBJECT_ID(N'bms_web.ExpenseRequestNumberLeases',N'U');
IF @NumberLeaseObject IS NOT NULL
 AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=@NumberLeaseObject)=7
 AND (SELECT COUNT(*) FROM (VALUES
   (1,N'CostCenterId',36,16),(2,N'Ordinal',127,8),(3,N'LeaseId',36,16),
   (4,N'ActorExternalId',231,512),(5,N'Number',231,256),(6,N'ExpiresAtUtc',43,10),(7,N'Consumed',104,1)
  ) e(ordinal,name,type,length) JOIN sys.columns c ON c.object_id=@NumberLeaseObject
    AND c.column_id=e.ordinal AND c.name COLLATE Latin1_General_100_BIN2=e.name
    AND DATALENGTH(c.name)=DATALENGTH(e.name) AND c.system_type_id=e.type AND c.user_type_id=e.type
    AND c.max_length=e.length AND c.is_nullable=0 AND c.is_computed=0 AND c.is_identity=0
    AND c.default_object_id=0 AND c.rule_object_id=0 AND c.is_hidden=0 AND c.is_masked=0
    AND c.is_sparse=0 AND c.is_column_set=0 AND c.generated_always_type=0 AND c.encryption_type IS NULL
    AND (e.type<>231 OR c.collation_name=N'Latin1_General_100_BIN2')
    AND (e.type<>43 OR c.scale=7))=7
 AND (SELECT COUNT(*) FROM sys.indexes WHERE object_id=@NumberLeaseObject AND index_id>0)=3
 AND (SELECT COUNT(*) FROM sys.indexes i JOIN (VALUES
    (N'PK_ExpenseRequestNumberLeases',1,1,N'1,2'),
    (N'IX_ExpenseRequestNumberLeases_LeaseId',2,0,N'3'),
    (N'IX_ExpenseRequestNumberLeases_Number',2,0,N'5')) e(name,type,pk,columns)
    ON i.object_id=@NumberLeaseObject AND i.name COLLATE Latin1_General_100_BIN2=e.name
    AND DATALENGTH(i.name)=DATALENGTH(e.name) AND i.type=e.type AND i.is_primary_key=e.pk
    AND i.is_unique=1 AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
    AND (SELECT STRING_AGG(CONVERT(nvarchar(max),ic.column_id),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
         FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=e.columns
    AND NOT EXISTS(SELECT 1 FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
     AND (ic.is_descending_key=1 OR ic.is_included_column=1)))=3
 AND (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=@NumberLeaseObject)=1
 AND EXISTS(SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id=f.object_id
    WHERE f.parent_object_id=@NumberLeaseObject AND f.referenced_object_id=OBJECT_ID(N'bms_web.CostCenters')
    AND f.name=N'FK_ExpenseRequestNumberLeases_CostCenters_CostCenterId' AND f.is_disabled=0 AND f.is_not_trusted=0
    AND f.is_not_for_replication=0 AND f.delete_referential_action=0 AND f.update_referential_action=0
    AND c.parent_column_id=1 AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'Id'
    AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=f.object_id)=1)
 AND (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=@NumberLeaseObject)=1
 AND EXISTS(SELECT 1 FROM sys.check_constraints c WHERE c.parent_object_id=@NumberLeaseObject
    AND c.name=N'CK_ExpenseRequestNumberLeases_Ordinal' AND c.is_disabled=0 AND c.is_not_trusted=0
    AND c.is_not_for_replication=0
    AND REPLACE(REPLACE(REPLACE(REPLACE(c.definition,N' ',N''),NCHAR(9),N''),NCHAR(13),N''),NCHAR(10),N'')
      COLLATE Latin1_General_100_BIN2=N'([Ordinal]>(0))')
 AND NOT EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id=@NumberLeaseObject)
 SET @NumberLeaseSchema53Ready=1;
 IF @NumberLeaseSchema53Ready<>1 THROW 65306,N'SQL053 shape differs.',1;
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
