using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bms.Application.Budgeting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Persistence;

public interface IDirectDdsReplicaReader
{
    Task<DirectDdsSnapshot?> ReadAsync(CancellationToken cancellationToken);
}

public sealed record DirectDdsArticle(Guid CashFlowItemId, string ArticleName);
public sealed record DirectDdsCell(Guid CashFlowItemId, DateOnly BudgetMonth,
    decimal ReceiptsAmountKzt, decimal PaymentsAmountKzt, int LegCount);
internal sealed record ActualsSnapshotReadMetadata(Guid Id, int SourceYear, string SourceHash,
    string SourceGeneration, DateTimeOffset CapturedAtUtc, int RowCount, int ArticleCount,
    decimal ReceiptsAmountKzt, decimal PaymentsAmountKzt, decimal NetCashFlowAmountKzt)
{
    internal static ActualsSnapshotReadMetadata FromLegacy(CashFlowActualsSnapshot snapshot) =>
        new(snapshot.Id, snapshot.SourceYear, snapshot.SourceHash, snapshot.SourceGeneration, snapshot.CapturedAtUtc,
            snapshot.RowCount, snapshot.ArticleCount, snapshot.ReceiptsAmountKzt, snapshot.PaymentsAmountKzt, snapshot.NetCashFlowAmountKzt);
    internal static ActualsSnapshotReadMetadata FromDirect(DirectDdsSnapshot snapshot, int? year = null)
    {
        var net = snapshot.NetCommand(year);
        return new(snapshot.Id, net.AvailableMonths[0].Year, net.SourceHash, net.SourceGeneration, snapshot.CapturedAtUtc,
            net.Rows.Count, net.ArticleNames.Count, net.ReceiptsAmountKzt, net.PaymentsAmountKzt, net.NetCashFlowAmountKzt);
    }
}
internal static class CurrentDdsActualsSource
{
    internal static async Task<DirectDdsSnapshot> ReadRequiredAsync(IDirectDdsReplicaReader reader,
        CancellationToken cancellationToken)
    {
        // Materialize once per request. Failure must never select an older manual import.
        var snapshot = await reader.ReadAsync(cancellationToken)
            ?? throw new CashFlowActualsNotAvailableException();
        snapshot.Validate();
        return snapshot;
    }
}
public sealed record DirectDdsSnapshot(Guid Id, string SourceGeneration, string GrossHash,
    DateTimeOffset CapturedAtUtc, IReadOnlyList<DateOnly> Months, IReadOnlyList<DirectDdsArticle> Articles,
    IReadOnlyList<DirectDdsCell> Cells, decimal ReceiptsAmountKzt, decimal PaymentsAmountKzt,
    decimal NetCashFlowAmountKzt)
{
    public DateOnly CutoffExclusiveLocal { get; init; }
    // Compatibility is a pure net READ view, never an author or an INSERT into manual snapshots.
    public void Validate()
    {
        if (Id == Guid.Empty || SourceGeneration.Length != 64 || GrossHash.Length != 64
            || !SourceGeneration.All(IsHex) || !GrossHash.All(IsHex)
            || CapturedAtUtc == default || CapturedAtUtc.Offset != TimeSpan.Zero || CapturedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5)
            || Months.Count is < 1 or > AccountingPeriod467.MaximumMonths || Months.Distinct().Count() != Months.Count
            || Months.Any(m => !AccountingPeriod467.IsMonth(m))
            || Articles.Count is < 1 or > 2000 || Articles.Select(a => a.CashFlowItemId).Distinct().Count() != Articles.Count
            || Articles.Select(a => a.ArticleName).Distinct(StringComparer.Ordinal).Count() != Articles.Count
            || Articles.Any(a => a.CashFlowItemId == Guid.Empty || string.IsNullOrWhiteSpace(a.ArticleName)
                || a.ArticleName.Length > 500 || a.ArticleName != a.ArticleName.Trim()
                || a.ArticleName.Any(char.IsControl) || CashFlowActualsSource.IsExcludedArticle(a.ArticleName))
            || Cells.Count > AccountingPeriod467.MaximumCells || Cells.Count != Articles.Count * Months.Count
            || Cells.GroupBy(c => (c.CashFlowItemId, c.BudgetMonth)).Any(g => g.Count() != 1))
            throw new CashFlowActualsSourceInvalidException();
        var ids = Articles.Select(a => a.CashFlowItemId).ToHashSet();
        var months = Months.ToHashSet();
        if (Cells.Any(c => !ids.Contains(c.CashFlowItemId) || !months.Contains(c.BudgetMonth)
            || !Money(c.ReceiptsAmountKzt) || !Money(c.PaymentsAmountKzt) || c.LegCount < 0)
            || !Money(ReceiptsAmountKzt) || !Money(PaymentsAmountKzt)
            || Cells.Sum(c => c.ReceiptsAmountKzt) != ReceiptsAmountKzt
            || Cells.Sum(c => c.PaymentsAmountKzt) != PaymentsAmountKzt
            || ReceiptsAmountKzt - PaymentsAmountKzt != NetCashFlowAmountKzt
            || !string.Equals(CalculateGrossHash(Cells), GrossHash, StringComparison.Ordinal))
            throw new CashFlowActualsSourceInvalidException();
        foreach (var year in Months.Select(m => m.Year).Distinct())
            CashFlowActualsSource.Validate(NetCommand(year));
    }

    public static string CalculateGrossHash(IEnumerable<DirectDdsCell> cells)
    {
        var text = new StringBuilder("BMS-DirectDDS-Gross-v1\n");
        foreach (var c in cells.OrderBy(c => c.CashFlowItemId.ToString("D"), StringComparer.Ordinal).ThenBy(c => c.BudgetMonth))
            text.Append(c.CashFlowItemId.ToString("D").ToUpperInvariant()).Append('\u001f')
                .Append(c.BudgetMonth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\u001f')
                .Append(c.ReceiptsAmountKzt.ToString("0.00", CultureInfo.InvariantCulture)).Append('\u001f')
                .Append(c.PaymentsAmountKzt.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    public ImportCashFlowActualsCommand NetCommand(int? year = null)
    {
        var selectedYear = year ?? Months.Max(m => m.Year);
        var selectedMonths = Months.Where(m => m.Year == selectedYear).ToArray();
        if (selectedMonths.Length == 0) throw new CashFlowActualsPeriodUnavailableException();
        var byId = Articles.ToDictionary(a => a.CashFlowItemId, a => a.ArticleName);
        var rows = Cells.Where(c => c.BudgetMonth.Year == selectedYear)
            .OrderBy(c => byId[c.CashFlowItemId], StringComparer.Ordinal).ThenBy(c => c.BudgetMonth)
            .Select((c, n) => new CashFlowActualsSourceRow(n + 1, byId[c.CashFlowItemId], c.BudgetMonth,
                c.ReceiptsAmountKzt - c.PaymentsAmountKzt)).ToArray();
        var command = new ImportCashFlowActualsCommand(new string('0', 64), SourceGeneration, CapturedAtUtc,
            selectedMonths, Articles.Select(a => a.ArticleName).ToArray(), rows,
            rows.Where(r => r.SignedAmountKzt > 0).Sum(r => r.SignedAmountKzt),
            -rows.Where(r => r.SignedAmountKzt < 0).Sum(r => r.SignedAmountKzt), rows.Sum(r => r.SignedAmountKzt));
        return command with { SourceHash = CashFlowActualsSource.Hash(command) };
    }

    internal CashFlowActualsArticle[] NetArticles() => Articles.OrderBy(a => a.ArticleName, StringComparer.Ordinal)
        .Select((a, n) => new CashFlowActualsArticle { SnapshotId = Id, ArticleOrdinal = n + 1,
            ArticleName = a.ArticleName, CashFlowItemId = a.CashFlowItemId }).ToArray();
    internal CashFlowActualsRow[] NetRows(int? year = null)
    {
        var ordinals = NetArticles().ToDictionary(a => a.ArticleName, a => a.ArticleOrdinal, StringComparer.Ordinal);
        return NetCommand(year).Rows.Select(r => new CashFlowActualsRow { SnapshotId = Id, SourceOrdinal = r.SourceOrdinal,
            ArticleOrdinal = ordinals[r.ArticleName], BudgetMonth = r.BudgetMonth, SignedAmountKzt = r.SignedAmountKzt }).ToArray();
    }
    private static bool IsHex(char c) => c is >= 'A' and <= 'F' or >= '0' and <= '9';
    private static bool Money(decimal d) => d >= 0m && d <= CashFlowActualsSource.MaximumSafeMoney && decimal.Round(d, 2) == d;
}

public sealed class DirectDdsReplicaReader(BmsDbContext db, AccountingReplicaLocation? location=null) : IDirectDdsReplicaReader
{
    internal const string ExpectedBinding = "770CEECC0AB83A601BAE95A35FE1037C4D691A10E7AE840D6448F6DE76827384";
    // Exact owner-approved revision: eight additions and two source renames;
    // the renamed food source is assigned to RC; the separate ADM source stays ADM.
    // Retain the prior sealed snapshot identity for controlled rollback only.
    internal const string ApprovedBinding20261018 = "0375B39C632DF0246C86BABFEFBE10E10B0228FB5831899BA2C5CEF9F9BF3A9E";
    internal static void ValidateProvenance(string binding, string rawHash, string generation, DateOnly cutoff,
        DateTimeOffset originalCapture, DateTimeOffset verifiedCapture, DateTimeOffset published,
        DateTimeOffset checkedAt, int sourceLegs, IReadOnlyList<DateOnly> months)
        => ValidateProvenanceAt(binding, rawHash, generation, cutoff, originalCapture, verifiedCapture,
            published, checkedAt, sourceLegs, months, DateTimeOffset.UtcNow);

    // Internal pure clock seam for deterministic rollover tests; no request/config clock override.
    internal static void ValidateProvenanceAt(string binding, string rawHash, string generation, DateOnly cutoff,
        DateTimeOffset originalCapture, DateTimeOffset verifiedCapture, DateTimeOffset published,
        DateTimeOffset checkedAt, int sourceLegs, IReadOnlyList<DateOnly> months, DateTimeOffset now)
    {
        static bool Utc(DateTimeOffset value) => value != default && value.Offset == TimeSpan.Zero;
        if (binding is not (ExpectedBinding or ApprovedBinding20261018) || rawHash.Length != 64 || rawHash.Any(c => c is not (>= 'A' and <= 'F') and not (>= '0' and <= '9'))
            || !Utc(originalCapture) || !Utc(verifiedCapture) || !Utc(published) || !Utc(checkedAt)
            || originalCapture > verifiedCapture || originalCapture > published || published > checkedAt
            || verifiedCapture > checkedAt || checkedAt > now.AddMinutes(5) || sourceLegs < 1
            || cutoff <= new DateOnly(AccountingPeriod467.FirstYear, 1, 1) || cutoff > new DateOnly(2101, 1, 1)
            || cutoff != DateOnly.FromDateTime(verifiedCapture.ToOffset(TimeSpan.FromHours(5)).Date.AddDays(1)))
            throw new CashFlowActualsSourceInvalidException();
        var expected = AccountingPeriod467.CoveredMonths(cutoff);
        if (!months.SequenceEqual(expected)) throw new CashFlowActualsSourceInvalidException();
        var canonical = $"BMS-DirectDDS-Source-v1\n{cutoff:yyyy-MM-dd}\n{binding}\n{rawHash}\n";
        if (generation != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))))
            throw new CashFlowActualsSourceInvalidException();
    }

    public async Task<DirectDdsSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        // The execution report requires gross fact. No disabled/net/legacy-zero fallback.
        var builder = new SqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            InitialCatalog = (location??new()).Catalog, ApplicationName = "BMS-DirectDDS-Replica-ReadOnly",
            Pooling = false, ConnectTimeout = 15
        };
