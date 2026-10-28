using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Bms.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

internal sealed record CounterpartyIdentityDecision428(string SourceDatabase, string SourceRef, string State,
    Guid? TargetId, string? ExpectedSourceBin, string? ExpectedSourceName, bool ExpectedSourceActive,
    string SourceFingerprint, string? TargetBinAtDecision, string DecisionReason, Guid DecisionSetId,
    DateTimeOffset SourceCapturedAtUtc, string? CurrentTargetBin, bool? CurrentTargetIsActive,
    bool Foreign405ApprovalConfirmed, bool TypedForeignApprovalConfirmed = false,
    string? ForeignCountry = null, string? ForeignIdentifierType = null,
    string? ForeignIdentifier = null, string? TargetNameAtDecision = null);

internal sealed record CounterpartyResolution428(Guid? TargetId, string Reason)
{
    internal bool IsRegistryBlock => TargetId is null && Reason != "LEGACY_NOT_UNIQUE";
}

/// <summary>Identity-only admission. It never edits source facts, financial hashes, amounts or existing links.</summary>
internal sealed class CounterpartySourceIdentity428
{
    internal const int MaxRows = 100000;
    private static readonly Regex DocumentKey = new("^(ExampleAccountingA|ExampleAccountingB|ExampleAccountingC)\\|[0-9A-F]{8}\\|[0-9A-F]{32}$", RegexOptions.CultureInvariant);
    private readonly Dictionary<(string Database, string Ref), CounterpartyIdentityDecision428> decisions;
    private readonly HashSet<string> knownRefs;
    private readonly HashSet<Guid> typedTargets;
    private readonly Dictionary<Guid, CounterpartyReference> targets;
    private readonly Dictionary<string, CounterpartyReference[]> bins;
    private readonly Dictionary<string, CounterpartyReference[]> names;

