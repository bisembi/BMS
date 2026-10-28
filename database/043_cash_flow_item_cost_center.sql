-- Publication install guard. The installer sets the expected name and unique
-- ownership marker on the same connection. Never run against an existing DB.
IF SESSION_CONTEXT(N'BMS.TargetDatabase') IS NULL
 OR SESSION_CONTEXT(N'BMS.InstallationId') IS NULL
 OR DB_NAME() COLLATE Latin1_General_100_BIN2 <> CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.TargetDatabase')) COLLATE Latin1_General_100_BIN2
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'BMS.PublicInstallationId'
   AND CONVERT(nvarchar(128),value)=CONVERT(nvarchar(128),SESSION_CONTEXT(N'BMS.InstallationId')))
 THROW 51000,N'BMS installation ownership and target database are required.',1;
/* One DDS article belongs to at most one CFO. No inferred assignments or budget rewrites. */
IF @@TRANCOUNT <> 0 THROW 64300, N'Run SQL043 outside an ambient transaction.', 1;
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
IF DB_NAME() <> CONVERT(sysname,SESSION_CONTEXT(N'BMS.TargetDatabase')) THROW 64301, N'SQL043 requires the explicitly owned BMS database.', 1;
IF OBJECT_ID(N'bms_web.schema_version', N'U') IS NULL THROW 64302, N'Missing schema registry.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS(SELECT 1 FROM bms_web.schema_version WITH(UPDLOCK,HOLDLOCK) WHERE version_no>43)
        THROW 64303, N'Future schema present.', 1;
    IF (SELECT COUNT(*) FROM bms_web.schema_version WHERE version_no BETWEEN 1 AND 42)<>42
        THROW 64304, N'SQL001-SQL042 predecessors required.', 1;
    IF COL_LENGTH(N'bms_web.CashFlowItems', N'CostCenterId') IS NULL
        ALTER TABLE bms_web.CashFlowItems ADD CostCenterId uniqueidentifier NULL;
    IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems')
        AND name=N'CostCenterId' AND system_type_id=36 AND is_nullable=1)
        THROW 64305, N'Article CFO column identity mismatch.', 1;
    IF OBJECT_ID(N'bms_web.FK_bms_web_CashFlowItems_CostCenter',N'F') IS NULL
        EXEC(N'ALTER TABLE bms_web.CashFlowItems WITH CHECK ADD CONSTRAINT FK_bms_web_CashFlowItems_CostCenter FOREIGN KEY(CostCenterId) REFERENCES bms_web.CostCenters(Id);');
    IF OBJECT_ID(N'bms_web.CK_bms_web_CashFlowItems_CostCenterLeaf',N'C') IS NULL
        EXEC(N'ALTER TABLE bms_web.CashFlowItems WITH CHECK ADD CONSTRAINT CK_bms_web_CashFlowItems_CostCenterLeaf CHECK(CostCenterId IS NULL OR IsGroup=0);');
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems')
        AND name=N'IX_bms_web_CashFlowItems_CostCenterId')
        EXEC(N'CREATE INDEX IX_bms_web_CashFlowItems_CostCenterId ON bms_web.CashFlowItems(CostCenterId);');
    EXEC(N'CREATE OR ALTER TRIGGER bms_web.TR_CashFlowItems_CostCenter ON bms_web.CashFlowItems AFTER INSERT,UPDATE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted i JOIN bms_web.CostCenters c ON c.Id=i.CostCenterId
            WHERE i.IsActive=1 AND (c.IsGroup=1 OR c.IsActive=0))
            THROW 64306,N''DDS articles require an active non-group CFO.'',1;
    END;');
    EXEC(N'CREATE OR ALTER TRIGGER bms_web.TR_CostCenters_AssignedArticles ON bms_web.CostCenters AFTER UPDATE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted c WHERE (c.IsGroup=1 OR c.IsActive=0)
            AND EXISTS(SELECT 1 FROM bms_web.CashFlowItems i WHERE i.CostCenterId=c.Id AND i.IsActive=1))
            THROW 64307,N''Reassign DDS articles before disabling or grouping their CFO.'',1;
    END;');
    IF NOT (EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'bms_web.CashFlowItems') AND name=N'CostCenterId' AND system_type_id=36 AND user_type_id=36 AND max_length=16 AND is_nullable=1 AND is_computed=0 AND is_identity=0 AND default_object_id=0)
        AND EXISTS(SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c ON c.constraint_object_id=f.object_id WHERE f.object_id=OBJECT_ID(N'bms_web.FK_bms_web_CashFlowItems_CostCenter') AND f.parent_object_id=OBJECT_ID(N'bms_web.CashFlowItems') AND f.referenced_object_id=OBJECT_ID(N'bms_web.CostCenters') AND c.parent_column_id=COLUMNPROPERTY(f.parent_object_id,N'CostCenterId','ColumnId') AND c.referenced_column_id=COLUMNPROPERTY(f.referenced_object_id,N'Id','ColumnId') AND f.is_disabled=0 AND f.is_not_trusted=0 AND f.is_not_for_replication=0 AND f.delete_referential_action=0 AND f.update_referential_action=0 AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=f.object_id)=1)
        AND EXISTS(SELECT 1 FROM sys.check_constraints WHERE object_id=OBJECT_ID(N'bms_web.CK_bms_web_CashFlowItems_CostCenterLeaf') AND parent_object_id=OBJECT_ID(N'bms_web.CashFlowItems') AND is_disabled=0 AND is_not_trusted=0 AND is_not_for_replication=0 AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(definition),N' ',N''),N'(',N''),N')',N''),N'[',N''),N']',N'') COLLATE Latin1_General_100_BIN2=N'COSTCENTERIDISNULLORISGROUP=0')
        AND EXISTS(SELECT 1 FROM sys.indexes i JOIN sys.index_columns c ON c.object_id=i.object_id AND c.index_id=i.index_id WHERE i.object_id=OBJECT_ID(N'bms_web.CashFlowItems') AND i.name=N'IX_bms_web_CashFlowItems_CostCenterId' AND i.type=2 AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0 AND i.is_hypothetical=0 AND c.key_ordinal=1 AND c.is_descending_key=0 AND c.column_id=COLUMNPROPERTY(i.object_id,N'CostCenterId','ColumnId') AND (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=i.object_id AND index_id=i.index_id)=1)
        AND EXISTS(SELECT 1 FROM sys.triggers WHERE object_id=OBJECT_ID(N'bms_web.TR_CashFlowItems_CostCenter') AND parent_id=OBJECT_ID(N'bms_web.CashFlowItems') AND parent_class=1 AND type=N'TR' AND is_disabled=0 AND is_instead_of_trigger=0 AND is_not_for_replication=0 AND (SELECT COUNT(*) FROM sys.trigger_events WHERE object_id=OBJECT_ID(N'bms_web.TR_CashFlowItems_CostCenter'))=2) AND HASHBYTES('SHA2_256',REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(OBJECT_DEFINITION(OBJECT_ID(N'bms_web.TR_CashFlowItems_CostCenter'))),CHAR(13),N''),CHAR(10),N''),CHAR(9),N''),N' ',N''),N'CREATEORALTERTRIGGER',N'CREATETRIGGER'),N'ALTERTRIGGER',N'CREATETRIGGER'))=0xF508366F33B2B06502A0783568CDD7D94D780D9E3004F60757AC513B566988B2
        AND EXISTS(SELECT 1 FROM sys.triggers WHERE object_id=OBJECT_ID(N'bms_web.TR_CostCenters_AssignedArticles') AND parent_id=OBJECT_ID(N'bms_web.CostCenters') AND parent_class=1 AND type=N'TR' AND is_disabled=0 AND is_instead_of_trigger=0 AND is_not_for_replication=0 AND (SELECT COUNT(*) FROM sys.trigger_events WHERE object_id=OBJECT_ID(N'bms_web.TR_CostCenters_AssignedArticles'))=1) AND HASHBYTES('SHA2_256',REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(OBJECT_DEFINITION(OBJECT_ID(N'bms_web.TR_CostCenters_AssignedArticles'))),CHAR(13),N''),CHAR(10),N''),CHAR(9),N''),N' ',N''),N'CREATEORALTERTRIGGER',N'CREATETRIGGER'),N'ALTERTRIGGER',N'CREATETRIGGER'))=0xE8BE1CF8CAE26FCF4F28FB6F0339765228BA29166E1F5EB14EC5AA4AE5B80249)
        THROW 64309, N'Article CFO physical invariant mismatch.', 1;
    IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=43)
        INSERT bms_web.schema_version(version_no,script_name) VALUES(43,N'043_cash_flow_item_cost_center.sql');
    IF NOT EXISTS(SELECT 1 FROM bms_web.schema_version WHERE version_no=43
        AND script_name COLLATE Latin1_General_100_BIN2=N'043_cash_flow_item_cost_center.sql'
        AND DATALENGTH(script_name)=DATALENGTH(N'043_cash_flow_item_cost_center.sql'))
        THROW 64308, N'SQL043 registry identity mismatch.', 1;
    COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
