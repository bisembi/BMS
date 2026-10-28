using System.Text.RegularExpressions;

namespace Bms.Infrastructure.Health;

/// <summary>
/// Bounded column metadata is captured per physical inspection, then read again
/// before the positive row is emitted. Every reviewed predicate remains intact.
/// Neither metadata nor a verdict survives a call; SQL identity never changes.
/// </summary>
internal static class ReadinessCatalogSnapshot407
{
    internal const string PositiveResultMarker = "    SELECT\n        CAST(MAX(CASE WHEN readiness.Ordinal = 0";
    private static readonly Regex ColumnsReference = new(
        @"(?<![.\w])sys\.columns\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal const string Prelude = """
        SET NOCOUNT ON;
        IF OBJECT_ID(N'tempdb..#Bms407ColumnScope') IS NOT NULL OR OBJECT_ID(N'tempdb..#Bms407Columns') IS NOT NULL OR OBJECT_ID(N'tempdb..#Bms407ColumnsFresh') IS NOT NULL OR OBJECT_ID(N'tempdb..#Bms407ColumnScopeFresh') IS NOT NULL
         THROW 64407,N'Readiness private metadata collision.',1;
        SELECT TOP(1025) ObjectId INTO #Bms407ColumnScope FROM (
        SELECT object_id AS ObjectId FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web')
        UNION SELECT type_table_object_id FROM sys.table_types WHERE schema_id=SCHEMA_ID(N'bms_web')
        UNION SELECT parent_object_id FROM sys.foreign_keys WHERE referenced_object_id IN(SELECT object_id FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web'))
        UNION SELECT referenced_object_id FROM sys.foreign_keys WHERE parent_object_id IN(SELECT object_id FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web'))
        ) AS scope;
        IF (SELECT COUNT(*) FROM #Bms407ColumnScope)>1024 THROW 64407,N'Readiness metadata scope limit exceeded.',1;
        SELECT TOP(0) * INTO #Bms407Columns FROM sys.columns;
        DECLARE @Bms407ColumnObjectId int, @Bms407Remaining int;
        DECLARE Bms407ColumnObjects CURSOR LOCAL FAST_FORWARD FOR SELECT ObjectId FROM #Bms407ColumnScope;
        OPEN Bms407ColumnObjects;
        FETCH NEXT FROM Bms407ColumnObjects INTO @Bms407ColumnObjectId;
        WHILE @@FETCH_STATUS=0
        BEGIN
         SET @Bms407Remaining=20001-(SELECT COUNT(*) FROM #Bms407Columns);
         INSERT #Bms407Columns SELECT TOP(@Bms407Remaining) * FROM sys.columns WHERE object_id=@Bms407ColumnObjectId OPTION(FORCE ORDER,LOOP JOIN);
         IF (SELECT COUNT(*) FROM #Bms407Columns)>20000 THROW 64407,N'Readiness metadata column limit exceeded.',1;
         FETCH NEXT FROM Bms407ColumnObjects INTO @Bms407ColumnObjectId;
        END;
        CLOSE Bms407ColumnObjects;
        DEALLOCATE Bms407ColumnObjects;
        CREATE UNIQUE CLUSTERED INDEX IX_Bms407_Columns ON #Bms407Columns(object_id,column_id);
        """;
    internal const string Revalidation = """
        SELECT TOP(1025) ObjectId INTO #Bms407ColumnScopeFresh FROM (
        SELECT object_id AS ObjectId FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web')
        UNION SELECT type_table_object_id FROM sys.table_types WHERE schema_id=SCHEMA_ID(N'bms_web')
        UNION SELECT parent_object_id FROM sys.foreign_keys WHERE referenced_object_id IN(SELECT object_id FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web'))
        UNION SELECT referenced_object_id FROM sys.foreign_keys WHERE parent_object_id IN(SELECT object_id FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web'))
        ) AS scope;
        IF (SELECT COUNT(*) FROM #Bms407ColumnScopeFresh)>1024 OR EXISTS(SELECT ObjectId FROM #Bms407ColumnScope EXCEPT SELECT ObjectId FROM #Bms407ColumnScopeFresh) OR EXISTS(SELECT ObjectId FROM #Bms407ColumnScopeFresh EXCEPT SELECT ObjectId FROM #Bms407ColumnScope)
         THROW 64407,N'Readiness metadata visibility changed during inspection.',1;
        SELECT TOP(0) * INTO #Bms407ColumnsFresh FROM sys.columns;
        DECLARE @Bms407FreshObjectId int, @Bms407FreshRemaining int;
        DECLARE Bms407FreshObjects CURSOR LOCAL FAST_FORWARD FOR SELECT ObjectId FROM #Bms407ColumnScopeFresh;
        OPEN Bms407FreshObjects;
        FETCH NEXT FROM Bms407FreshObjects INTO @Bms407FreshObjectId;
        WHILE @@FETCH_STATUS=0
        BEGIN
         SET @Bms407FreshRemaining=20001-(SELECT COUNT(*) FROM #Bms407ColumnsFresh);
         INSERT #Bms407ColumnsFresh SELECT TOP(@Bms407FreshRemaining) * FROM sys.columns WHERE object_id=@Bms407FreshObjectId OPTION(FORCE ORDER,LOOP JOIN);
         IF (SELECT COUNT(*) FROM #Bms407ColumnsFresh)>20000 THROW 64407,N'Readiness metadata column limit exceeded.',1;
         FETCH NEXT FROM Bms407FreshObjects INTO @Bms407FreshObjectId;
        END;
        CLOSE Bms407FreshObjects;
        DEALLOCATE Bms407FreshObjects;
        IF EXISTS(SELECT * FROM #Bms407Columns EXCEPT SELECT * FROM #Bms407ColumnsFresh) OR EXISTS(SELECT * FROM #Bms407ColumnsFresh EXCEPT SELECT * FROM #Bms407Columns)
         THROW 64407,N'Readiness column metadata changed during inspection.',1;
        """;

    // Cache immutable command text only, not SQL metadata or a health verdict.
    internal static string Text { get; } = Build(SqlServerDatabaseReadinessCommand.Text);

    internal static string Build(string canonical)
    {
        if (canonical.Contains("#Bms407", StringComparison.Ordinal)
            || canonical.Contains("@Bms407", StringComparison.Ordinal)
            || canonical.Contains("Bms407ColumnObjects", StringComparison.Ordinal)
            || canonical.Contains("Bms407FreshObjects", StringComparison.Ordinal))
            throw new InvalidOperationException("Readiness private variable name collision.");

        var body = ColumnsReference.Replace(canonical, "#Bms407Columns");
        var marker = body.IndexOf(PositiveResultMarker, StringComparison.Ordinal);
        if (marker < 0 || marker != body.LastIndexOf(PositiveResultMarker, StringComparison.Ordinal))
            throw new InvalidOperationException("Readiness canonical positive-result boundary changed.");
        return Prelude + "\n" + body.Insert(marker, Revalidation + "\n");
    }
}
