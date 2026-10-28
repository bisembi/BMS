using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bms.Application.Abstractions;
using Bms.Application.Budgeting;
using Bms.Application.ExpenseRequests;
using Bms.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

/// <summary>Only EXEC on finite same-server replica readers. No source-system
/// connection, password, user-selected SQL/catalogue or public mutation route.</summary>
public sealed class AccountingPaymentSourceReader(BmsDbContext db, AccountingReplicaLocation? location=null) : IAccountingPaymentSourceReader
{
    private static readonly Regex Key=new("^(ExampleAccountingA|ExampleAccountingB|ExampleAccountingC)\\|[0-9A-F]{8}\\|[0-9A-F]{32}$",RegexOptions.CultureInvariant);
    private SqlConnection Connection()
    {
        var b=new SqlConnectionStringBuilder(db.Database.GetConnectionString())
        { InitialCatalog=(location??new()).Catalog,ApplicationName="BMS-AccountingPayments258-Replica-ReadOnly",Pooling=false,ConnectTimeout=15 };
#if BMS_OWNED419_ACCEPTANCE
        AccountingReplicaLocation.RequireReader419(b);
#else
        if(!b.IntegratedSecurity||b.UserID.Length!=0||b.Password.Length!=0)
            throw new ExpenseRequestPaymentSummaryUnavailableException();
#endif
        return new(b.ConnectionString);
    }

