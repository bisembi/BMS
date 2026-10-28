-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web — safe SQL Server bootstrap for the implemented draft slice.

    IMPORTANT
    - This schema matches the current EF Core model in BMS_WEB/src/backend.
    - It does not create a database, login, user, role or permission.
    - It refuses to run outside the explicitly owned BMS database.
    - It creates objects only in bms_web and contains no destructive DDL/DML.
    - It has not been executed against an accounting server.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 51001, N'BMS bootstrap refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 51002, N'BMS bootstrap refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NOT NULL
   AND OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
   AND EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'bms_web'))
    THROW 51003, N'BMS bootstrap refused: non-empty unmanaged schema bms_web already exists.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF SCHEMA_ID(N'bms_web') IS NULL
        EXEC(N'CREATE SCHEMA bms_web AUTHORIZATION dbo;');

    IF OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.schema_version
        (
            version_no     int           NOT NULL,
            script_name    nvarchar(260) NOT NULL,
            applied_at_utc datetime2(3)  NOT NULL
                CONSTRAINT DF_bms_web_schema_version_applied DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_bms_web_schema_version PRIMARY KEY (version_no),
            CONSTRAINT CK_bms_web_schema_version_positive CHECK (version_no > 0)
        );
    END;

    IF OBJECT_ID(N'bms_web.ExpenseRequests', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.ExpenseRequests
        (
            Id                    uniqueidentifier NOT NULL,
            Status                nvarchar(40)     NOT NULL,
            InitiatorExternalId   nvarchar(256)    NOT NULL,
            OrganizationId        uniqueidentifier NULL,
            DefaultCostCenterId   uniqueidentifier NULL,
            PlannedPaymentDate    date             NULL,
            CurrencyCode          nvarchar(3)      NOT NULL,
            CounterpartyId        uniqueidentifier NULL,
            ContractId            uniqueidentifier NULL,
            Purpose               nvarchar(1000)   NULL,
            Comment               nvarchar(2000)   NULL,
            BusinessJustification nvarchar(2000)   NULL,
            IsUrgent              bit              NOT NULL
                CONSTRAINT DF_bms_web_ExpenseRequests_IsUrgent DEFAULT (0),
            UrgencyReason         nvarchar(1000)   NULL,
            IsNewCounterparty     bit              NOT NULL,
            IsNewContract         bit              NOT NULL,
            CreatedAtUtc          datetimeoffset(3) NOT NULL,
            UpdatedAtUtc          datetimeoffset(3) NOT NULL,
            ConcurrencyStamp      uniqueidentifier NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseRequests PRIMARY KEY (Id),
            CONSTRAINT CK_bms_web_ExpenseRequests_actor CHECK
                (LEN(LTRIM(RTRIM(InitiatorExternalId))) > 0),
            CONSTRAINT CK_bms_web_ExpenseRequests_currency CHECK
                (CurrencyCode NOT LIKE N'%[^A-Z]%' AND LEN(CurrencyCode) = 3),
            CONSTRAINT CK_bms_web_ExpenseRequests_urgency CHECK
                ((IsUrgent = 0 AND UrgencyReason IS NULL)
                 OR (IsUrgent = 1 AND LEN(LTRIM(RTRIM(UrgencyReason))) > 0))
        );
    END;

    IF OBJECT_ID(N'bms_web.ExpenseRequestLines', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.ExpenseRequestLines
        (
            Id               uniqueidentifier NOT NULL,
            ExpenseRequestId uniqueidentifier NOT NULL,
            CashFlowItemId   uniqueidentifier NULL,
            CostCenterId     uniqueidentifier NULL,
            AccrualPeriod    date             NULL,
            Amount           decimal(19,4)    NOT NULL,
            CONSTRAINT PK_bms_web_ExpenseRequestLines PRIMARY KEY (Id),
            CONSTRAINT FK_bms_web_ExpenseRequestLines_Request FOREIGN KEY (ExpenseRequestId)
                REFERENCES bms_web.ExpenseRequests (Id) ON DELETE CASCADE,
            CONSTRAINT CK_bms_web_ExpenseRequestLines_amount CHECK (Amount > 0)
        );
    END;

    IF OBJECT_ID(N'bms_web.BusinessAudit', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.BusinessAudit
        (
            Id              uniqueidentifier NOT NULL,
            OccurredAtUtc   datetimeoffset(3) NOT NULL,
            ActorExternalId nvarchar(256)    NOT NULL,
            ObjectType      nvarchar(100)    NOT NULL,
            ObjectId        nvarchar(100)    NOT NULL,
            Action          nvarchar(100)    NOT NULL,
            BeforeJson      nvarchar(max)    NULL,
            AfterJson       nvarchar(max)    NOT NULL,
            CorrelationId   nvarchar(64)     NOT NULL,
            CONSTRAINT PK_bms_web_BusinessAudit PRIMARY KEY (Id),
            CONSTRAINT CK_bms_web_BusinessAudit_before_json CHECK
                (BeforeJson IS NULL OR ISJSON(BeforeJson) = 1),
            CONSTRAINT CK_bms_web_BusinessAudit_after_json CHECK (ISJSON(AfterJson) = 1)
        );
    END;

    IF OBJECT_ID(N'bms_web.OutboxMessages', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.OutboxMessages
        (
            Id             uniqueidentifier NOT NULL,
            OccurredAtUtc  datetimeoffset(3) NOT NULL,
            MessageType    nvarchar(200)    NOT NULL,
            PayloadJson    nvarchar(max)    NOT NULL,
            CorrelationId  nvarchar(64)     NOT NULL,
            ProcessedAtUtc datetimeoffset(3) NULL,
            RetryCount     int              NOT NULL,
            LastError      nvarchar(4000)   NULL,
            CONSTRAINT PK_bms_web_OutboxMessages PRIMARY KEY (Id),
            CONSTRAINT CK_bms_web_OutboxMessages_payload CHECK (ISJSON(PayloadJson) = 1),
            CONSTRAINT CK_bms_web_OutboxMessages_retry CHECK (RetryCount >= 0)
        );
    END;

    IF OBJECT_ID(N'bms_web.IdempotencyRecords', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.IdempotencyRecords
        (
            Scope              nvarchar(350) NOT NULL,
            [Key]              nvarchar(128) NOT NULL,
            RequestHash        char(64)      NOT NULL,
            ResponseStatusCode int           NOT NULL,
            ResponseJson       nvarchar(max) NOT NULL,
            CreatedAtUtc       datetimeoffset(3) NOT NULL,
            CONSTRAINT PK_bms_web_IdempotencyRecords PRIMARY KEY (Scope, [Key]),
            CONSTRAINT CK_bms_web_IdempotencyRecords_hash CHECK
                (RequestHash NOT LIKE '%[^0-9A-F]%' AND LEN(RequestHash) = 64),
            CONSTRAINT CK_bms_web_IdempotencyRecords_response CHECK (ISJSON(ResponseJson) = 1)
        );
    END;

    /* Refuse silent success against an earlier or manually drifted draft schema. */
    IF COL_LENGTH(N'bms_web.ExpenseRequests', N'BusinessJustification') IS NULL
       OR COL_LENGTH(N'bms_web.ExpenseRequests', N'IsUrgent') IS NULL
       OR COL_LENGTH(N'bms_web.ExpenseRequests', N'UrgencyReason') IS NULL
        THROW 51004, N'BMS bootstrap refused: ExpenseRequests schema is older than the reviewed version.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns AS c
        WHERE c.object_id = OBJECT_ID(N'bms_web.IdempotencyRecords')
          AND c.name = N'RequestHash'
          AND (TYPE_NAME(c.user_type_id) <> N'char' OR c.max_length <> 64)
    )
        THROW 51005, N'BMS bootstrap refused: IdempotencyRecords.RequestHash type does not match the EF contract.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.ExpenseRequests', N'CreatedAtUtc'),
                (N'bms_web.ExpenseRequests', N'UpdatedAtUtc'),
                (N'bms_web.BusinessAudit', N'OccurredAtUtc'),
                (N'bms_web.OutboxMessages', N'OccurredAtUtc'),
                (N'bms_web.OutboxMessages', N'ProcessedAtUtc'),
                (N'bms_web.IdempotencyRecords', N'CreatedAtUtc')
        ) AS expected(ObjectName, ColumnName)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> N'datetimeoffset'
           OR c.scale <> 3
    )
        THROW 51006, N'BMS bootstrap refused: timestamp columns do not match datetimeoffset(3).', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'bms_web.ExpenseRequests') AND name = N'IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc')
        CREATE INDEX IX_ExpenseRequests_InitiatorExternalId_CreatedAtUtc ON bms_web.ExpenseRequests (InitiatorExternalId, CreatedAtUtc);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'bms_web.ExpenseRequests') AND name = N'IX_ExpenseRequests_Status_CreatedAtUtc')
        CREATE INDEX IX_ExpenseRequests_Status_CreatedAtUtc ON bms_web.ExpenseRequests (Status, CreatedAtUtc);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'bms_web.ExpenseRequestLines') AND name = N'IX_ExpenseRequestLines_ExpenseRequestId')
        CREATE INDEX IX_ExpenseRequestLines_ExpenseRequestId ON bms_web.ExpenseRequestLines (ExpenseRequestId);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'bms_web.BusinessAudit') AND name = N'IX_BusinessAudit_ObjectType_ObjectId_OccurredAtUtc')
        CREATE INDEX IX_BusinessAudit_ObjectType_ObjectId_OccurredAtUtc ON bms_web.BusinessAudit (ObjectType, ObjectId, OccurredAtUtc);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'bms_web.OutboxMessages') AND name = N'IX_OutboxMessages_ProcessedAtUtc_OccurredAtUtc')
        CREATE INDEX IX_OutboxMessages_ProcessedAtUtc_OccurredAtUtc ON bms_web.OutboxMessages (ProcessedAtUtc, OccurredAtUtc);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'bms_web.IdempotencyRecords') AND name = N'IX_IdempotencyRecords_CreatedAtUtc')
        CREATE INDEX IX_IdempotencyRecords_CreatedAtUtc ON bms_web.IdempotencyRecords (CreatedAtUtc);

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no = 1)
        INSERT INTO bms_web.schema_version (version_no, script_name) VALUES (1, N'001_bootstrap_bms_web.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
