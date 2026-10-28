using Bms.Infrastructure.Mail;

namespace Bms.WeeklyPrimary425;

internal static class Program
{
    private static string Mode = "Unknown";
    private static bool SettingsRead;
    private static int GateCalls;
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = Wire425.Utf8; Console.OutputEncoding = Wire425.Utf8;
        try
        {
            Wire425.Require(args.Length == 0, "CLI_ARGUMENTS_REJECTED");
            var raw = await ReadLine(CancellationToken.None);

            var input = Wire425.Parse<Input425>(raw); Mode = input.Mode;
            Wire425.Require(Mode is "DryRun" or "Run" or "ManualResume", "MODE_INVALID");
            var config = Runtime425.Verify();
            if (Mode != "DryRun" && !Calendar425.MaySend(DateTimeOffset.UtcNow)) return Complete("SKIPPED_WINDOW", 0, 0, 0, new(), config);
            Wire425.Require(Mode == "DryRun" || (config.Enabled && Runtime425.IsSystem()), "ACTIVATION_OR_SYSTEM_REQUIRED");
            Wire425.Require(input.SqlLockHeld && input.SourceChecksPassed && input.RuntimeServicePid > 0, "SOURCE_RUNTIME_PROOF_REQUIRED");
            var now = DateTimeOffset.UtcNow;
            Wire425.Require(now - input.AsOfUtc >= TimeSpan.FromSeconds(-2) && now - input.AsOfUtc <= TimeSpan.FromSeconds(60)
                && input.WeekMonday == Calendar425.Monday(input.AsOfUtc), "SOURCE_CAPTURE_STALE");
            Wire425.Require(input.Batches is not null && input.Batches.Count <= Wire425.MaxOwners, "OWNERS_BOUND");
            var batches = input.Batches!;
            Wire425.Require(batches.Select(b => b.EmployeeId).Distinct().Count() == batches.Count
                && batches.Select(b => b.Recipient.ToUpperInvariant()).Distinct().Count() == batches.Count, "DUPLICATE_OWNER_OR_ADDRESS");
            Wire425.Require(batches.Sum(b => b.Rows.Count) <= Wire425.MaxRows, "GLOBAL_ROWS_BOUND");
            var prepared = new List<Prepared425>(); var empty = 0;
            foreach (var batch in batches)
            {
                Wire425.Require(batch.AsOfDate == Calendar425.Date(input.AsOfUtc), "ASOF_DATE_DRIFT");
                var rendered = Content425.Prepare(batch, input.WeekMonday);
                if (rendered is null) empty++; else prepared.Add(rendered);
            }
            // No network/attempt can begin until every owner's entire digest is valid.
            SettingsRead = true; var settings = ProtectedTaskMailSettings.Read();
            Wire425.Require(settings.Enabled, "SMTP_DISABLED"); SmtpTransport425.ValidatePolicy();
            foreach (var item in prepared) using (Content425.Build(item, settings.From!)) { }
            var ledger = new Ledger425(Path.Combine(Runtime425.Owned, "state")); using var processLock = ledger.Lock();
            if (Mode == "DryRun") return Complete("DRY_RUN_PASS", 0, prepared.Count, empty, new(), config);
            if (Mode == "Run" && ledger.AutomaticRunExists(input.WeekMonday)) return Complete("SKIPPED_ALREADY_STARTED_MANUAL_REVIEW_REQUIRED", 0, prepared.Count, empty, new(), config);
            var results = new Dictionary<string, int>(StringComparer.Ordinal); var eligible = new List<Prepared425>();
            foreach (var item in prepared)
            {
                var state = ledger.NextAttempt(item, Mode);
                if (state.Number == 0) { Increment(results, state.State == "Accepted" ? "AcceptedPreviously" : state.State); continue; }
                try { ledger.ValidatePrepared(item); eligible.Add(item); }
                catch (Guard425 e) when (e.Code == "PREPARED_CHANGED_MANUAL_REVIEW_REQUIRED") { Increment(results, "PreparedChangedManualReviewRequired"); }
            }
            if (Mode == "Run") ledger.ClaimAutomaticRun(input.WeekMonday);
            // Persist the whole prepared set before first SMTP. Failures never truncate and send a prefix.
            foreach (var item in eligible) ledger.StorePrepared(item);
            using var runDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            var smtp = new SmtpTransport425(settings);
            foreach (var item in eligible)
            {
                if (!Calendar425.MaySend(DateTimeOffset.UtcNow) || !Runtime425.ReadConfig().Enabled)
                { Increment(results, "SkippedWindowOrDisabled"); continue; }
                async Task<bool> Gate(CancellationToken token)
                {
                    if (!Calendar425.MaySend(DateTimeOffset.UtcNow) || !Runtime425.ReadConfig().Enabled) return false;
                    GateCalls++; Wire425.Require(GateCalls <= 2 * prepared.Count, "REVALIDATION_COUNT_BOUND");
                    await Console.Out.WriteLineAsync(Wire425.Serialize(new { kind = "revalidate", employeeId = item.EmployeeId }));
                    await Console.Out.FlushAsync(token);
                    var response = Wire425.Parse<Revalidation425>(await ReadLine(token));
                    return Content425.RevalidationMatches(item, response, DateTimeOffset.UtcNow);
                }
                var result = await DeliveryEngine425.Deliver(ledger, item, Mode, smtp, Gate, runDeadline.Token);
                Increment(results, result);
            }
            var attention = results.Any(p => p.Key is not ("Accepted" or "AcceptedPreviously"));
            return Complete(attention ? "COMPLETED_WITH_BLOCKS_NO_AUTO_RETRY" : "COMPLETED_SMTP_ACCEPTANCE_ONLY", attention ? 2 : 0, prepared.Count, empty, results, config);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Console.WriteLine(Wire425.Serialize(new { kind = "complete", status = "FAILED_NO_AUTOMATIC_RETRY", mode = Mode,
                code = error is Guard425 g ? g.Code : "UNAVAILABLE", exceptionType = error.GetType().Name,
                hResult = "0x" + error.HResult.ToString("X8"), protectedSettingsRead = SettingsRead, retryAllowed = false, financialWrites = 0 }));
            return 2;
        }
    }
    private static async Task<string> ReadLine(CancellationToken token)
    {
        var line = await Console.In.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(45), token);
        Wire425.Require(line is not null && line.Length is > 0 and <= Wire425.MaxInputCharacters, "STDIN_BOUND"); return line!;
    }
    private static void Increment(Dictionary<string, int> values, string key) => values[key] = values.GetValueOrDefault(key) + 1;
    private static int Complete(string status, int exit, int owners, int empty, Dictionary<string, int> states, Config425 c)
    {
        Console.WriteLine(Wire425.Serialize(new { kind = "complete", status, mode = Mode, atUtc = DateTimeOffset.UtcNow,
            systemIdentity = Runtime425.IsSystem(), protectedSettingsRead = SettingsRead, ownerCount = owners, emptyOwnerCount = empty,
            states, revalidationCount = GateCalls, financialWrites = 0, packageManifestSha256 = c.PackageManifestSha256,
            configSha256 = Wire425.FileHash(Path.Combine(Runtime425.Owned, "runner-config.json")),
            retryAllowed = false, smtpAcceptanceIsMailboxDelivery = false }));
        return exit;
    }
}
