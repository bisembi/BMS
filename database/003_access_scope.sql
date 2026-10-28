-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/*
    BMS Web — creation-only reference and actor access-scope slice.

    IMPORTANT
    - Apply 001_bootstrap_bms_web.sql and 002_budget_projection.sql first.
    - This script creates no database, login, user, role or permission.
    - It creates no reference values or actor grants: an empty scope denies access.
    - Organization grants are current-state rows. Cost-center access is periodic;
      the application uses the latest EffectiveFrom not later than the check date.
    - It has not been executed against an accounting server.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase'))
    THROW 53001, N'BMS access scope refused: current database must be the explicitly owned BMS database.', 1;

IF DATABASEPROPERTYEX(DB_NAME(), 'Updateability') <> N'READ_WRITE'
    THROW 53002, N'BMS access scope refused: current database is not READ_WRITE.', 1;

IF SCHEMA_ID(N'bms_web') IS NULL
   OR OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL
    THROW 53003, N'BMS access scope refused: managed schema bms_web is absent.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM bms_web.schema_version
    WHERE version_no = 1
      AND script_name COLLATE Latin1_General_100_BIN2 = N'001_bootstrap_bms_web.sql'
)
    THROW 53004, N'BMS access scope refused: exact schema version 1 is not registered.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM bms_web.schema_version
    WHERE version_no = 2
      AND script_name COLLATE Latin1_General_100_BIN2 = N'002_budget_projection.sql'
)
    THROW 53005, N'BMS access scope refused: exact schema version 2 is not registered.', 1;

IF EXISTS
(
    SELECT 1
    FROM bms_web.schema_version
    WHERE version_no = 3
      AND script_name COLLATE Latin1_General_100_BIN2 <> N'003_access_scope.sql'
)
    THROW 53006, N'BMS access scope refused: schema version 3 belongs to another script.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'bms_web.Organizations', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.Organizations
        (
            Id       uniqueidentifier NOT NULL,
            Code     nvarchar(100)    NOT NULL,
            Name     nvarchar(500)    NOT NULL,
            IsActive bit              NOT NULL,
            CONSTRAINT PK_bms_web_Organizations PRIMARY KEY (Id),
            CONSTRAINT UQ_bms_web_Organizations_Code UNIQUE (Code),
            CONSTRAINT CK_bms_web_Organizations_Code CHECK
                (LEN(LTRIM(RTRIM(Code))) > 0),
            CONSTRAINT CK_bms_web_Organizations_Name CHECK
                (LEN(LTRIM(RTRIM(Name))) > 0)
        );
    END;

    IF OBJECT_ID(N'bms_web.CostCenters', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.CostCenters
        (
            Id       uniqueidentifier NOT NULL,
            Code     nvarchar(100)    NOT NULL,
            Name     nvarchar(500)    NOT NULL,
            ParentId uniqueidentifier NULL,
            IsGroup  bit              NOT NULL,
            IsActive bit              NOT NULL,
            CONSTRAINT PK_bms_web_CostCenters PRIMARY KEY (Id),
            CONSTRAINT UQ_bms_web_CostCenters_Code UNIQUE (Code),
            CONSTRAINT FK_bms_web_CostCenters_Parent FOREIGN KEY (ParentId)
                REFERENCES bms_web.CostCenters (Id) ON DELETE NO ACTION,
            CONSTRAINT CK_bms_web_CostCenters_Code CHECK
                (LEN(LTRIM(RTRIM(Code))) > 0),
            CONSTRAINT CK_bms_web_CostCenters_Name CHECK
                (LEN(LTRIM(RTRIM(Name))) > 0),
            CONSTRAINT CK_bms_web_CostCenters_Parent CHECK
                (ParentId IS NULL OR ParentId <> Id)
        );
    END;

    IF OBJECT_ID(N'bms_web.ActorOrganizationGrants', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.ActorOrganizationGrants
        (
            ActorExternalId nvarchar(256)    NOT NULL,
            OrganizationId  uniqueidentifier NOT NULL,
            IsActive        bit              NOT NULL,
            CONSTRAINT PK_bms_web_ActorOrganizationGrants PRIMARY KEY
                (ActorExternalId, OrganizationId),
            CONSTRAINT FK_bms_web_ActorOrganizationGrants_Organization FOREIGN KEY
                (OrganizationId) REFERENCES bms_web.Organizations (Id)
                ON DELETE NO ACTION,
            CONSTRAINT CK_bms_web_ActorOrganizationGrants_Actor CHECK
                (LEN(LTRIM(RTRIM(ActorExternalId))) > 0)
        );
    END;

    IF OBJECT_ID(N'bms_web.ActorCostCenterAccess', N'U') IS NULL
    BEGIN
        CREATE TABLE bms_web.ActorCostCenterAccess
        (
            ActorExternalId nvarchar(256)    NOT NULL,
            CostCenterId    uniqueidentifier NOT NULL,
            EffectiveFrom   date             NOT NULL,
            IsAllowed       bit              NOT NULL,
            CONSTRAINT PK_bms_web_ActorCostCenterAccess PRIMARY KEY
                (ActorExternalId, CostCenterId, EffectiveFrom),
            CONSTRAINT FK_bms_web_ActorCostCenterAccess_CostCenter FOREIGN KEY
                (CostCenterId) REFERENCES bms_web.CostCenters (Id)
                ON DELETE NO ACTION,
            CONSTRAINT CK_bms_web_ActorCostCenterAccess_Actor CHECK
                (LEN(LTRIM(RTRIM(ActorExternalId))) > 0)
        );
    END;

    /* Refuse silent success against manually drifted reference/access tables. */
    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.Organizations', N'Id', N'uniqueidentifier', 0, 0, 16),
                (N'bms_web.Organizations', N'Code', N'nvarchar', 0, 0, 200),
                (N'bms_web.Organizations', N'Name', N'nvarchar', 0, 0, 1000),
                (N'bms_web.Organizations', N'IsActive', N'bit', 0, 0, 1),
                (N'bms_web.CostCenters', N'Id', N'uniqueidentifier', 0, 0, 16),
                (N'bms_web.CostCenters', N'Code', N'nvarchar', 0, 0, 200),
                (N'bms_web.CostCenters', N'Name', N'nvarchar', 0, 0, 1000),
                (N'bms_web.CostCenters', N'ParentId', N'uniqueidentifier', 0, 0, 16),
                (N'bms_web.CostCenters', N'IsGroup', N'bit', 0, 0, 1),
                (N'bms_web.CostCenters', N'IsActive', N'bit', 0, 0, 1),
                (N'bms_web.ActorOrganizationGrants', N'ActorExternalId', N'nvarchar', 0, 0, 512),
                (N'bms_web.ActorOrganizationGrants', N'OrganizationId', N'uniqueidentifier', 0, 0, 16),
                (N'bms_web.ActorOrganizationGrants', N'IsActive', N'bit', 0, 0, 1),
                (N'bms_web.ActorCostCenterAccess', N'ActorExternalId', N'nvarchar', 0, 0, 512),
                (N'bms_web.ActorCostCenterAccess', N'CostCenterId', N'uniqueidentifier', 0, 0, 16),
                (N'bms_web.ActorCostCenterAccess', N'EffectiveFrom', N'date', 0, 0, 3),
                (N'bms_web.ActorCostCenterAccess', N'IsAllowed', N'bit', 0, 0, 1)
        ) AS expected(ObjectName, ColumnName, TypeName, [Precision], Scale, MaxLength)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL
           OR TYPE_NAME(c.user_type_id) <> expected.TypeName
           OR c.max_length <> expected.MaxLength
    )
        THROW 53007, N'BMS access scope refused: W-04 column contract is missing or drifted.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM
        (
            VALUES
                (N'bms_web.Organizations', N'Id', 0),
                (N'bms_web.Organizations', N'Code', 0),
                (N'bms_web.Organizations', N'Name', 0),
                (N'bms_web.Organizations', N'IsActive', 0),
                (N'bms_web.CostCenters', N'Id', 0),
                (N'bms_web.CostCenters', N'Code', 0),
                (N'bms_web.CostCenters', N'Name', 0),
                (N'bms_web.CostCenters', N'ParentId', 1),
                (N'bms_web.CostCenters', N'IsGroup', 0),
                (N'bms_web.CostCenters', N'IsActive', 0),
                (N'bms_web.ActorOrganizationGrants', N'ActorExternalId', 0),
                (N'bms_web.ActorOrganizationGrants', N'OrganizationId', 0),
                (N'bms_web.ActorOrganizationGrants', N'IsActive', 0),
                (N'bms_web.ActorCostCenterAccess', N'ActorExternalId', 0),
                (N'bms_web.ActorCostCenterAccess', N'CostCenterId', 0),
                (N'bms_web.ActorCostCenterAccess', N'EffectiveFrom', 0),
                (N'bms_web.ActorCostCenterAccess', N'IsAllowed', 0)
        ) AS expected(ObjectName, ColumnName, IsNullable)
        LEFT JOIN sys.columns AS c
          ON c.object_id = OBJECT_ID(expected.ObjectName)
         AND c.name = expected.ColumnName
        WHERE c.object_id IS NULL OR c.is_nullable <> expected.IsNullable
    )
        THROW 53008, N'BMS access scope refused: W-04 nullability contract is drifted.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.Organizations')
          AND [type] = N'PK' AND name = N'PK_bms_web_Organizations'
    )
       OR NOT EXISTS
    (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.CostCenters')
          AND [type] = N'PK' AND name = N'PK_bms_web_CostCenters'
    )
       OR NOT EXISTS
    (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ActorOrganizationGrants')
          AND [type] = N'PK' AND name = N'PK_bms_web_ActorOrganizationGrants'
    )
       OR NOT EXISTS
    (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'bms_web.ActorCostCenterAccess')
          AND [type] = N'PK' AND name = N'PK_bms_web_ActorCostCenterAccess'
    )
        THROW 53009, N'BMS access scope refused: W-04 primary keys are missing.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM sys.foreign_keys
        WHERE name IN
        (
            N'FK_bms_web_CostCenters_Parent',
            N'FK_bms_web_ActorOrganizationGrants_Organization',
            N'FK_bms_web_ActorCostCenterAccess_CostCenter'
        )
          AND delete_referential_action = 0
    ) <> 3
        THROW 53010, N'BMS access scope refused: W-04 NO ACTION foreign keys are missing.', 1;

    IF NOT EXISTS (SELECT 1 FROM bms_web.schema_version WHERE version_no = 3)
        INSERT INTO bms_web.schema_version (version_no, script_name)
            VALUES (3, N'003_access_scope.sql');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
