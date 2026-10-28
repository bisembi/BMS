using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Common;
using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExpenseRequestApprovalDetailsCommand(Guid ExpectedVersion, ExpenseRequestApprovalDetails Details);
public sealed record ExpenseRequestApprovalDetailsState(Guid RequestId, bool CanEdit, Guid RequestVersion,
    string? PaymentBasisCode, bool IsNewContract, ExpenseRequestApprovalDetails? Details);
public sealed class ExpenseRequestApprovalDetailsInvalidException(string publicMessage) : Exception(publicMessage);

public sealed partial class ExpenseRequestWorkflowService
{
    public Task<ExpenseRequestApprovalDetailsState> GetApprovalDetailsAsync(Guid id,CancellationToken token) =>
        GetApprovalDetailsAsync(id,false,token);
    public async Task<ExpenseRequestApprovalDetailsState> GetApprovalDetailsAsync(Guid id,bool splitAccruals,CancellationToken token)
    {
        var actor=ActorIdentity.RequireBusinessDataAccess(currentActor);
        var r=await repository.FindVisibleAsync(id,actor,currentActor.HasFullBusinessDataAccess,token)
            ?? throw new ExpenseRequestNotFoundException();
        var can=false;
        try{await RequireApprovalDetailsAuthorityAsync(r,actor,token);can=true;}
        catch(ActorForbiddenException){}
        var details=can?ExpenseRequestApprovalDetails.From(r):null;
        if(details is not null && accrualEditPolicy?.Enabled==false)details=details with{AccrualLines=null};
        else if(details is not null&&splitAccruals&&r.Lines.Count>0&&r.ApprovalAllocationLines().Count==r.Lines.Count)
            details=details with{AccrualLines=null,AllocationLines=r.ApprovalAllocationLines()};
        return new(r.Id,can,r.ConcurrencyStamp,r.PaymentBasisCode?.ToString(),r.IsNewContract,details);
    }

    private async Task RequireApprovalDetailsAuthorityAsync(ExpenseRequest r,string actor,CancellationToken token)
    {
        if(r.PaymentOperation?.IsPayrollAdvance == true || currentActor.IsBmsAdministrator || currentActor.IsBmsIntegrationAdministrator
            || currentActor.ActiveRoleCode is ExpenseRequestWorkflowCodes.AuditorRoleCode or ExpenseRequestWorkflowCodes.NsiSpecialistRoleCode
            || r.Status is not (ExpenseRequestStatus.InApproval or ExpenseRequestStatus.Approved))
            throw new ActorForbiddenException();
        await RequireDocumentReadScopeAsync(r,token);
        var version=initiatorRoutes is null?1:await initiatorRoutes.CurrentVersionAsync(r,token);
        var workflow=await ReadVerifiedWorkflowAsync(token,version);
        var code=r.Status==ExpenseRequestStatus.InApproval?ExpenseRequestWorkflowCodes.ApproveCommandCode:ExpenseRequestWorkflowCodes.SendToPaymentCommandCode;
        var context=await BuildWorkflowContextAsync(r,code,new(r.ConcurrencyStamp),workflow,actor,token);
        if(!WorkflowResolver.AvailableCommands(context,workflow.Permissions,workflow.Commands,workflow.Statuses).Any(c=>c.Code==code && c.IsEnabled))
            throw new ActorForbiddenException();
    }

    public async Task<ExpenseRequestDto?> ResolveApprovalDetailsAsync(Guid id,string key,CancellationToken token)
    {
        var actor=ActorIdentity.RequireBusinessDataAccess(currentActor);ValidateIdempotencyKey(key);
        return await transactionRunner.ExecuteAsync<ExpenseRequestDto?>(async ct=>{
            var r=await repository.FindForWorkflowUpdateAsync(id,ct)??throw new ExpenseRequestNotFoundException();
            await RequireApprovalDetailsAuthorityAsync(r,actor,ct);
            var stored=await idempotencyStore.FindAsync(BuildIdempotencyScope("approval-details",id,actor),key,ct);
            return stored is null?null:await ReadStoredResponseAsync(stored.ResponseJson,r,ct);
        },token);
    }

