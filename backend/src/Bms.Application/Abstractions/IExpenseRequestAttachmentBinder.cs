namespace Bms.Application.Abstractions;

public interface IExpenseRequestAttachmentBinder
{
    Task BindAsync(Guid sessionId, Guid requestId, string actorExternalId, CancellationToken token);
}
