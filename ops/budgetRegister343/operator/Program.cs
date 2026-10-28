using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using Bms.Domain.ExpenseRequests;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// Finite local administrative operation, never hosted as an HTTP endpoint.
// Runtime financial code is the exact tested package, not duplicated SQL.
if(!OperatingSystem.IsWindows()||Environment.MachineName!="BMS"
    ||!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)
    ||!args.SequenceEqual(new[]{"initialize"}))throw new InvalidOperationException("Exact local administrative initialization required.");

static async Task AssertStopped()
{
    var shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\powershell.exe");
    using var process=Process.Start(new ProcessStartInfo(shell,"-NoProfile -NonInteractive -Command \"(Get-Service -Name BmsWebCurrent).Status.ToString()\"")
        {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})
        ??throw new InvalidOperationException("Service state check unavailable.");
    var result=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();
    if(process.ExitCode!=0||result.Trim()!="Stopped")throw new InvalidOperationException("BMS service must be stopped for initialization.");
}
await AssertStopped();
await using var db=new BmsDbContext(new DbContextOptionsBuilder<BmsDbContext>().UseSqlServer(
    "Server=lpc:.;Database=DataBI;Integrated Security=True;Encrypt=False;Pooling=False").Options);
await db.Database.OpenConnectionAsync();
if(await db.Database.SqlQueryRaw<int>("SELECT CONVERT(int,IS_SRVROLEMEMBER('sysadmin')) AS Value").SingleAsync()!=1
    ||await db.Database.SqlQueryRaw<int>("SELECT MAX(version_no) AS Value FROM bms_web.schema_version").SingleAsync()!=61)
    throw new InvalidOperationException("Exact privileged core61 local connection required.");
var backups=await db.Database.SqlQueryRaw<int>("""
    SELECT COUNT(DISTINCT database_name) AS Value FROM msdb.dbo.backupset
    WHERE database_name IN(N'DataBI',N'BmsDdsReplica2026') AND type='D' AND is_copy_only=1 AND has_backup_checksums=1
      AND backup_finish_date>=DATEADD(minute,-15,GETDATE())
    """).SingleAsync();
if(backups!=2)throw new InvalidOperationException("Fresh protected copy-only checksummed backups required.");
using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(5));var token=cancellation.Token;
await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
var assembly=typeof(BmsDbContext).Assembly;
Type Type343(string name)=>assembly.GetType("Bms.Infrastructure.Persistence."+name,true)??throw new InvalidOperationException("Tested register runtime missing.");
void Flag(string name,bool value)=>(typeof(BmsDbContext).GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic)
    ??throw new InvalidOperationException("Tested context contract missing.")).SetValue(db,value);