#if BMS_OWNED419_ACCEPTANCE
        AccountingReplicaLocation.RequireReader419(builder);
#else
        if (!builder.IntegratedSecurity || builder.UserID.Length != 0 || builder.Password.Length != 0)
            throw new InvalidOperationException("Direct DDS replica requires the existing Windows service identity.");
#endif
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure; command.CommandText = "dbo.ReadSnapshot467"; command.CommandTimeout = 30;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new CashFlowActualsNotAvailableException();
        Guid GuidField(string name) => reader.GetGuid(reader.GetOrdinal(name));
        string Text(string name) => reader.GetString(reader.GetOrdinal(name));
        decimal DecimalField(string name) => reader.GetDecimal(reader.GetOrdinal(name));
        if (GuidField("OwnerMarker") != Guid.Parse("6FC44B33-6166-4D35-85F1-622190601EB6")
            || reader.GetInt32(reader.GetOrdinal("ReaderVersion")) != 2 || !reader.GetBoolean(reader.GetOrdinal("IsSealed")))
            throw new CashFlowActualsSourceInvalidException();
        var id = GuidField("SnapshotId"); var generation = Text("SourceGeneration"); var hash = Text("GrossSHA256");
        var binding = Text("BindingSHA256"); var rawHash = Text("RawDataSHA256");
        var cutoff = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("CutoffExclusiveLocal")));
        var originalCapture = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("CapturedAtUtc"));
        var published = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("PublishedAtUtc"));
        var checkedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("LastCheckedAtUtc"));
        var sourceLegs = reader.GetInt32(reader.GetOrdinal("SourceLegs"));
        var captured = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("VerifiedSourceCapturedAtUtc"));
        var receipt = DecimalField("ReceiptsAmountKzt"); var payment = DecimalField("PaymentsAmountKzt"); var net = DecimalField("NetCashFlowAmountKzt");
        var count = reader.GetInt32(reader.GetOrdinal("ArticleCount")); var monthCount = reader.GetInt32(reader.GetOrdinal("MonthCount"));
        if (await reader.ReadAsync(cancellationToken)) throw new CashFlowActualsSourceInvalidException();
        var articles = new List<DirectDdsArticle>(); var months = new List<DateOnly>(); var cells = new List<DirectDdsCell>();
        if (!await reader.NextResultAsync(cancellationToken)) throw new CashFlowActualsSourceInvalidException();
        while (await reader.ReadAsync(cancellationToken))
        { if (articles.Count >= 2000) throw new CashFlowActualsSourceInvalidException(); articles.Add(new(reader.GetGuid(0), reader.GetString(1))); }
        if (!await reader.NextResultAsync(cancellationToken)) throw new CashFlowActualsSourceInvalidException();
        while (await reader.ReadAsync(cancellationToken))
        { if (months.Count >= AccountingPeriod467.MaximumMonths) throw new CashFlowActualsSourceInvalidException(); months.Add(DateOnly.FromDateTime(reader.GetDateTime(0))); }
        if (!await reader.NextResultAsync(cancellationToken)) throw new CashFlowActualsSourceInvalidException();
        while (await reader.ReadAsync(cancellationToken))
        { if (cells.Count >= AccountingPeriod467.MaximumCells) throw new CashFlowActualsSourceInvalidException(); cells.Add(new(reader.GetGuid(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetInt32(4))); }
        if (await reader.NextResultAsync(cancellationToken) || count != articles.Count || monthCount != months.Count)
            throw new CashFlowActualsSourceInvalidException();
        var result = new DirectDdsSnapshot(id, generation, hash, captured, months, articles, cells, receipt, payment, net)
            { CutoffExclusiveLocal = cutoff };
        ValidateProvenance(binding, rawHash, generation, cutoff, originalCapture, captured, published, checkedAt, sourceLegs, months);
        result.Validate();
        return result;
    }
}
