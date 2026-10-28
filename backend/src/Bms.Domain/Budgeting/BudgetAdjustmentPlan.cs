using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

public sealed record BudgetAdjustmentTransfer(Guid SourceArticleId, DateOnly SourceMonth,
    Guid TargetArticleId, DateOnly TargetMonth, decimal AmountKzt,
    BudgetFlowDirection FlowDirection = BudgetFlowDirection.Payment);
public sealed record BudgetAdjustmentCell(Guid ArticleId, DateOnly Month, BudgetFlowDirection Direction, decimal AmountKzt);
public sealed record BudgetAdjustmentUsage(Guid ArticleId, DateOnly Month, decimal ReservedKzt, decimal PaidKzt,
    BudgetFlowDirection Direction = BudgetFlowDirection.Payment);
public sealed class BudgetAdjustmentRuleException(string message) : Exception(message);

/// Balanced transfers only. Consumption is never rewritten or released.
public static class BudgetAdjustmentPlan
{
    public static IReadOnlyList<BudgetAdjustmentCell> Apply(DateOnly from, DateOnly to,
        IReadOnlyCollection<BudgetAdjustmentCell> original, IReadOnlyCollection<BudgetAdjustmentTransfer> transfers,
        IReadOnlyCollection<BudgetAdjustmentUsage>? consumption = null)
    {
        BudgetInstance.EnsureValidityPeriod(from, to);
        if (transfers is null || transfers.Count is < 1 or > 200)
            throw new BudgetAdjustmentRuleException("Добавьте от 1 до 200 строк переноса.");
        var cells = new Dictionary<(Guid, DateOnly, BudgetFlowDirection), decimal>();
        foreach (var cell in original)
        {
            if (cell.ArticleId == Guid.Empty || cell.Month.Day != 1 || cell.Month < from || cell.Month > to || !Enum.IsDefined(cell.Direction))
                throw new BudgetAdjustmentRuleException("Состав исходного бюджета некорректен.");
            MoneyAmountPolicy.EnsureNonNegative(cell.AmountKzt, "Исходный бюджет");
            var k = (cell.ArticleId, cell.Month, cell.Direction);
            cells[k] = checked(cells.GetValueOrDefault(k) + cell.AmountKzt);
            MoneyAmountPolicy.EnsureRepresentable(cells[k], "Исходный бюджет");
        }
        var outgoing = new Dictionary<(Guid, DateOnly, BudgetFlowDirection), decimal>();
        foreach (var move in transfers)
        {
            if (move is null || move.SourceArticleId == Guid.Empty || move.TargetArticleId == Guid.Empty
                || !Enum.IsDefined(move.FlowDirection) || move.SourceMonth.Day != 1 || move.TargetMonth.Day != 1
                || move.SourceMonth < from || move.SourceMonth > to || move.TargetMonth < from || move.TargetMonth > to)
                throw new BudgetAdjustmentRuleException("Выберите статьи и месяцы в пределах периода исходного экземпляра.");
            // Exactly one dimension changes: article OR month, never both.
            if ((move.SourceArticleId == move.TargetArticleId) == (move.SourceMonth == move.TargetMonth))
                throw new BudgetAdjustmentRuleException("Меняйте либо месяц той же статьи, либо статью в том же месяце.");
            if (move.AmountKzt <= 0) throw new BudgetAdjustmentRuleException("Сумма переноса должна быть больше нуля.");
            MoneyAmountPolicy.EnsureRepresentable(move.AmountKzt, "Сумма переноса");
            var k = (move.SourceArticleId, move.SourceMonth, move.FlowDirection);
            if (!cells.ContainsKey(k)) throw new BudgetAdjustmentRuleException("Статья-источник отсутствует в действующем составе экземпляра.");
            outgoing[k] = checked(outgoing.GetValueOrDefault(k) + move.AmountKzt);
        }
        foreach (var (key, amount) in outgoing)
        {
            var used = 0m;
            if (consumption is not null)
            {
                var matches = consumption.Where(c => c.ArticleId == key.Item1 && c.Month == key.Item2 && c.Direction == key.Item3).ToArray();
                if (matches.Length != 1 || matches[0].ReservedKzt < 0 || matches[0].PaidKzt < 0)
                    throw new BudgetAdjustmentRuleException("Не удалось подтвердить факт и резервы источника. Повторите обновление.");
                used = checked(matches[0].ReservedKzt + matches[0].PaidKzt);
            }
            var free = Math.Max(0m, cells[key] - used);
            // Incoming transfers cannot finance another outgoing row in this document.
            if (amount > free)
                throw new BudgetAdjustmentRuleException($"Недостаточно свободного остатка за {key.Item2:MM.yyyy}: доступно {free:N2} ₸, перенос {amount:N2} ₸. Факт и резервы не изменяются.");
        }
        var result = new Dictionary<(Guid, DateOnly, BudgetFlowDirection), decimal>(cells);
        foreach (var move in transfers)
        {
            var source = (move.SourceArticleId, move.SourceMonth, move.FlowDirection);
            var target = (move.TargetArticleId, move.TargetMonth, move.FlowDirection);
            result[source] -= move.AmountKzt;
            result[target] = checked(result.GetValueOrDefault(target) + move.AmountKzt);
            MoneyAmountPolicy.EnsureRepresentable(result[target], "Скорректированный бюджет");
        }
        foreach (var direction in Enum.GetValues<BudgetFlowDirection>())
            if (result.Where(p => p.Key.Item3 == direction).Sum(p => p.Value) != cells.Where(p => p.Key.Item3 == direction).Sum(p => p.Value))
                throw new BudgetAdjustmentRuleException("Нарушен баланс переноса.");
        return result.OrderBy(p => p.Key.Item2).ThenBy(p => p.Key.Item1).ThenBy(p => p.Key.Item3)
            .Select(p => new BudgetAdjustmentCell(p.Key.Item1, p.Key.Item2, p.Key.Item3, p.Value)).ToArray();
    }
    public static IReadOnlyList<BudgetAdjustmentCell> Cells(BudgetInstance instance) =>
        instance.Lines.GroupBy(l => (l.CashFlowItemId, l.BudgetMonth, l.FlowDirection))
            .Select(g => new BudgetAdjustmentCell(g.Key.CashFlowItemId, g.Key.BudgetMonth, g.Key.FlowDirection, g.Sum(l => l.LimitAmountKzt)))
            .OrderBy(c => c.Month).ThenBy(c => c.ArticleId).ThenBy(c => c.Direction).ToArray();
}
