using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed record CurrencyRateDirectoryRow(Guid CurrencyId, string Code, string Name,
    DateOnly RateDate, decimal? Rate, decimal? Multiplicity, string? SourceName, string Stamp);
public sealed record CurrencyRateDirectoryPage(DateOnly BusinessDate, DateOnly Date,
    IReadOnlyList<CurrencyRateDirectoryRow> Items, int Total, int Skip, int Take, string? Notice);
public sealed record ManualCurrencyRateCommand(Guid CurrencyId, DateOnly Date, decimal Rate,
    decimal Multiplicity, string ExpectedStamp, string Reason);
public sealed record ExpenseDraftDefaults(DateOnly BusinessDate, Guid? CostCenterId,
    CurrencyRateDirectoryRow? CurrencyRate);

/// <summary>Audited rate maintenance only; immutable request conversion snapshots are never rewritten.</summary>
public sealed class CurrencyRateDirectoryService(BmsDbContext db, IBusinessTransactionRunner transactions,
    BudgetDocumentAuthority authority, ICurrentActor actor, IClock clock, HttpClient http)
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Qyzylorda");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public DateTimeOffset BusinessNow => TimeZoneInfo.ConvertTime(clock.UtcNow, Zone);
    public DateOnly BusinessDate => DateOnly.FromDateTime(BusinessNow.DateTime);
    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Stamp(CurrencyRateReference? rate) => rate is null ? "new" : Convert.ToHexString(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { rate.CurrencyId, rate.RateDate,
            Rate = rate.Rate.ToString("G29", CultureInfo.InvariantCulture),
            Multiplicity = rate.Multiplicity.ToString("G29", CultureInfo.InvariantCulture), rate.SourceName,
            PublishedAtSource = rate.PublishedAtSource.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) }, JsonOptions)));

    // Display-only defaults: never changes a request, authorizes creation, or
    // relaxes the submission resolver's daily freshness policy.
    public async Task<ExpenseDraftDefaults> DraftDefaultsAsync(string currencyCode, CancellationToken token)
    {
        var actorId = ActorIdentity.RequireBusinessDataAccess(actor);
        if (currencyCode.Length != 3 || currencyCode.Any(c => c is < 'A' or > 'Z'))
            throw new DomainRuleViolationException("Invalid currency code.");
        var day = BusinessDate;
        var employees = await BusinessDataReadScopeQuery.EmployeeIds(db, actorId).Take(2).ToArrayAsync(token);
        Guid? costCenterId = null;
        if (employees.Length == 1)
        {
            var memberships = await BusinessDataReadScopeQuery.AssignedCreatorCostCenterIds(db, employees[0])
                .Distinct().Take(2).ToArrayAsync(token);
            costCenterId = UniqueDefaultCostCenter(memberships);
        }
        var currency = await db.Currencies.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == currencyCode && c.IsActive, token);
        CurrencyRateDirectoryRow? row = null;
        if (currency is not null)
        {
            var rate = await LatestQuoteQuery(currency.Id, day).FirstOrDefaultAsync(token);
            if (currencyCode == "KZT") row = new(currency.Id, currency.Code, currency.Name, day, 1m, 1m, "KZT", "base");
            else if (rate is not null) row = new(currency.Id, currency.Code, currency.Name, rate.RateDate,
                rate.Rate, rate.Multiplicity, rate.SourceName, Stamp(rate));
        }
        return new(day, costCenterId, row);
    }

    internal static Guid? UniqueDefaultCostCenter(IReadOnlyList<Guid> memberships) =>
        memberships.Count == 1 && memberships[0] != Guid.Empty ? memberships[0] : null;
    internal IQueryable<CurrencyRateReference> LatestQuoteQuery(Guid currencyId, DateOnly day) =>
        db.CurrencyRates.AsNoTracking().Where(r => r.CurrencyId == currencyId && r.RateDate <= day
            && r.Rate > 0 && r.Multiplicity > 0).OrderByDescending(r => r.RateDate);

    public async Task<CurrencyRateDirectoryPage> ListAsync(DateOnly? requested, int skip, int take, CancellationToken token)
    {
        ActorIdentity.RequireBusinessDataAccess(actor);
        var date = requested ?? BusinessDate;
        if (date > BusinessDate || skip < 0 || take is < 1 or > 100) throw new DomainRuleViolationException("Invalid rate filters.");
        var currencies = db.Currencies.AsNoTracking().Where(c => c.IsActive);
        var total = await currencies.CountAsync(token);
        var selected = await currencies.OrderBy(c => c.Code).Skip(skip).Take(take).ToArrayAsync(token);
        var ids = selected.Select(c => c.Id).ToArray();
        var rates = await db.CurrencyRates.AsNoTracking().Where(r => r.RateDate == date && ids.Contains(r.CurrencyId))
            .ToDictionaryAsync(r => r.CurrencyId, token);
        var day = Day(BusinessDate);
        var lastImport = await db.BusinessAuditEntries.AsNoTracking().Where(a => a.ObjectType == "CurrencyRateImport"
            && a.ObjectId == day).OrderByDescending(a => a.OccurredAtUtc).Select(a => a.Action).FirstOrDefaultAsync(token);
        var missing = await db.Currencies.AnyAsync(c => c.IsActive && c.Code != "KZT"
            && !db.CurrencyRates.Any(r => r.CurrencyId == c.Id && r.RateDate == BusinessDate && r.Rate > 0 && r.Multiplicity > 0), token);
        var notice = lastImport == "CurrencyRateImportFailed"
            ? "Загрузка курсов Нацбанка не выполнена. Введите курс вручную в справочнике «Курсы валют» или повторите загрузку."
            : missing ? "Для части валют нет актуального курса. Введите курс вручную или загрузите курсы Нацбанка." : null;
        return new(BusinessDate, date, selected.Select(c =>
        {
            rates.TryGetValue(c.Id, out var r);
            return new CurrencyRateDirectoryRow(c.Id, c.Code, c.Name, date,
                c.Code == "KZT" ? 1m : r?.Rate, c.Code == "KZT" ? 1m : r?.Multiplicity,
                c.Code == "KZT" ? "KZT" : r?.SourceName, Stamp(r));
        }).ToArray(), total, skip, take, notice);
    }

    public Task SaveManualAsync(ManualCurrencyRateCommand command, CancellationToken token)
    {
        ActorIdentity.RequireReferenceDataManagement(actor);
        if (command.Date != BusinessDate || command.CurrencyId == Guid.Empty || command.Rate <= 0m
            || command.Rate >= 1000000000m || decimal.Round(command.Rate, 6) != command.Rate
            || command.Multiplicity is <= 0m or >= 10000000000m || decimal.Truncate(command.Multiplicity) != command.Multiplicity
            || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500 || command.Reason.Any(char.IsControl)
            || string.IsNullOrEmpty(command.ExpectedStamp)) throw new DomainRuleViolationException("Invalid manual rate.");
        return transactions.ExecuteAsync(async ct =>
        {
            await LockAsync(ct);
            if (!await authority.CanMaintainDirectoryAsync(actor, ct)) throw new ActorForbiddenException();
            if (command.Date != BusinessDate) throw new DomainRuleViolationException("The business date changed.");
            var currency = await db.Currencies.SingleOrDefaultAsync(c => c.Id == command.CurrencyId && c.IsActive, ct);
            if (currency is null || currency.Code == "KZT") throw new DomainRuleViolationException("Currency is unavailable.");
            var current = await db.CurrencyRates.SingleOrDefaultAsync(r => r.CurrencyId == command.CurrencyId && r.RateDate == command.Date, ct);
            if (Stamp(current) != command.ExpectedStamp) throw new CurrencyRateStateConflictException();
            var before = current is null ? "{}" : JsonSerializer.Serialize(current, JsonOptions);
            current ??= AddRate(command.CurrencyId, command.Date);
            current.Rate = command.Rate; current.Multiplicity = command.Multiplicity;
            current.SourceName = "Manual"; current.PublishedAtSource = BusinessNow.DateTime;
            Audit("CurrencyRate", currency.Code + ":" + Day(command.Date), "CurrencyRateManuallySet",
                actor.ExternalId!, before, new { currency.Code, command.Date, command.Rate, command.Multiplicity, Reason = command.Reason.Trim() });
            await db.SaveChangesAsync(ct);
            return true;
        }, token);
    }

    public async Task<bool> ImportAsync(bool scheduled, CancellationToken token)
    {
        var date = BusinessDate;
        if (!scheduled) ActorIdentity.RequireReferenceDataManagement(actor);
        if (!scheduled && !await authority.CanMaintainDirectoryAsync(actor, token)) throw new ActorForbiddenException();
        if (scheduled && await db.BusinessAuditEntries.AsNoTracking().AnyAsync(a => a.ObjectType == "CurrencyRateImport"
            && a.ObjectId == Day(date) && a.Action == "CurrencyRateImportSucceeded", token)) return true;
        IReadOnlyList<NationalBankRate>? rates = null;
        try
        {
            using var response = await http.GetAsync(NationalBankCurrencyFeed.ForDate(date), HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > NationalBankCurrencyFeed.MaximumBytes) throw new FormatException();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var memory = new MemoryStream();
            var buffer = new byte[4096]; int read;
            while ((read = await stream.ReadAsync(buffer, token)) != 0)
            {
                if (memory.Length + read > NationalBankCurrencyFeed.MaximumBytes) throw new FormatException();
                memory.Write(buffer, 0, read);
            }
            rates = NationalBankCurrencyFeed.Parse(System.Text.Encoding.UTF8.GetString(memory.ToArray()), date);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or FormatException or System.Xml.XmlException or OperationCanceledException)
        { /* Only a fixed public failure is persisted; no exception/secret payload. */ }
        var importCorrelation = Guid.NewGuid().ToString("N");
        return await transactions.ExecuteAsync(async ct =>
        {
            await LockAsync(ct);
            if (!scheduled && !await authority.CanMaintainDirectoryAsync(actor, ct)) throw new ActorForbiddenException();
            if (date != BusinessDate) return false;
            if (scheduled && await db.BusinessAuditEntries.AnyAsync(a => a.ObjectType == "CurrencyRateImport" && a.ObjectId == Day(date)
                && a.Action == "CurrencyRateImportSucceeded", ct)) return true;
            var currencies = await db.Currencies.Where(c => c.IsActive && c.Code != "KZT").ToArrayAsync(ct);
            var existing = await db.CurrencyRates.Where(r => r.RateDate == date).ToDictionaryAsync(r => r.CurrencyId, ct);
            var imported = rates?.ToDictionary(r => r.Code, StringComparer.Ordinal);
            // An unsupported active currency needs a manually supplied current rate. Never partially commit a batch.
            var success = imported is not null && currencies.All(c => imported.ContainsKey(c.Code)
                || existing.TryGetValue(c.Id, out var manual) && manual.SourceName == "Manual" && manual.Rate > 0m && manual.Multiplicity > 0m);
            var writtenCount = 0;
            if (success)
                foreach (var currency in currencies)
                {
                    existing.TryGetValue(currency.Id, out var current);
                    if (current?.SourceName == "Manual") continue; // Explicit operator correction wins.
                    var quote = imported![currency.Code];
                    var before = current is null ? "{}" : JsonSerializer.Serialize(current, JsonOptions);
                    current ??= AddRate(currency.Id, date);
                    current.Rate = quote.Rate; current.Multiplicity = quote.Multiplicity;
                    // The feed supplies an effective date, not a publication timestamp.
                    current.SourceName = "NationalBank.kz"; current.PublishedAtSource = date.ToDateTime(TimeOnly.MinValue);
                    Audit("CurrencyRate", currency.Code + ":" + Day(date), "CurrencyRateImported", "BMS:NBK", before, quote, importCorrelation);
                    writtenCount++;
                }
            Audit("CurrencyRateImport", Day(date), success ? "CurrencyRateImportSucceeded" : "CurrencyRateImportFailed",
                scheduled ? "BMS:NBK" : actor.ExternalId!, "{}", new { Date = date, Success = success, Count = writtenCount,
                    ExpectedCurrencyCount = currencies.Length, PreservedManualCount = success ? currencies.Length - writtenCount : 0 }, importCorrelation);
            await db.SaveChangesAsync(ct);
            return success;
        }, token);
    }

    private CurrencyRateReference AddRate(Guid id, DateOnly date)
    {
        var rate = new CurrencyRateReference { CurrencyId = id, RateDate = date };
        db.CurrencyRates.Add(rate); return rate;
    }
    private void Audit(string type, string id, string action, string externalId, string before, object after, string? correlationId = null) =>
        db.BusinessAuditEntries.Add(new() { Id = Guid.NewGuid(), OccurredAtUtc = clock.UtcNow,
            ActorExternalId = externalId, ObjectType = type, ObjectId = id, Action = action,
            BeforeJson = before, AfterJson = JsonSerializer.Serialize(after, JsonOptions), CorrelationId = correlationId ?? Guid.NewGuid().ToString("N") });
    private Task LockAsync(CancellationToken token) => db.Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @fx_lock int;
        EXEC @fx_lock=sys.sp_getapplock @Resource=N'bms-web:currency-rates', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
        IF @fx_lock<0 THROW 64892,N'Currency rate lock unavailable.',1;
        """, token);
}

public sealed class CurrencyRateStateConflictException : Exception;
