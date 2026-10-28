namespace Bms.Application.Security;

/// <summary>
/// Stable public codes for the request-scoped BMS access profile. A code only
/// selects among assignments already resolved by the server; it is never an
/// assignment or an identity claim by itself.
/// </summary>
public static class BmsAccessProfileCodes
{
    public const string Business = "BUSINESS";
    public const string SystemAdministrator = "SYSTEM_ADMIN";
    public const string IntegrationAdministrator = "INTEGRATION_ADMIN";
}
