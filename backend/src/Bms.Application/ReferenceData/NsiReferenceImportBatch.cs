using System.Collections.ObjectModel;

namespace Bms.Application.ReferenceData;

/// <summary>A source item for the existing bms_web.Counterparties projection.</summary>
public sealed class NsiCounterpartyImportItem
{
    private NsiCounterpartyImportItem(Guid id, string code, string name, bool isActive)
    {
        Id = id;
        Code = code;
        Name = name;
        IsActive = isActive;
    }

    public Guid Id { get; }

    public string Code { get; }

    public string Name { get; }

    public bool IsActive { get; }

    public static NsiCounterpartyImportItem Create(
        Guid id,
        string code,
        string name,
        bool isActive)
    {
        ValidateIdentifier(id, nameof(id), "Counterparty id");
        return new NsiCounterpartyImportItem(
            id,
            NsiImportText.Validate(code, 20, nameof(code), "Counterparty code"),
            NsiImportText.Validate(name, 150, nameof(name), "Counterparty name"),
            isActive);
    }

    private static void ValidateIdentifier(Guid value, string parameterName, string fieldName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{fieldName} is required.", parameterName);
        }
    }
}

/// <summary>A source item for the existing bms_web.Contracts projection.</summary>
public sealed class NsiContractImportItem
{
    private NsiContractImportItem(
        Guid id,
        Guid counterpartyId,
        Guid organizationId,
        string code,
        string name,
        DateOnly contractDate,
        Guid currencyId,
        bool isActive)
    {
        Id = id;
        CounterpartyId = counterpartyId;
        OrganizationId = organizationId;
        Code = code;
        Name = name;
        ContractDate = contractDate;
        CurrencyId = currencyId;
        IsActive = isActive;
    }

    public Guid Id { get; }

    public Guid CounterpartyId { get; }

    public Guid OrganizationId { get; }

    public string Code { get; }

    public string Name { get; }

    public DateOnly ContractDate { get; }

    public Guid CurrencyId { get; }

    public bool IsActive { get; }

    public static NsiContractImportItem Create(
        Guid id,
        Guid counterpartyId,
        Guid organizationId,
        string code,
        string name,
        DateOnly contractDate,
        Guid currencyId,
        bool isActive)
    {
        ValidateIdentifier(id, nameof(id), "Contract id");
        ValidateIdentifier(counterpartyId, nameof(counterpartyId), "Contract counterparty id");
        ValidateIdentifier(organizationId, nameof(organizationId), "Contract organization id");
        ValidateIdentifier(currencyId, nameof(currencyId), "Contract currency id");
        if (contractDate == default)
        {
            throw new ArgumentException("Contract date is required.", nameof(contractDate));
        }

        return new NsiContractImportItem(
            id,
            counterpartyId,
            organizationId,
            NsiImportText.Validate(code, 30, nameof(code), "Contract code"),
            NsiImportText.Validate(name, 150, nameof(name), "Contract name"),
            contractDate,
            currencyId,
            isActive);
    }

    private static void ValidateIdentifier(Guid value, string parameterName, string fieldName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{fieldName} is required.", parameterName);
        }
    }
}

/// <summary>
/// Immutable transport-neutral batch. An empty collection is valid and makes
/// no completeness claim; absence never means delete or deactivate.
/// </summary>
public sealed class NsiReferenceImportBatch
{
    private NsiReferenceImportBatch(
        IReadOnlyList<NsiCounterpartyImportItem> counterparties,
        IReadOnlyList<NsiContractImportItem> contracts)
    {
        Counterparties = counterparties;
        Contracts = contracts;
    }

    public IReadOnlyList<NsiCounterpartyImportItem> Counterparties { get; }

    public IReadOnlyList<NsiContractImportItem> Contracts { get; }

    public static NsiReferenceImportBatch Create(
        IEnumerable<NsiCounterpartyImportItem> counterparties,
        IEnumerable<NsiContractImportItem> contracts)
    {
        ArgumentNullException.ThrowIfNull(counterparties);
        ArgumentNullException.ThrowIfNull(contracts);

        var counterpartySnapshot = counterparties.ToArray();
        var contractSnapshot = contracts.ToArray();
        ValidateItems(counterpartySnapshot, contractSnapshot);

        return new NsiReferenceImportBatch(
            new ReadOnlyCollection<NsiCounterpartyImportItem>(counterpartySnapshot),
            new ReadOnlyCollection<NsiContractImportItem>(contractSnapshot));
    }

    private static void ValidateItems(
        IReadOnlyList<NsiCounterpartyImportItem> counterparties,
        IReadOnlyList<NsiContractImportItem> contracts)
    {
        if (counterparties.Any(item => item is null))
        {
            throw new ArgumentException("Counterparty import batch contains a null item.", nameof(counterparties));
        }
        if (contracts.Any(item => item is null))
        {
            throw new ArgumentException("Contract import batch contains a null item.", nameof(contracts));
        }

        EnsureUnique(
            counterparties,
            item => item.Id,
            EqualityComparer<Guid>.Default,
            "Counterparty ids must be unique.");
        EnsureUnique(
            counterparties,
            item => item.Code,
            StringComparer.Ordinal,
            "Counterparty codes must be ordinal-unique.");
        EnsureUnique(
            contracts,
            item => item.Id,
            EqualityComparer<Guid>.Default,
            "Contract ids must be unique.");
        EnsureUnique(
            contracts,
            item => item.Code,
            StringComparer.Ordinal,
            "Contract codes must be ordinal-unique.");
    }

    private static void EnsureUnique<TItem, TKey>(
        IEnumerable<TItem> items,
        Func<TItem, TKey> keySelector,
        IEqualityComparer<TKey> comparer,
        string errorMessage)
        where TKey : notnull
    {
        var keys = new HashSet<TKey>(comparer);
        if (items.Any(item => !keys.Add(keySelector(item))))
        {
            throw new ArgumentException(errorMessage, nameof(items));
        }
    }
}

internal static class NsiImportText
{
    internal static string Validate(
        string? value,
        int maxLength,
        string parameterName,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{fieldName} is required.", parameterName);
        }
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{fieldName} must not have surrounding whitespace.",
                parameterName);
        }
        if (value.Length > maxLength)
        {
            throw new ArgumentException(
                $"{fieldName} cannot exceed {maxLength} characters.",
                parameterName);
        }
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"{fieldName} must not contain control characters.",
                parameterName);
        }

        return value;
    }
}
