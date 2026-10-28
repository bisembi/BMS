using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class BudgetRequestProjectionConfiguration :
    IEntityTypeConfiguration<BudgetRequestProjection>
{
    public void Configure(EntityTypeBuilder<BudgetRequestProjection> builder)
    {
        builder.ToTable("BudgetRequestProjections", "bms_web");
        builder.HasKey(projection => projection.Id)
            .HasName("PK_bms_web_BudgetRequestProjections");
        builder.Property(projection => projection.BudgetMonth).HasColumnType("date");
        builder.Property(projection => projection.ReservedAmountKzt)
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Ignore(projection => projection.Key);

        builder.HasIndex(projection => projection.ExpenseRequestLineId)
            .IsUnique()
            .HasDatabaseName("UQ_bms_web_BudgetRequestProjections_Line");
        builder.HasIndex(projection => projection.ExpenseRequestId)
            .HasDatabaseName("IX_BudgetRequestProjections_ExpenseRequestId");
        builder.HasIndex(projection => new
        {
            projection.CostCenterId,
            projection.CashFlowItemId,
            projection.BudgetMonth
        })
            .HasDatabaseName("IX_BudgetRequestProjections_BudgetKey")
            .IncludeProperties(projection => new
            {
                projection.ReservedAmountKzt,
                projection.ExpenseRequestId
            });

        builder.HasOne<ExpenseRequest>()
            .WithMany()
            .HasForeignKey(projection => projection.ExpenseRequestId)
            .HasConstraintName("FK_bms_web_BudgetRequestProjections_Request")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ExpenseRequestLine>()
            .WithMany()
            .HasForeignKey(projection => projection.ExpenseRequestLineId)
            .HasConstraintName("FK_bms_web_BudgetRequestProjections_Line")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<BudgetPosition>()
            .WithMany()
            .HasForeignKey(projection => new
            {
                projection.CostCenterId,
                projection.CashFlowItemId,
                projection.BudgetMonth
            })
            .HasConstraintName("FK_bms_web_BudgetRequestProjections_Position")
            .OnDelete(DeleteBehavior.NoAction);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_BudgetRequestProjections_month_start",
                "DAY([BudgetMonth]) = 1");
            table.HasCheckConstraint(
                "CK_bms_web_BudgetRequestProjections_reserved",
                "[ReservedAmountKzt] > 0");
        });
    }
}
