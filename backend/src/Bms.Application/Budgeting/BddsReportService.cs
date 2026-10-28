using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.Workflow;

namespace Bms.Application.Budgeting;

public sealed class BddsReportService(
    IBddsReportStore store,
    EmployeeActorResolver employeeActorResolver,
    EmployeeFunctionalRoleResolver roleResolver,
    ICurrentActor actor,
    IBusinessTransactionRunner transactions,
    IClock clock)
{
    public async Task<BddsReportDto> ReadAsync(
        int year,
        Guid? costCenterId,
        CancellationToken cancellationToken) => await ReadCoreAsync(year, costCenterId, null, cancellationToken);

    public Task<BddsReportDto> ReadPeriodAsync(int year, Guid? costCenterId,
        BddsReportOptions options, CancellationToken cancellationToken) =>
        ReadCoreAsync(year, costCenterId, options, cancellationToken);

    private async Task<BddsReportDto> ReadCoreAsync(int year, Guid? costCenterId,
        BddsReportOptions? options, CancellationToken cancellationToken)
    {
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);
        if (year is < 2000 or > 2100 || costCenterId == Guid.Empty)
        {
            throw new DomainRuleViolationException("BDDS report filter is invalid.");
        }
        options?.Months(year);

        return await transactions.ExecuteAsync(async token =>
        {
            var employee = await employeeActorResolver.ResolveAsync(externalId, token);
            var employeeId = employee.EmployeeId ?? throw new ActorForbiddenException();
            var roles = await roleResolver.ResolveAsync(employeeId, token);
            if (roles.EmployeeId != employeeId || !roles.EmployeeExists || !roles.EmployeeIsActive)
                throw new BudgetOwnerCreationAuthorityUnavailableException();
            var canSeeAll = BusinessDataReadPolicy.HasGlobalRole(roles);
            if (!canSeeAll && !roles.HasExactRole(ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)
                && !roles.HasExactRole(ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode))
                throw new ActorForbiddenException();
            return (options is null ? await store.ReadAsync(year, costCenterId, employeeId, canSeeAll, token)
                : await store.ReadPeriodAsync(year, costCenterId, employeeId, canSeeAll, options, token))
                ?? throw new BddsSnapshotNotFoundException();
        }, cancellationToken);
    }

    public async Task<BddsImportResultDto> ImportAsync(
        ImportBddsSnapshotCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var externalId = ActorIdentity.RequireBusinessDataAccess(actor);

        var employee = await employeeActorResolver.ResolveAsync(externalId, cancellationToken);
        var employeeId = employee.EmployeeId ?? throw new ActorForbiddenException();
        var roles = await roleResolver.ResolveAsync(employeeId, cancellationToken);
        if (roles.EmployeeId != employeeId || !roles.EmployeeExists || !roles.EmployeeIsActive
            || (!HasCurrentAdministratorRole(roles)
                && !roles.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode)))
        {
            throw new ActorForbiddenException();
        }
        ValidateSource(command);

        return await transactions.ExecuteAsync(async token =>
        {
            // Repeat authority reads in the same fresh transaction as replay/import.
            // Preflight authority must not survive mapping or role revocation.
            var currentEmployee = await employeeActorResolver.ResolveAsync(externalId, token);
            if (currentEmployee.EmployeeId != employeeId)
                throw new ActorForbiddenException();
            var currentRoles = await roleResolver.ResolveAsync(employeeId, token);
            if (currentRoles.EmployeeId != employeeId
                || !currentRoles.EmployeeExists || !currentRoles.EmployeeIsActive
                || (!HasCurrentAdministratorRole(currentRoles)
                    && !currentRoles.HasExactRole(ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode)))
                throw new ActorForbiddenException();
            token.ThrowIfCancellationRequested();

            var existing = await store.FindImportAsync(command.SourceHash, token);
            if (existing is not null)
            {
                return existing with { AlreadyImported = true };
            }

            return await store.ImportAsync(
                command,
                employeeId,
                externalId,
                RequireUtc(clock.UtcNow),
                token);
        }, cancellationToken);
    }

    private bool HasCurrentAdministratorRole(EmployeeFunctionalRoleResolution resolution) =>
        actor.HasBmsAdministratorAssignment
        && (resolution.HasExactRole("BMS_Администратор") || resolution.HasExactRole("BMS.Administrator"));

    private static void ValidateSource(ImportBddsSnapshotCommand command)
    {
        if (command.SourceVersionDate == default
            || command.BudgetYear != 2026
            || !string.Equals(command.Scenario, "Пессимистичный", StringComparison.Ordinal)
            || command.Rows is null
            || command.Rows.Count is < 1 or > 100_000
            || command.SourceHash is null
            || command.SourceHash.Length != 64
            || !command.SourceHash.All(Uri.IsHexDigit))
        {
            throw new BddsImportConflictException("The source snapshot differs from the approved BDDS control set.");
        }

        var ordinals = new HashSet<int>();
        foreach (var row in command.Rows)
        {
            if (row is null
                || row.SourceOrdinal < 1
                || !ordinals.Add(row.SourceOrdinal)
                || row.BudgetMonth.Year != command.BudgetYear
                || row.BudgetMonth.Day != 1
                || row.SourceRowNumber < 1
                || row.SourceLoadedAtUtc == default
                || row.SourceLoadedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new BddsImportConflictException("A source BDDS row is invalid.");
            }
            ValidateText(row.SourceCostCenterName, 500);
            ValidateText(row.ActivityName, 500);
            ValidateText(row.SectionName, 500);
            ValidateText(row.GroupName, 500);
            ValidateText(row.ArticleBusinessCode, 20);
            ValidateText(row.ArticleName, 500);
            ValidateText(row.SourceFile, 1000);
            ValidateText(row.SourceSheet, 200);
            if (decimal.Round(row.SignedAmountKzt, 2) != row.SignedAmountKzt)
            {
                throw new BddsImportConflictException("BDDS amounts must have cent precision.");
            }
        }

        var hash = BddsCanonicalHash.Compute(command.Rows);
        var receipt = command.Rows.Where(row => row.SignedAmountKzt > 0).Sum(row => row.SignedAmountKzt);
        var payment = -command.Rows.Where(row => row.SignedAmountKzt < 0).Sum(row => row.SignedAmountKzt);
        var net = receipt - payment;
        var cfoCount = command.Rows.Select(row => row.SourceCostCenterName).Distinct(StringComparer.Ordinal).Count();
        var articleCount = command.Rows
            .Select(row => (row.ArticleBusinessCode, row.ArticleName))
            .Distinct().Count();
        var nonZeroCells = command.Rows
            .GroupBy(row => (row.SourceCostCenterName, row.ArticleBusinessCode, row.ArticleName, row.BudgetMonth))
            .Count(group => group.Sum(row => row.SignedAmountKzt) != 0m);
        if (!string.Equals(hash, command.SourceHash, StringComparison.OrdinalIgnoreCase)
            || receipt <= 0m
            || payment <= 0m
            || cfoCount != 12
            || articleCount != 157
            || nonZeroCells < 1
            || net != receipt - payment)
        {
            throw new BddsImportConflictException("The source snapshot control totals do not match the approved BDDS snapshot.");
        }
    }

    private static void ValidateText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maxLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new BddsImportConflictException("A source BDDS text value is invalid.");
        }
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value) =>
        value != default && value.Offset == TimeSpan.Zero
            ? value
            : throw new InvalidOperationException("A UTC clock is required for BDDS import.");
}