    internal CounterpartySourceIdentity428(IEnumerable<CounterpartyIdentityDecision428> rows, IEnumerable<CounterpartyReference> parties)
    {
        decisions = new();
        foreach (var row in rows)
        {
            Validate(row);
            if (decisions.Count >= MaxRows || !decisions.TryAdd((row.SourceDatabase, row.SourceRef), row))
                throw new InvalidOperationException("Counterparty identity registry is invalid or exceeds its bound.");
        }
        knownRefs = decisions.Values.Select(r => r.SourceRef).ToHashSet(StringComparer.Ordinal);
        typedTargets = decisions.Values.Where(r => r.ForeignIdentifier is not null && r.TargetNameAtDecision is not null)
            .Where(r => r.TargetId.HasValue).Select(r => r.TargetId!.Value).ToHashSet();
        targets = parties.ToDictionary(p => p.Id);
        bins = targets.Values.Where(p => ValidBin(p.Bin)).GroupBy(p => (p.Bin ?? throw new InvalidOperationException("Counterparty BIN missing.")).Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        names = targets.Values.GroupBy(p => p.Name.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }

    internal CounterpartyResolution428 Resolve(AccountingSourcePayment payment)
    {
        if (!DocumentKey.IsMatch(payment.DocumentKey) || !Hex(payment.CounterpartyRef, 32) || payment.CounterpartyRef.All(c => c == '0'))
            return new(null, "INVALID_SOURCE_IDENTITY");
        var database = payment.DocumentKey[..payment.DocumentKey.IndexOf('|')];
        if (decisions.TryGetValue((database, payment.CounterpartyRef), out var decision))
        {
            if (decision.State != "Approved") return new(null, "REGISTRY_" + decision.State.ToUpperInvariant());
            if (!decision.ExpectedSourceActive || payment.Bin != (decision.ExpectedSourceBin ?? "")
                || payment.CounterpartyName != decision.ExpectedSourceName)
                return new(null, "SOURCE_IDENTITY_DRIFT");
            var foreignApproved = decision.Foreign405ApprovalConfirmed
                && database is "ExampleAccountingA" or "ExampleAccountingB"
                && decision.TargetBinAtDecision is null && !ValidBin(payment.Bin)
                && payment.Bin.Length > 0 && payment.Bin.All(c => c is >= '0' and <= '9');
            // 466 is a separate, server-proven exact-pair permission, not a wider
            // interpretation of Foreign405. Empty/zero source IDs remain raw.
            var typedApproved = decision.TypedForeignApprovalConfirmed
                && TypedIdentifier(decision.ForeignCountry, decision.ForeignIdentifierType, decision.ForeignIdentifier);
            if (!(ValidBin(payment.Bin) && payment.Bin.Any(c => c != '0')) && !foreignApproved && !typedApproved)
                return new(null, "APPROVED_TAX_IDENTITY_REQUIRED");
            if (decision.TargetId is not { } id || !targets.TryGetValue(id, out var target)
                || !target.IsActive || decision.CurrentTargetIsActive != true
                || target.Bin != decision.TargetBinAtDecision || decision.CurrentTargetBin != decision.TargetBinAtDecision)
                return new(null, "TARGET_IDENTITY_DRIFT");
            if (typedApproved && target.Name != decision.TargetNameAtDecision)
                return new(null, "TARGET_IDENTITY_DRIFT");
            // Foreign tax identifiers are never padded, truncated or written into a Kazakhstan BIN.
            // The finite reader independently proves the exact pre-existing foreign405 approval.
            if (!string.IsNullOrEmpty(payment.Bin) && payment.Bin != target.Bin
                && !(foreignApproved && target.Bin is null) && !typedApproved)
                return new(null, "SOURCE_TARGET_BIN_CONFLICT");
            return new(id, "APPROVED_SOURCE_PAIR");
        }
        // A known reference in another 1C database does not authorize a new alias.
        if (knownRefs.Contains(payment.CounterpartyRef)) return new(null, "UNAPPROVED_SOURCE_DATABASE");
        var matches = ValidBin(payment.Bin) ? bins.GetValueOrDefault(payment.Bin.Trim()) ?? []
            : string.IsNullOrWhiteSpace(payment.Bin) ? names.GetValueOrDefault(payment.CounterpartyName.Trim()) ?? [] : [];
        // A newly seen source reference must not acquire a manually scoped
        // foreign target through the old name-only compatibility fallback.
        if (matches.Any(p => typedTargets.Contains(p.Id))) return new(null, "UNAPPROVED_TYPED_SOURCE_PAIR");
        return matches.Length == 1 ? new(matches[0].Id, "LEGACY_UNIQUE") : new(null, "LEGACY_NOT_UNIQUE");
    }

    internal bool MatchesExisting(AccountingSourcePayment payment, Guid? targetId) =>
        targetId is not null && Resolve(payment).TargetId == targetId;

    internal static string Fingerprint(string database, string sourceRef, string? bin, string? name, bool active)
    {
        var text = new StringBuilder("BMS-CounterpartyIdentity428-v1|");
        foreach (var value in new[] { database, sourceRef, bin ?? "", name ?? "", active ? "1" : "0" })
            text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static bool ValidBin(string? value) => value?.Trim() is { Length: 12 } bin && bin.All(c => c is >= '0' and <= '9');
    private static bool Hex(string value, int length) => value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    internal static bool TypedIdentifier(string? country, string? type, string? value) => value is not null
        && value.All(c => c is >= '0' and <= '9') && value.Any(c => c != '0')
        && (country, type, value.Length) is ("Россия", "ИНН", 10) or ("Беларусь", "УНП", 9)
            or ("Армения", "налоговый номер", 8) or ("Италия", "P.IVA", 11)
            or ("Грузия", "идентификационный код", 9) or ("Литва", "регистрационный код", 9);
    private static void Validate(CounterpartyIdentityDecision428 row)
    {
        if (row.SourceDatabase is not ("ExampleAccountingA" or "ExampleAccountingB" or "ExampleAccountingC")
            || !Hex(row.SourceRef, 32) || row.SourceRef.All(c => c == '0')
            || row.State is not ("Approved" or "Held" or "Revoked")
            || row.ExpectedSourceBin?.Length > 4000 || row.ExpectedSourceName?.Length > 4000
            || row.DecisionSetId == Guid.Empty || string.IsNullOrWhiteSpace(row.DecisionReason) || row.DecisionReason.Length > 160
            || row.SourceCapturedAtUtc == default || row.SourceCapturedAtUtc.Offset != TimeSpan.Zero
            || row.SourceFingerprint != Fingerprint(row.SourceDatabase, row.SourceRef, row.ExpectedSourceBin, row.ExpectedSourceName, row.ExpectedSourceActive)
            || row.TargetId == Guid.Empty || row.TargetBinAtDecision is not null && !ValidBin(row.TargetBinAtDecision)
            || row.CurrentTargetBin is not null && !ValidBin(row.CurrentTargetBin)
            || row.TypedForeignApprovalConfirmed && (!TypedIdentifier(row.ForeignCountry, row.ForeignIdentifierType, row.ForeignIdentifier)
                || string.IsNullOrWhiteSpace(row.TargetNameAtDecision) || row.TargetNameAtDecision.Length > 150)
            || row.State == "Approved" && (row.TargetId is null || string.IsNullOrWhiteSpace(row.ExpectedSourceName)
                || row.ExpectedSourceName != row.ExpectedSourceName.Trim(' ') || row.ExpectedSourceName.Any(char.IsControl)
                || row.ExpectedSourceBin is { } bin && (bin != bin.Trim(' ') || bin.Any(char.IsControl))))
            throw new InvalidOperationException("Counterparty identity registry row failed validation.");
    }

    internal static async Task<CounterpartySourceIdentity428> ReadAsync(BmsDbContext db, CounterpartyReference[] parties, CancellationToken token)
    {
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        if (transaction?.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Counterparty identity admission requires the business transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction; command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "bms_reference466.ReadCounterpartySourceIdentities"; command.CommandTimeout = 30;
        await using var reader = await command.ExecuteReaderAsync(token);
        return await ParseAsync(reader, parties, token);
    }

    internal static async Task<CounterpartySourceIdentity428> ParseAsync(DbDataReader reader, CounterpartyReference[] parties, CancellationToken token)
    {
        RequireColumns(reader, ["ContractVersion", "MaxRows"]);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Counterparty identity reader contract unavailable.");
        var version = reader.GetInt32(0);
        if (version is not (1 or 2) || reader.GetInt32(1) != MaxRows || await reader.ReadAsync(token)
            || !await reader.NextResultAsync(token)) throw new InvalidOperationException("Counterparty identity reader contract unavailable.");
        string[] columns = ["SourceDatabase", "SourceRef", "State", "TargetId", "ExpectedSourceBin", "ExpectedSourceName",
            "ExpectedSourceActive", "SourceFingerprint", "TargetBinAtDecision", "DecisionReason", "DecisionSetId", "SourceCapturedAtUtc",
            "CurrentTargetBin", "CurrentTargetIsActive", "Foreign405ApprovalConfirmed"];
        if (version == 2) columns = [.. columns, "TypedForeignApprovalConfirmed", "ForeignCountry", "ForeignIdentifierType", "ForeignIdentifier", "TargetNameAtDecision"];
        RequireColumns(reader, columns);
        var rows = new List<CounterpartyIdentityDecision428>();
        while (await reader.ReadAsync(token))
        {
            if (rows.Count >= MaxRows) throw new InvalidOperationException("Counterparty identity registry exceeds its bound.");
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetBoolean(6),
                reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9), reader.GetGuid(10),
                reader.GetFieldValue<DateTimeOffset>(11), reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetBoolean(13), reader.GetBoolean(14),
                version == 2 && reader.GetBoolean(15),
                version == 2 && !reader.IsDBNull(16) ? reader.GetString(16) : null,
                version == 2 && !reader.IsDBNull(17) ? reader.GetString(17) : null,
                version == 2 && !reader.IsDBNull(18) ? reader.GetString(18) : null,
                version == 2 && !reader.IsDBNull(19) ? reader.GetString(19) : null));
        }
        if (await reader.NextResultAsync(token)) throw new InvalidOperationException("Unexpected counterparty identity result.");
        return new(rows, parties);
    }

    private static void RequireColumns(DbDataReader reader, string[] columns)
    {
        if (reader.FieldCount != columns.Length || columns.Where((name, index) => reader.GetName(index) != name).Any())
            throw new InvalidOperationException("Counterparty identity reader schema changed.");
    }
}
