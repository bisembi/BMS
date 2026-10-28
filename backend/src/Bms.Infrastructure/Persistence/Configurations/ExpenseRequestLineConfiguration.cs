using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class ExpenseRequestLineConfiguration : IEntityTypeConfiguration<ExpenseRequestLine>
{
    public void Configure(EntityTypeBuilder<ExpenseRequestLine> builder)
    {
        builder.ToTable("ExpenseRequestLines", "bms_web");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.AccrualCostCenterId).IsRequired(false);
        builder.HasOne<CostCenterReference>().WithMany()
            .HasForeignKey(line => line.AccrualCostCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(line => line.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(line => line.VatRateCode)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(line => line.VatAmount).HasPrecision(18, 2);
        builder.Property(line => line.AmountExcludingVat).HasPrecision(18, 2);
        builder.Property(line => line.AmountKzt).HasPrecision(18, 2).IsRequired(false);
        builder.Property(line => line.VatAmountKzt).HasPrecision(18, 2).IsRequired(false);
        builder.Property(line => line.AmountExcludingVatKzt).HasPrecision(18, 2).IsRequired(false);
        builder.Property(line => line.AccrualSnapshotFormsManagerialAccrual)
            .HasColumnType("bit")
            .IsRequired(false);
        builder.Property(line => line.AccrualSnapshotExpenseItemId)
            .HasColumnType("uniqueidentifier")
            .IsRequired(false);
        builder.Property(line => line.ConfirmedClosedAmount)
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(line => line.ConfirmedClosedAmountKzt)
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(line => line.SortOrder).IsRequired();
        builder.HasIndex(line => new { line.ExpenseRequestId, line.SortOrder })
            .IsUnique()
            .HasDatabaseName("UX_bms_web_ExpenseRequestLines_Request_SortOrder");
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_bms_web_ExpenseRequestLines_SortOrder_Positive",
            "[SortOrder] > 0"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ExpenseRequestLines_CalculatedAmounts",
            "CASE WHEN " +
            "(([VatRateCode] IS NULL AND [VatAmount] IS NULL " +
            "AND [AmountExcludingVat] IS NULL) " +
            "OR ([VatRateCode] = N'WithoutVat' AND [VatAmount] = 0 " +
            "AND [AmountExcludingVat] = [Amount]) " +
            "OR ([VatRateCode] = N'Vat16' " +
            "AND [VatAmount] = ROUND([Amount] * 16 / 116, 2) " +
            "AND [AmountExcludingVat] = [Amount] - [VatAmount])) " +
            "AND (([AmountKzt] IS NULL AND [VatAmountKzt] IS NULL " +
            "AND [AmountExcludingVatKzt] IS NULL) " +
            "OR ([AmountKzt] IS NOT NULL AND [AmountKzt] >= 0 AND " +
            "(([VatRateCode] IS NULL AND [VatAmountKzt] IS NULL " +
            "AND [AmountExcludingVatKzt] IS NULL) " +
            "OR ([VatRateCode] = N'WithoutVat' AND [VatAmountKzt] = 0 " +
            "AND [AmountExcludingVatKzt] = [AmountKzt]) " +
            "OR ([VatRateCode] = N'Vat16' AND [VatAmountKzt] >= 0 " +
            "AND [AmountExcludingVatKzt] >= 0 " +
            "AND [VatAmountKzt] + [AmountExcludingVatKzt] = [AmountKzt])))) " +
            "THEN 1 ELSE 0 END = 1"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_bms_web_ExpenseRequestLines_AccrualSnapshot",
            "CASE " +
            "WHEN [AccrualSnapshotFormsManagerialAccrual] IS NULL " +
            "AND [AccrualSnapshotExpenseItemId] IS NULL THEN 1 " +
            "WHEN [AccrualSnapshotFormsManagerialAccrual] = 0 " +
            "AND [AccrualSnapshotExpenseItemId] IS NULL THEN 1 " +
            "WHEN [AccrualSnapshotFormsManagerialAccrual] = 1 " +
            "AND [AccrualSnapshotExpenseItemId] IS NOT NULL " +
            "AND [AccrualSnapshotExpenseItemId] <> " +
            "CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000') " +
            "THEN 1 ELSE 0 END = 1"));
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_bms_web_ExpenseRequestLines_ConfirmedClosing",
            "CASE " +
            "WHEN [ConfirmedClosedAmount] = 0 " +
            "AND [ConfirmedClosedAmountKzt] = 0 THEN 1 " +
            "WHEN [AccrualSnapshotFormsManagerialAccrual] = 1 " +
            "AND [AccrualSnapshotExpenseItemId] IS NOT NULL " +
            "AND [AmountKzt] IS NOT NULL " +
            "AND [ConfirmedClosedAmount] > 0 " +
            "AND [ConfirmedClosedAmount] <= [Amount] " +
            "AND [ConfirmedClosedAmountKzt] >= 0 " +
            "AND [ConfirmedClosedAmountKzt] <= [AmountKzt] " +
            "AND ([ConfirmedClosedAmount] <> [Amount] " +
            "OR [ConfirmedClosedAmountKzt] = [AmountKzt]) " +
            "THEN 1 ELSE 0 END = 1"));
    }
}
