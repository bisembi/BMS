using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class BudgetPositionConfiguration : IEntityTypeConfiguration<BudgetPosition>
{
    public void Configure(EntityTypeBuilder<BudgetPosition> builder)
    {
        builder.ToTable("BudgetPositions", "bms_web");
        builder.HasKey(position => new
        {
            position.CostCenterId,
            position.CashFlowItemId,
            position.BudgetMonth
        }).HasName("PK_bms_web_BudgetPositions");
        builder.Property(position => position.BudgetMonth).HasColumnType("date");
        builder.Property(position => position.LimitAmountKzt).HasPrecision(18, 2);
        builder.Property(position => position.ReservedAmountKzt).HasPrecision(18, 2).IsRequired();
        builder.Property(position => position.PaidAmountKzt).HasPrecision(18, 2).IsRequired();
        builder.Property(position => position.ConcurrencyStamp).IsConcurrencyToken();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_BudgetPositions_month_start",
                "DAY([BudgetMonth]) = 1");
            table.HasCheckConstraint(
                "CK_bms_web_BudgetPositions_limit",
                "[LimitAmountKzt] IS NULL OR [LimitAmountKzt] >= 0");
            table.HasCheckConstraint(
                "CK_bms_web_BudgetPositions_reserved",
                "[ReservedAmountKzt] >= 0");
            table.HasCheckConstraint(
                "CK_bms_web_BudgetPositions_paid",
                "[PaidAmountKzt] >= 0");
        });
        builder.Ignore(position => position.Key);
        builder.Ignore(position => position.AvailableAmountKzt);
        builder.Ignore(position => position.IsOverLimit);
    }
}
