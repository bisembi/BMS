SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() NOT IN(N'DataBI',N'BmsAccrualEdit311_Test') THROW 64341,'EXACT_ORIGIN_DATABASE_REQUIRED',1;
IF DB_NAME()=N'DataBI' AND (SELECT MAX(version_no) FROM bms_web.schema_version)<>61 THROW 64341,'SCHEMA61_REQUIRED',1;
IF DB_NAME()=N'BmsAccrualEdit311_Test' AND NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BmsAccrualEdit311Owner' AND CONVERT(nvarchar(128),value)=N'BMS_ACCRUAL_EDIT_311_ISOLATED') THROW 64341,'OWNED_CLONE_REQUIRED',1;
IF SCHEMA_ID(N'bms_origin341') IS NULL EXEC(N'CREATE SCHEMA bms_origin341 AUTHORIZATION dbo');
GO
CREATE OR ALTER PROCEDURE bms_origin341.Exchange
 @Operation varchar(10),@RunId uniqueidentifier=NULL,@Payload nvarchar(max)=NULL
WITH EXECUTE AS OWNER
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @Operation='State'
 BEGIN
  SELECT a.ObjectId,JSON_VALUE(a.AfterJson,'$.id'),a.OccurredAtUtc FROM bms_web.BusinessAudit a
   WHERE a.ObjectType=N'CounterpartySourceBinding' AND a.Action=N'CounterpartySourceBound'
    AND EXISTS(SELECT 1 FROM bms_web.Counterparties c WHERE c.Id=TRY_CONVERT(uniqueidentifier,JSON_VALUE(a.AfterJson,'$.id')));
  SELECT TOP(64) ObjectId,OccurredAtUtc,AfterJson FROM bms_web.BusinessAudit
   WHERE ObjectType=N'CounterpartyImport' AND Action IN(N'CounterpartyImportCompleted',N'CounterpartyImportWarnings')
   ORDER BY OccurredAtUtc DESC,Id DESC;
  SELECT ObjectId,AfterJson FROM (SELECT ObjectId,AfterJson,ROW_NUMBER() OVER(PARTITION BY ObjectId ORDER BY OccurredAtUtc DESC,Id DESC) n
   FROM bms_web.BusinessAudit WHERE ObjectType=N'CounterpartySourceOrigin' AND Action=N'CounterpartyOriginsCaptured') a WHERE n=1;
  SELECT ObjectId FROM bms_web.BusinessAudit WHERE ObjectType=N'CounterpartyOriginProjection' AND Action=N'CounterpartyOriginsProjected';
  RETURN;
 END;
 IF @Operation<>'Publish' OR @RunId IS NULL OR ISJSON(@Payload)<>1 OR DATALENGTH(@Payload)>10000000 THROW 64341,'INVALID_ORIGIN_COMMAND',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@Payload) WHERE [key] NOT IN(N'imports',N'origins') OR type<>4)
  OR (SELECT COUNT(*) FROM OPENJSON(@Payload))<>2 OR JSON_QUERY(@Payload,'$.imports') IS NULL OR JSON_QUERY(@Payload,'$.origins') IS NULL THROW 64341,'EXACT_ORIGIN_PAYLOAD_REQUIRED',1;
 DECLARE @imports TABLE(Id uniqueidentifier NOT NULL PRIMARY KEY);
 IF EXISTS(SELECT 1 FROM OPENJSON(@Payload,'$.imports') WHERE type<>1 OR TRY_CONVERT(uniqueidentifier,value) IS NULL)
  OR (SELECT COUNT(*) FROM OPENJSON(@Payload,'$.imports')) NOT BETWEEN 1 AND 64 THROW 64341,'INVALID_IMPORT_EVIDENCE',1;
 INSERT @imports SELECT TRY_CONVERT(uniqueidentifier,value) FROM OPENJSON(@Payload,'$.imports');
 IF EXISTS(SELECT 1 FROM @imports i WHERE NOT EXISTS(SELECT 1 FROM bms_web.BusinessAudit a WHERE a.ObjectType=N'CounterpartyImport'
  AND a.Action IN(N'CounterpartyImportCompleted',N'CounterpartyImportWarnings') AND TRY_CONVERT(uniqueidentifier,a.ObjectId)=i.Id)) THROW 64341,'COMMITTED_IMPORT_EVIDENCE_REQUIRED',1;
 IF EXISTS(SELECT 1 FROM OPENJSON(@Payload,'$.origins') WHERE type<>5) THROW 64341,'ORIGIN_OBJECT_REQUIRED',1;
 DECLARE @rows TABLE(Id uniqueidentifier NOT NULL PRIMARY KEY,Sources nvarchar(512) NOT NULL);
 IF EXISTS(SELECT 1 FROM OPENJSON(@Payload,'$.origins') x WHERE TRY_CONVERT(uniqueidentifier,JSON_VALUE(x.value,'$.id')) IS NULL
  OR JSON_QUERY(x.value,'$.sources') IS NULL OR (SELECT COUNT(*) FROM OPENJSON(x.value))<>2
  OR EXISTS(SELECT 1 FROM OPENJSON(x.value) WHERE [key] NOT IN(N'id',N'sources') OR ([key]=N'sources' AND type<>4))
  OR (SELECT COUNT(*) FROM OPENJSON(x.value,'$.sources')) NOT BETWEEN 1 AND 3
  OR EXISTS(SELECT 1 FROM OPENJSON(x.value,'$.sources') s WHERE s.type<>1 OR s.value COLLATE Latin1_General_100_BIN2 NOT IN(N'ExampleAccountingA',N'ExampleAccountingB',N'ExampleAccountingC'))
  OR (SELECT COUNT(*) FROM OPENJSON(x.value,'$.sources'))<>(SELECT COUNT(DISTINCT value) FROM OPENJSON(x.value,'$.sources')))
  THROW 64341,'INVALID_SOURCE_COMPANIES',1;
 INSERT @rows SELECT TRY_CONVERT(uniqueidentifier,JSON_VALUE(value,'$.id')),JSON_QUERY(value,'$.sources') FROM OPENJSON(@Payload,'$.origins');
 IF EXISTS(SELECT 1 FROM @rows r WHERE NOT EXISTS(SELECT 1 FROM bms_web.Counterparties c WHERE c.Id=r.Id)
  OR NOT EXISTS(SELECT 1 FROM bms_web.BusinessAudit a WHERE a.ObjectType=N'CounterpartySourceBinding' AND a.Action=N'CounterpartySourceBound' AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(a.AfterJson,'$.id'))=r.Id))
  THROW 64341,'BOUND_COUNTERPARTY_REQUIRED',1;
 BEGIN TRANSACTION;
 BEGIN TRY
  DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource=N'BMS:CounterpartyOrigin341',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
  IF @lock<0 THROW 64341,'ORIGIN_LOCK_FAILED',1;
  -- Descriptive provenance may grow, never erase an already proven company.
  IF EXISTS(SELECT 1 FROM @rows r CROSS APPLY(SELECT TOP(1) AfterJson FROM bms_web.BusinessAudit a
    WHERE a.ObjectType=N'CounterpartySourceOrigin' AND a.Action=N'CounterpartyOriginsCaptured' AND TRY_CONVERT(uniqueidentifier,a.ObjectId)=r.Id ORDER BY a.OccurredAtUtc DESC,a.Id DESC) old
    CROSS APPLY OPENJSON(old.AfterJson,'$.sources') s WHERE NOT EXISTS(SELECT 1 FROM OPENJSON(r.Sources) n WHERE n.value=s.value)) THROW 64341,'ORIGIN_HISTORY_MUST_BE_PRESERVED',1;
  DECLARE @now datetimeoffset(3)=SYSUTCDATETIME();
  INSERT bms_web.BusinessAudit(Id,OccurredAtUtc,ActorExternalId,ObjectType,ObjectId,Action,BeforeJson,AfterJson,CorrelationId)
   SELECT NEWID(),@now,N'BMS:CounterpartyOrigin341',N'CounterpartySourceOrigin',CONVERT(nvarchar(36),r.Id),N'CounterpartyOriginsCaptured',NULL,
    N'{"sources":'+r.Sources+N'}',REPLACE(CONVERT(varchar(36),@RunId),'-','') FROM @rows r
   OUTER APPLY(SELECT TOP(1) JSON_QUERY(AfterJson,'$.sources') Sources FROM bms_web.BusinessAudit a
    WHERE a.ObjectType=N'CounterpartySourceOrigin' AND a.Action=N'CounterpartyOriginsCaptured' AND TRY_CONVERT(uniqueidentifier,a.ObjectId)=r.Id ORDER BY a.OccurredAtUtc DESC,a.Id DESC) old
   WHERE old.Sources IS NULL OR old.Sources<>r.Sources;
  DECLARE @changed int=@@ROWCOUNT;
  INSERT bms_web.BusinessAudit(Id,OccurredAtUtc,ActorExternalId,ObjectType,ObjectId,Action,BeforeJson,AfterJson,CorrelationId)
   SELECT NEWID(),@now,N'BMS:CounterpartyOrigin341',N'CounterpartyOriginProjection',CONVERT(nvarchar(36),i.Id),N'CounterpartyOriginsProjected',NULL,
    N'{"counterpartyImportRunId":"'+CONVERT(nvarchar(36),i.Id)+N'"}',REPLACE(CONVERT(varchar(36),@RunId),'-','') FROM @imports i
   WHERE NOT EXISTS(SELECT 1 FROM bms_web.BusinessAudit a WHERE a.ObjectType=N'CounterpartyOriginProjection' AND a.Action=N'CounterpartyOriginsProjected' AND TRY_CONVERT(uniqueidentifier,a.ObjectId)=i.Id);
  COMMIT; SELECT @changed;
 END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH;
END;
GO
IF DB_NAME()=N'DataBI'
BEGIN
 IF DATABASE_PRINCIPAL_ID(N'bms_counterparty323_system') IS NULL THROW 64341,'EXISTING_LIMITED_SYSTEM_USER_REQUIRED',1;
 GRANT EXECUTE ON OBJECT::bms_origin341.Exchange TO bms_counterparty323_system;
END;
