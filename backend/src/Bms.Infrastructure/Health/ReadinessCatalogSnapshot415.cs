using System.Text.RegularExpressions;

namespace Bms.Infrastructure.Health;

/// <summary>Set-based candidate. Keeps every canonical predicate, bounded
/// metadata scope and a fresh second inspection; no verdict or metadata cache.</summary>
internal static class ReadinessCatalogSnapshot415
{
    private static readonly Regex ColumnsReference=new(@"(?<![.\w])sys\.columns\b",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
    internal const string Prelude="""
        SET NOCOUNT ON;
        IF OBJECT_ID(N'tempdb..#Bms415ColumnScope') IS NOT NULL OR OBJECT_ID(N'tempdb..#Bms415Columns') IS NOT NULL OR OBJECT_ID(N'tempdb..#Bms415ColumnsFresh') IS NOT NULL OR OBJECT_ID(N'tempdb..#Bms415ColumnScopeFresh') IS NOT NULL
         THROW 64415,N'Readiness private metadata collision.',1;
        SELECT TOP(1025) ObjectId INTO #Bms415ColumnScope FROM (
        SELECT object_id AS ObjectId FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web')
        UNION SELECT type_table_object_id FROM sys.table_types WHERE schema_id=SCHEMA_ID(N'bms_web')
        UNION SELECT parent_object_id FROM sys.foreign_keys WHERE referenced_object_id IN(SELECT object_id FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web'))
        UNION SELECT referenced_object_id FROM sys.foreign_keys WHERE parent_object_id IN(SELECT object_id FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web'))
        ) AS scope;
        IF (SELECT COUNT(*) FROM #Bms415ColumnScope)>1024 THROW 64415,N'Readiness metadata scope limit exceeded.',1;
        SELECT TOP(20001) c.* INTO #Bms415Columns FROM #Bms415ColumnScope s
         INNER LOOP JOIN sys.columns c ON c.object_id=s.ObjectId OPTION(FORCE ORDER,LOOP JOIN);
        IF (SELECT COUNT(*) FROM #Bms415Columns)>20000 THROW 64415,N'Readiness metadata column limit exceeded.',1;
        CREATE UNIQUE CLUSTERED INDEX IX_Bms415_Columns ON #Bms415Columns(object_id,column_id);
        """;
    internal const string Revalidation="""
        SELECT TOP(1025) ObjectId INTO #Bms415ColumnScopeFresh FROM (
        SELECT object_id AS ObjectId FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web')
        UNION SELECT type_table_object_id FROM sys.table_types WHERE schema_id=SCHEMA_ID(N'bms_web')
        UNION SELECT parent_object_id FROM sys.foreign_keys WHERE referenced_object_id IN(SELECT object_id FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web'))
        UNION SELECT referenced_object_id FROM sys.foreign_keys WHERE parent_object_id IN(SELECT object_id FROM sys.objects WHERE schema_id=SCHEMA_ID(N'bms_web'))
        ) AS scope;
        IF (SELECT COUNT(*) FROM #Bms415ColumnScopeFresh)>1024 OR EXISTS(SELECT ObjectId FROM #Bms415ColumnScope EXCEPT SELECT ObjectId FROM #Bms415ColumnScopeFresh) OR EXISTS(SELECT ObjectId FROM #Bms415ColumnScopeFresh EXCEPT SELECT ObjectId FROM #Bms415ColumnScope)
         THROW 64415,N'Readiness metadata visibility changed during inspection.',1;
        SELECT TOP(20001) c.* INTO #Bms415ColumnsFresh FROM #Bms415ColumnScopeFresh s
         INNER LOOP JOIN sys.columns c ON c.object_id=s.ObjectId OPTION(FORCE ORDER,LOOP JOIN);
        IF (SELECT COUNT(*) FROM #Bms415ColumnsFresh)>20000 THROW 64415,N'Readiness metadata column limit exceeded.',1;
        IF EXISTS(SELECT * FROM #Bms415Columns EXCEPT SELECT * FROM #Bms415ColumnsFresh) OR EXISTS(SELECT * FROM #Bms415ColumnsFresh EXCEPT SELECT * FROM #Bms415Columns)
         THROW 64415,N'Readiness column metadata changed during inspection.',1;
        """;
    internal static string Text {get;}=Build(SqlServerDatabaseReadinessCommand.Text);
    internal static string Build(string canonical)
    {
        if(canonical.Contains("#Bms415",StringComparison.Ordinal))throw new InvalidOperationException("Readiness private name collision.");
        var body=ColumnsReference.Replace(canonical,"#Bms415Columns");
        var marker=body.IndexOf(ReadinessCatalogSnapshot407.PositiveResultMarker,StringComparison.Ordinal);
        if(marker<0||marker!=body.LastIndexOf(ReadinessCatalogSnapshot407.PositiveResultMarker,StringComparison.Ordinal))
            throw new InvalidOperationException("Readiness canonical positive boundary changed.");
        return Prelude+"\n"+body.Insert(marker,Revalidation+"\n");
    }
}