async Task<object?> Invoke343(object target,string method,params object?[] arguments)
{
    var call=target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
        ??throw new InvalidOperationException("Tested register operation missing.");
    var task=call.Invoke(target,arguments) as Task??throw new InvalidOperationException("Tested operation must be async.");
    await task;return task.GetType().GetProperty("Result")?.GetValue(task);
}
var storage=Activator.CreateInstance(Type343("BudgetExecutionStorage343"),db)??throw new InvalidOperationException("Storage unavailable.");
if(await Invoke343(storage,"AcquireGateAsync",false,token) is not false)throw new InvalidOperationException("Initialization requires disabled register, never an overwrite.");
var before=await db.ExpenseRequests.AsNoTracking().ToDictionaryAsync(r=>r.Id,r=>new{r.Status,r.ConcurrencyStamp},token);
async Task<string> ProtectedFinancialDigest343()
{
    var hashes=new List<string>();
    foreach(var (table,query) in new[]{
        ("BudgetPositions","SELECT TOP(100001) CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT t.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)),2) AS Value FROM bms_web.BudgetPositions t"),
        ("BudgetRequestProjections","SELECT TOP(100001) CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT t.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)),2) AS Value FROM bms_web.BudgetRequestProjections t"),
        ("AccountingPaymentLinks","SELECT TOP(100001) CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT t.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)),2) AS Value FROM bms_web.AccountingPaymentLinks t"),
        ("AccountingPaymentLinkLines","SELECT TOP(100001) CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT t.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)),2) AS Value FROM bms_web.AccountingPaymentLinkLines t"),
        ("ExpenseRequestLines","SELECT TOP(100001) CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT t.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)),2) AS Value FROM bms_web.ExpenseRequestLines t")})
    {
        var rows=await db.Database.SqlQueryRaw<string>(query).ToArrayAsync(token);
        if(rows.Length>100000)throw new InvalidOperationException("Protected financial source bound exceeded.");
        hashes.AddRange(rows.Select(row=>table+":"+row));
    }
    hashes.Sort(StringComparer.Ordinal);
    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",hashes))));
}
var protectedBefore=await ProtectedFinancialDigest343();
try
{
    var snapshot=await new DirectDdsReplicaReader(db).ReadAsync(token)??throw new InvalidOperationException("Verified cash capture required.");snapshot.Validate();
    var cash=Activator.CreateInstance(Type343("BudgetExecutionCash343"),db,new AccountingPaymentCoverageReader(db,new AccountingPaymentSourceReader(db)),null)
        ??throw new InvalidOperationException("Cash projector unavailable.");
    Flag("BudgetRegisterEnabled343",true);Flag("BudgetRegisterReadOnly343",false);
    await Invoke343(cash,"RefreshAsync",snapshot,token);
    var posting=Activator.CreateInstance(Type343("BudgetExecutionPosting343"),db)??throw new InvalidOperationException("Posting unavailable.");
    var receipt=await Invoke343(posting,"SynchronizeAsync",true,token,0);
    var after=await db.ExpenseRequests.AsNoTracking().ToDictionaryAsync(r=>r.Id,r=>new{r.Status,r.ConcurrencyStamp},token);
    foreach(var pair in before.Where(p=>p.Value.Status is ExpenseRequestStatus.ToPay or ExpenseRequestStatus.PartiallyPaid or ExpenseRequestStatus.Paid or ExpenseRequestStatus.Closed))
        if(!after.TryGetValue(pair.Key,out var saved)||!Equals(saved,pair.Value))throw new InvalidOperationException("Initialization cannot recall payment handoffs.");
    var drift=await db.Database.SqlQueryRaw<int>("""
        SELECT COUNT(*) AS Value FROM bms_execution343.Balances b WHERE
          b.LimitAmountKzt<>(SELECT COALESCE(SUM(AmountKzt),0) FROM bms_execution343.SourceValues s WHERE s.Kind='Limit' AND s.CostCenterId=b.CostCenterId AND s.CashFlowItemId=b.CashFlowItemId AND s.BudgetMonth=b.BudgetMonth)
          OR b.ReservedAmountKzt<>(SELECT COALESCE(SUM(AmountKzt),0) FROM bms_execution343.SourceValues s WHERE s.Kind='Reserve' AND s.CostCenterId=b.CostCenterId AND s.CashFlowItemId=b.CashFlowItemId AND s.BudgetMonth=b.BudgetMonth)
          OR b.PaidAmountKzt<>(SELECT COALESCE(SUM(AmountKzt),0) FROM bms_execution343.SourceValues s WHERE s.Kind='Paid' AND s.CostCenterId=b.CostCenterId AND s.CashFlowItemId=b.CashFlowItemId AND s.BudgetMonth=b.BudgetMonth)
        """).SingleAsync(token);
    if(drift!=0)throw new InvalidOperationException("Register source-to-movement reconciliation failed.");
    if(protectedBefore!=await ProtectedFinancialDigest343())throw new InvalidOperationException("Initialization changed protected financial source rows.");
    var verify=await Invoke343(posting,"SynchronizeAsync",false,token,0);
    if(verify is not ValueTuple<int,int> repeated||repeated!=(0,0))throw new InvalidOperationException("Register repost must be idempotent.");
    await AssertStopped();
    await db.Database.ExecuteSqlRawAsync("UPDATE bms_execution343.Component SET Enabled=1 WHERE Id=1",token);
    await tx.CommitAsync(token);
    Console.WriteLine(JsonSerializer.Serialize(new{status="INITIALIZED343",snapshotId=snapshot.Id,grossSha256=snapshot.GrossHash,
        receipt,changedRequests=after.Count(p=>!Equals(p.Value,before[p.Key])),paymentHandoffsPreserved=true,
        protectedFinancialSourceSHA256=protectedBefore,protectedFinancialTables=5},new JsonSerializerOptions{IncludeFields=true}));
}
catch{await tx.RollbackAsync(CancellationToken.None);throw;}