    public async Task<ExpenseRequestWorkflowCommandResult> UpdateApprovalDetailsAsync(Guid id,
        ExpenseRequestApprovalDetailsCommand command,string key,CancellationToken token)
    {
        var actor=ActorIdentity.RequireBusinessDataAccess(currentActor);
        ArgumentNullException.ThrowIfNull(command);ValidateExpectedVersion(command.ExpectedVersion);ValidateIdempotencyKey(key);
        if(command.Details is null)throw new ExpenseRequestApprovalDetailsInvalidException("Не переданы редактируемые реквизиты заявки.");
        return await transactionRunner.ExecuteAsync(async ct=>{
            var scope=BuildIdempotencyScope("approval-details",id,actor);
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command,JsonOptions))));
            var stored=await idempotencyStore.FindAsync(scope,key,ct);
            if(stored is not null && stored.RequestHash!=hash)throw new IdempotencyConflictException();
            var r=await repository.FindForWorkflowUpdateAsync(id,ct)??throw new ExpenseRequestNotFoundException();
            await RequireApprovalDetailsAuthorityAsync(r,actor,ct);
            if(stored is not null)return new ExpenseRequestWorkflowCommandResult(await ReadStoredResponseAsync(stored.ResponseJson,r,ct),true,stored.ResponseStatusCode);
            if(r.ConcurrencyStamp!=command.ExpectedVersion)throw new ExpenseRequestVersionConflictException();
            ValidateApprovalDetailsInput(r,command.Details);
            if(accrualEditPolicy?.Enabled==false && (command.Details.AccrualLines is not null||command.Details.AllocationLines is not null))
                throw new ExpenseRequestApprovalDetailsInvalidException("Редактирование аналитики начислений временно недоступно.");
            if(command.Details.AccrualLines is { } accrualLines)
            {
                var old=r.Lines.ToDictionary(l=>l.Id);
                var changed=accrualLines.Where(e=>e is not null && (!old.TryGetValue(e.LineId,out var l)
                    || (l.AccrualCostCenterId??l.CostCenterId)!=e.AccrualCostCenterId)).Select(e=>e.AccrualCostCenterId).Distinct().ToArray();
                if(changed.Length>0 && (accrualDepartments is null || !await accrualDepartments.AreActiveAsync(changed,ct)))
                    throw new ExpenseRequestApprovalDetailsInvalidException("Выберите действующее подразделение начисления.");
            }
            if(command.Details.AllocationLines is { } allocations)
            {
                var old=r.Lines.ToDictionary(l=>l.Id);
                var changed=allocations.Where(e=>e is not null&&(!old.TryGetValue(e.SourceLineId,out var l)
                    || (l.AccrualCostCenterId??l.CostCenterId)!=e.AccrualCostCenterId)).Select(e=>e.AccrualCostCenterId).Distinct().ToArray();
                if(changed.Length>0&&(accrualDepartments is null||!await accrualDepartments.AreActiveAsync(changed,ct)))
                    throw new ExpenseRequestApprovalDetailsInvalidException("Выберите действующее подразделение начисления.");
            }
            var facts=attachmentStore is null?SubmissionAttachmentFacts.None:await attachmentStore.ReadSubmissionFactsAsync(id,ct);
            var missing=SubmissionReadinessEvaluator.Evaluate(r,facts).MissingRequirements;
            // No FX refresh, actuals read or route recalculation. A structural
            // split rebinds projection rows only, with exact key totals guarded.
            var before=ExpenseRequestDto.FromDomain(r,[],facts);var now=clock.UtcNow;
            r.UpdateApprovalDetails(command.Details,now);
            if(SubmissionReadinessEvaluator.Evaluate(r,facts).MissingRequirements.Except(missing,StringComparer.Ordinal).Any())
                throw new ExpenseRequestApprovalDetailsInvalidException("Нельзя очистить обязательные реквизиты основания оплаты или договора.");
            if(command.Details.ContractId!=before.ContractId && (contractValidator is null
                || !await contractValidator.MatchesAsync(r.ContractId,r.OrganizationId,r.CounterpartyId,r.CurrencyCode,ct)))
                throw new ExpenseRequestApprovalDetailsInvalidException("Выберите действующий договор этого контрагента, организации и валюты.");
            var beforeLines=before.Lines.ToDictionary(l=>l.Id);
            IReadOnlyList<Bms.Domain.Budgeting.BudgetRequestProjection> projections;
            IReadOnlyList<Bms.Domain.Budgeting.BudgetRequestProjection> originalProjections;
            if(command.Details.AllocationLines is not null && (before.Lines.Count!=r.Lines.Count
                || r.Lines.Any(l=>!beforeLines.TryGetValue(l.Id,out var b)||b.Amount!=l.Amount||b.AmountKzt!=l.AmountKzt)))
            {
                var calculation=Bms.Domain.Budgeting.ExpenseRequestBudgetProjectionCalculator.Calculate(r);
                if(!calculation.IsComplete)throw new ExpenseRequestApprovalDetailsInvalidException("Редактирование аналитики начислений временно недоступно.");
                // The replacement's locked read is the authoritative before image.
                // Never retain projection range locks before the budget app locks.
                originalProjections=await budgetRepository.RedistributeForApprovalAsync(id,calculation.Projections,ct);
                projections=calculation.Projections;
            }
            else originalProjections=projections=await budgetRepository.ListRequestProjectionsAsync([id],ct);
            before=before with{BudgetReservation=before.BudgetReservation with{
                ReservedAmountKzt=originalProjections.Sum(p=>p.ReservedAmountKzt),
                Positions=originalProjections.Select(p=>new BudgetReservationPositionDto(p.ExpenseRequestLineId,p.CostCenterId,p.CashFlowItemId,p.BudgetMonth,p.ReservedAmountKzt)).ToArray()}};
            var response=ExpenseRequestDto.FromDomain(r,projections,facts);
            var json=JsonSerializer.Serialize(response,JsonOptions);var correlation=Guid.NewGuid().ToString("N");
            auditSink.Append(Guid.NewGuid(),now,actor,"ExpenseRequest",id.ToString("D"),"ApprovalDetailsUpdated",
                JsonSerializer.Serialize(before,JsonOptions),json,correlation);
            outboxSink.Enqueue(Guid.NewGuid(),now,"expense-request.approval-details-updated.v1",json,correlation);
            idempotencyStore.Add(scope,key,hash,StatusOk,json,now);
            await unitOfWork.SaveChangesAsync(ct);
            return new ExpenseRequestWorkflowCommandResult(response,false,StatusOk);
        },token);
    }

    private static void ValidateApprovalDetailsInput(ExpenseRequest r,ExpenseRequestApprovalDetails d)
    {
        foreach(var (text,max,label) in new (string?,int,string)[]{(d.Purpose,1000,"Назначение платежа"),(d.Comment,1000,"Комментарий"),
            (d.BusinessJustification,1000,"Обоснование"),(d.UrgencyReason,1000,"Причина срочности"),(d.InvoiceNumber,100,"Номер счёта"),
            (d.PublicOfferName,300,"Наименование оферты"),(d.PublicOfferIdentifier,1000,"Идентификатор оферты"),
            (d.OtherBasisExplanation,1000,"Иное основание"),(d.CandidateContractNumber,100,"Номер договора"),
            (d.CandidateContractSubject,300,"Предмет договора"),(d.CandidateContractTerms,1000,"Условия оплаты")})
            if(text is not null && (text.Length>max || text.Any(c=>char.IsControl(c)&&c is not '\r' and not '\n')))
                throw new ExpenseRequestApprovalDetailsInvalidException($"Поле «{label}»: не более {max} символов, без управляющих знаков.");
        if(string.IsNullOrWhiteSpace(d.Purpose))throw new ExpenseRequestApprovalDetailsInvalidException("Заполните назначение платежа.");
        if(d.IsUrgent && string.IsNullOrWhiteSpace(d.UrgencyReason))throw new ExpenseRequestApprovalDetailsInvalidException("Укажите причину срочности.");
        if(!string.IsNullOrWhiteSpace(r.BusinessJustification)&&string.IsNullOrWhiteSpace(d.BusinessJustification))
            throw new ExpenseRequestApprovalDetailsInvalidException("Нельзя удалить обоснование заявки.");
    }
}
