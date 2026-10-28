using Bms.Application.Budgeting;

namespace Bms.Application.Abstractions;

public interface IUnbudgetedArticlesStore
{
    Task<UnbudgetedArticlesReportDto> ReadAsync(UnbudgetedArticlesOptions options,
        Guid employeeId, bool canSeeAll, CancellationToken cancellationToken);
}