    public async Task<AccountingPaymentSourceSnapshot> ReadAsync(CancellationToken token)
    {
        await using var connection=Connection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandType=CommandType.StoredProcedure;
        command.CommandText="dbo.ReadAccountingPaymentCapture";command.CommandTimeout=60;
        await using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token)||reader.GetGuid(0)!=Guid.Parse("6FC44B33-6166-4D35-85F1-622190601EB6")
            ||reader.GetInt32(1)!=1||!reader.GetBoolean(7)) throw new ExpenseRequestPaymentSummaryUnavailableException();
        var id=reader.GetGuid(2);var captured=reader.GetFieldValue<DateTimeOffset>(3);
        var cutoff=DateOnly.FromDateTime(reader.GetDateTime(4));var docCount=reader.GetInt32(5);var legCount=reader.GetInt32(6);
        if(id==Guid.Empty||captured==default||captured.Offset!=TimeSpan.Zero||captured>DateTimeOffset.UtcNow.AddMinutes(5)
            ||cutoff!=DateOnly.FromDateTime(captured.ToOffset(TimeSpan.FromHours(5)).Date.AddDays(1))
            ||docCount is <1 or >100000||legCount is <1 or >200000||await reader.ReadAsync(token))
            throw new ExpenseRequestPaymentSummaryUnavailableException();
        if(!await reader.NextResultAsync(token)) throw new ExpenseRequestPaymentSummaryUnavailableException();
        var documents=new List<AccountingSourcePayment>(docCount);
        while(await reader.ReadAsync(token))
        {
            var d=new AccountingSourcePayment(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
                reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),DateOnly.FromDateTime(reader.GetDateTime(8)),
                reader.GetString(9),reader.GetDecimal(10),reader.GetDecimal(11),reader.GetBoolean(12),reader.GetInt32(13));
            if(!Key.IsMatch(d.DocumentKey)||d.FinancialHash.Length!=64||d.FinancialHash.Any(c=>c is not(>= 'A' and <= 'F') and not(>= '0' and <= '9'))
                ||!AccountingPeriod467.IsSourceYear(d.PaymentDay.Year)||d.PaymentDay>=cutoff||d.LegCount is <0 or >2000
                ||(d.Eligible&&(d.Amount<=0||d.AmountKzt<=0||string.IsNullOrWhiteSpace(d.CounterpartyName)
                    ||d.CurrencyNumericCode.Length!=3||d.CurrencyNumericCode.Any(c=>c is <'0' or >'9'))))
                throw new ExpenseRequestPaymentSummaryUnavailableException();
            MoneyAmountPolicy.EnsureRepresentable(d.Amount,"Source payment amount");
            MoneyAmountPolicy.EnsureRepresentable(d.AmountKzt,"Source payment KZT amount");
            documents.Add(d);if(documents.Count>docCount) throw new ExpenseRequestPaymentSummaryUnavailableException();
        }
        if(!await reader.NextResultAsync(token)) throw new ExpenseRequestPaymentSummaryUnavailableException();
        var legs=new List<AccountingSourceCashLeg>(legCount);
        while(await reader.ReadAsync(token))
        {
            legs.Add(new(reader.GetString(0),reader.GetInt32(1),NormalizeTime(reader.GetString(2)),reader.GetDecimal(3)));
            if(legs.Count>legCount) throw new ExpenseRequestPaymentSummaryUnavailableException();
        }
        if(documents.Count!=docCount||legs.Count!=legCount||documents.Select(d=>d.DocumentKey).Distinct(StringComparer.Ordinal).Count()!=docCount
            ||legs.GroupBy(l=>(l.DocumentKey,l.LineNo)).Any(g=>g.Count()!=1)) throw new ExpenseRequestPaymentSummaryUnavailableException();
        var byKey=documents.ToDictionary(d=>d.DocumentKey,StringComparer.Ordinal);
        if(legs.Any(l=>!byKey.ContainsKey(l.DocumentKey)||l.LineNo<1
            ||!DateTime.TryParse(l.EntryDateLocal,System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,out var date)||!AccountingPeriod467.IsSourceYear(date.Year)||DateOnly.FromDateTime(date)>=cutoff))
            throw new ExpenseRequestPaymentSummaryUnavailableException();
        var legsByKey=legs.GroupBy(l=>l.DocumentKey).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        foreach(var d in documents)
        {
            var rows=legsByKey.GetValueOrDefault(d.DocumentKey)??[];
            if(rows.Length!=d.LegCount||d.Eligible&&(rows.Sum(l=>l.AmountKzt)!=d.AmountKzt||rows.Any(l=>l.AmountKzt<=0
                ||!l.EntryDateLocal.StartsWith(d.PaymentDay.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),StringComparison.Ordinal))))
                throw new ExpenseRequestPaymentSummaryUnavailableException();
        }
        await reader.DisposeAsync();
        var referenced = await AccountingOperationReferenceReader.ReadAsync(connection, id, documents, token);
        foreach (var d in referenced.Where(d => d.OperationReferenceEligible))
        {
            var rows = legsByKey.GetValueOrDefault(d.DocumentKey) ?? [];
            if (rows.Length != d.LegCount || rows.Sum(l => l.AmountKzt) != d.AmountKzt
                || rows.Any(l => l.AmountKzt <= 0 || !l.EntryDateLocal.StartsWith(d.PaymentDay.ToString("yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)))
                throw new ExpenseRequestPaymentSummaryUnavailableException();
        }
        var payroll=await PayrollCashEvidence455.ReadAsync(connection,id,referenced,legs,token);
        return new(id,captured,cutoff,payroll.Documents,legs,payroll.SnapshotId);
    }

    public async Task<IReadOnlyList<AccountingSourceCashLeg>> ReadRepresentedLegsAsync(Guid ddsSnapshotId,
        IReadOnlyCollection<string> documentKeys,CancellationToken token)
    {
        if(ddsSnapshotId==Guid.Empty||documentKeys.Count>100000||documentKeys.Any(k=>!Key.IsMatch(k)))
            throw new ExpenseRequestPaymentSummaryUnavailableException();
        if(documentKeys.Count==0) return [];
        await using var connection=Connection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandType=CommandType.StoredProcedure;
        command.CommandText="dbo.ReadAccountingPaymentCoverage455";command.CommandTimeout=60;
        command.Parameters.Add("@SnapshotId",SqlDbType.UniqueIdentifier).Value=ddsSnapshotId;
        command.Parameters.Add("@DocumentKeysJson",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(documentKeys.Distinct(StringComparer.Ordinal));
        await using var reader=await command.ExecuteReaderAsync(token);
        var rows=new List<AccountingSourceCashLeg>();var keys=documentKeys.ToHashSet(StringComparer.Ordinal);
        while(await reader.ReadAsync(token))
        {
            var row=new AccountingSourceCashLeg(reader.GetString(0),reader.GetInt32(1),NormalizeTime(reader.GetString(2)),reader.GetDecimal(3));
            if(!keys.Contains(row.DocumentKey)||row.LineNo<1||row.AmountKzt<=0||rows.Count>=200000)
                throw new ExpenseRequestPaymentSummaryUnavailableException();
            rows.Add(row);
        }
        if(rows.GroupBy(r=>(r.DocumentKey,r.LineNo)).Any(g=>g.Count()!=1)) throw new ExpenseRequestPaymentSummaryUnavailableException();
        return rows;
    }

    internal static string NormalizeTime(string value)
    {
        if(!DateTime.TryParseExact(value,["yyyy-MM-dd'T'HH:mm:ss","yyyy-MM-dd'T'HH:mm:ss.fff"],
            System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var time)
            ||time.Kind!=DateTimeKind.Unspecified||!AccountingPeriod467.IsSourceYear(time.Year))
            throw new ExpenseRequestPaymentSummaryUnavailableException();
        return time.ToString("yyyy-MM-dd'T'HH:mm:ss.fff",System.Globalization.CultureInfo.InvariantCulture);
    }
}
