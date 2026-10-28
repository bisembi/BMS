using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

internal static class ExpenseRequestInitiatorFilterQuery
{
    // Intersect the existing authorized query BEFORE counting, paging or export.
    // An employee without an account matches no requests, never the whole list.
    internal static IQueryable<ExpenseRequest> Apply(BmsDbContext db,
        IQueryable<ExpenseRequest> requests, Guid? employeeId) => employeeId is null ? requests
        : requests.Where(request => db.EmployeeActorMappings.Any(mapping => mapping.EmployeeId == employeeId
            && mapping.IsActive && EF.Functions.DataLength(mapping.ActorExternalId) == EF.Functions.DataLength(request.InitiatorExternalId)
            && EF.Functions.Collate(mapping.ActorExternalId, "Latin1_General_100_BIN2")
                == EF.Functions.Collate(request.InitiatorExternalId, "Latin1_General_100_BIN2")));
}
