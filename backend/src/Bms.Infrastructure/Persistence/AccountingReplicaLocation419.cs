#if BMS_OWNED419_ACCEPTANCE
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace Bms.Infrastructure.Persistence;

// Compiled ONLY into the disposable acceptance binary, not a publish. All
// runtime/config/HTTP catalogue overrides in the production class stay closed.
public sealed class AccountingReplicaLocation
{
    internal string Catalog { get; } = "BmsReadiness407_Test_415";
    public AccountingReplicaLocation() { }
    internal static void RequireReader419(SqlConnectionStringBuilder c)
    {
        if (Environment.MachineName != "BMS" || Environment.GetEnvironmentVariable("BMS_COMPLETION419") != "1"
            || c.DataSource != "lpc:." || c.IntegratedSecurity || c.UserID != "BmsFixture419Runner_20261025"
            || c.InitialCatalog is not ("BmsReadiness407_Test_415" or "BmsAccrualEdit311_Test_415"))
            throw new InvalidOperationException("Only the approved419 local reader and restricted SQL principal are permitted.");
        // EF may redact a password after the first open. Recover it only from
        // this runner's volatile environment, never configuration or a file.
        c.Password = Environment.GetEnvironmentVariable("BMS419_SQL_PASSWORD")
            ?? throw new InvalidOperationException("Volatile419 test credential required.");
    }
    private AccountingReplicaLocation(string catalog) { Catalog = catalog; }
    private static void Require(BmsDbContext db, string catalog)
    {
        var c = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (Environment.MachineName != "BMS" || Environment.GetEnvironmentVariable("BMS_COMPLETION419") != "1"
            || c.DataSource != "lpc:." || c.InitialCatalog != catalog || c.IntegratedSecurity
            || c.UserID != "BmsFixture419Runner_20261025")
            throw new InvalidOperationException("Exact isolated419 SQL principal and local fixture required.");
    }
    internal static AccountingReplicaLocation Payment400Owned(BmsDbContext db)
        => throw new InvalidOperationException("Outside the 74-scenario419 allowlist.");
    internal static AccountingReplicaLocation Budget343Owned(BmsDbContext db)
    { Require(db, "BmsBudgetExecution343_Test_415"); return new("BmsReadiness407_Test_415"); }
    internal AccountingReplicaLocation(BmsDbContext db, bool operationAcceptance319)
    {
        if (!operationAcceptance319) throw new InvalidOperationException("Explicit operation fixture required.");
        Require(db, "BmsAccrualEdit311_Test_415"); Catalog = "BmsAccrualEdit311_Test_415";
    }
    internal AccountingReplicaLocation(BmsDbContext db)
    { Require(db, "BmsGoLive258r2_DataBI_415"); Catalog = "BmsReadiness407_Test_415"; }
}
#endif
