using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

#if !BMS_OWNED419_ACCEPTANCE
public sealed class AccountingReplicaLocation
{
    internal string Catalog {get;}=BmsInstallationSettings.ReplicaDatabase;
    public AccountingReplicaLocation() {}
    internal static AccountingReplicaLocation Payroll455Owned(BmsDbContext db)
    {
        var c=new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if(c.DataSource!="lpc:."||c.InitialCatalog!="BmsPayroll455_Test"||!c.IntegratedSecurity)
            throw new InvalidOperationException("Exact owned payroll455 database required.");
        return new("BmsPayroll455_Test");
    }
    internal static AccountingReplicaLocation Payment400Owned(BmsDbContext db)
    {
        var c=new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if(c.DataSource!="lpc:."||c.InitialCatalog!="BmsPaymentAllocation400_Test"||!c.IntegratedSecurity)
            throw new InvalidOperationException("Exact owned payment400 test database required.");
        return new("BmsPaymentAllocation400_Test");
    }
    private AccountingReplicaLocation(string catalog){Catalog=catalog;}
    internal static AccountingReplicaLocation Budget343Owned(BmsDbContext db)
    {
        var c=new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if(c.DataSource!="lpc:."||c.InitialCatalog!="BmsBudgetExecution343_Test"||!c.IntegratedSecurity)
            throw new InvalidOperationException("Owned budget migration database required.");
        return new("BmsBudgetExecution343_ReplicaTest");
    }
    internal AccountingReplicaLocation(BmsDbContext db, bool operationAcceptance319)
    {
        var c = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (!operationAcceptance319 || c.DataSource != "lpc:." || c.InitialCatalog != "BmsAccrualEdit311_Test" || !c.IntegratedSecurity)
            throw new InvalidOperationException("Owned operation acceptance database required.");
        Catalog = "BmsAccrualEdit311_Test";
    }
    // No configuration/HTTP catalogue override. Only a friend test assembly can
    // select the finite, locally owned restore, never another network database.
    internal AccountingReplicaLocation(BmsDbContext db)
    {
        var c=new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if(c.DataSource!=@"lpc:.\BMSLOCAL"||c.InitialCatalog!="BmsGoLive258r2_DataBI"||!c.IntegratedSecurity)
            throw new InvalidOperationException("Owned local acceptance database required.");
        Catalog="BmsGoLive258r2_Replica";
    }
}
#endif
