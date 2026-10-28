using System.Text.Json;

namespace Bms.Infrastructure.Persistence;

/// <summary>SQL decimal zero and an absent-position zero can serialize as
/// 0.00 and 0. Preserve identical evidence instead of rewriting a posting cache
/// during a draft save. Different source versions or monetary values never coalesce.</summary>
internal static class BudgetRegisterEvidence350
{
    internal static RegisterSourceValue343 PreserveEquivalent(RegisterSourceValue343? previous,RegisterSourceValue343 current)
    {
        if(previous is null||previous.Kind!=current.Kind||previous.SourceId!=current.SourceId||previous.Key!=current.Key
            ||previous.AmountKzt!=current.AmountKzt||previous.SourceVersion!=current.SourceVersion||previous.Evidence==current.Evidence)return current;
        try
        {
            using var before=JsonDocument.Parse(previous.Evidence);using var after=JsonDocument.Parse(current.Evidence);
            return JsonElement.DeepEquals(before.RootElement,after.RootElement)?current with{Evidence=previous.Evidence}:current;
        }
        catch(JsonException){return current;}
    }
}
