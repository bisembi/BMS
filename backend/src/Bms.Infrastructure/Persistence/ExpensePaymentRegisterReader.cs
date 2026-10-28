using System.Globalization;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed class ExpensePaymentRegisterReader(BmsDbContext db, ICurrentActor actor,
    IBusinessTransactionRunner transactions, IEmployeeActorDisplayNamesReader names, IClock clock) : IExpensePaymentRegisterReader
{
    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    public Task<ExpensePaymentRegisterDto> ReadAsync(CancellationToken token)
    {
        var identity=ActorIdentity.RequireBusinessDataAccess(actor);
        return transactions.ExecuteAsync(async ct => {
            var scope=await BusinessDataReadScopeQuery.ResolveAsync(db,identity,ct);
            if(!scope.HasAny) throw new ActorForbiddenException();
            var query=ExpenseRequestVisibilityQuery.ApplyCapturedListScope(db,db.ExpenseRequests.AsNoTracking(),identity,scope,false)
                .Where(request=>request.Status==ExpenseRequestStatus.ToPay);
            // One Serializable capture, no UI filters, no paging and no stale catalogue cache.
            // Inactive references retain their real names on existing documents.
            var records=await query.OrderBy(request=>request.PlannedPaymentDate).ThenBy(request=>request.RequestNumber).ThenBy(request=>request.Id)
                .Select(request=>new {
                    request.Id,request.PlannedPaymentDate,request.InitiatorExternalId,request.CurrencyCode,request.PaymentOperation,
                    Organization=db.Organizations.Where(row=>row.Id==request.OrganizationId).Select(row=>row.Name).SingleOrDefault(),
                    CostCenter=db.CostCenters.Where(row=>row.Id==request.DefaultCostCenterId).Select(row=>row.Name).SingleOrDefault(),
                    Counterparty=db.Counterparties.Where(row=>row.Id==request.CounterpartyId).Select(row=>row.Name).SingleOrDefault(),
                    Amount=request.Lines.Sum(line=>line.Amount),
                    Equivalent=request.Lines.Any()&&request.Lines.All(line=>line.AmountKzt.HasValue)
                        ? request.Lines.Sum(line=>line.AmountKzt) : null
                }).ToArrayAsync(ct);
            var authors=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var batch in records.Select(row=>row.InitiatorExternalId).Distinct(StringComparer.Ordinal).Chunk(100)) {
                var mapped=await names.ReadAsync(batch,ct);
                foreach(var id in batch) {
                    var matches=mapped.Where(row=>row.ActorExternalId==id&&row.MappingIsActive&&row.EmployeeIsActive&&!string.IsNullOrWhiteSpace(row.EmployeeName)).ToArray();
                    if(matches.Length==1) authors[id]=matches[0].EmployeeName??"Инициатор не определён";
                }
            }
            var items=records.Select(row=>new ExpensePaymentRegisterRow(row.Id,row.Organization??"Не указана",row.PlannedPaymentDate,
                row.CostCenter??"Не указано",authors.GetValueOrDefault(row.InitiatorExternalId,"Инициатор не определён"),
                row.PaymentOperation is { IsBusinessTrip: true, EmployeeNames: { Count: > 0 } employees }
                    ? string.Join("; ",employees) : row.Counterparty??"Не указан",row.CurrencyCode,Money(row.Amount),row.Equivalent is decimal value?Money(value):null)).ToArray();
            var totals=records.GroupBy(row=>row.CurrencyCode,StringComparer.Ordinal).OrderBy(group=>group.Key,StringComparer.Ordinal)
                .Select(group=>new ExpensePaymentRegisterCurrencyTotal(group.Key,Money(group.Sum(row=>row.Amount)))).ToArray();
            var unknown=records.Any(row=>row.Equivalent is null);
            return new ExpensePaymentRegisterDto(clock.UtcNow,items,totals,
                unknown?null:Money(records.Sum(row=>row.Equivalent??0m)),unknown);
        },token);
    }
}
