using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class PersistedAccrualClosingCycleSnapshotConfiguration
    : IEntityTypeConfiguration<PersistedAccrualClosingCycleSnapshot>
{
    public void Configure(EntityTypeBuilder<PersistedAccrualClosingCycleSnapshot> builder)
    {
        builder.ToTable("ExpenseRequestAccrualClosingCycles", "bms_web", table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_ExpenseRequestAccrualClosingCycles_Identity",
                "[CycleId] <> CONVERT(uniqueidentifier, " +
                "'00000000-0000-0000-0000-000000000000') " +
                "AND [CycleNumber] > 0 " +
                "AND [RequestVersion] <> CONVERT(uniqueidentifier, " +
                "'00000000-0000-0000-0000-000000000000')");
            table.HasCheckConstraint(
                "CK_bms_web_ExpenseRequestAccrualClosingCycles_Amounts",
                "[GrossAmount] >= 0 AND [VatAmount] >= 0 " +
                "AND [AmountExcludingVat] >= 0 " +
                "AND [VatAmount] + [AmountExcludingVat] = [GrossAmount] " +
                "AND [GrossAmountKzt] >= 0 AND [VatAmountKzt] >= 0 " +
                "AND [AmountExcludingVatKzt] >= 0 " +
                "AND [VatAmountKzt] + [AmountExcludingVatKzt] = [GrossAmountKzt]");
            table.HasCheckConstraint(
                "CK_bms_web_ExpenseRequestAccrualClosingCycles_Time",
                "DATEPART(TZOFFSET, [SourceUpdatedAtUtc]) = 0 " +
                "AND DATEPART(TZOFFSET, [CapturedAtUtc]) = 0 " +
                "AND [CapturedAtUtc] >= [SourceUpdatedAtUtc]");
        });
        builder.HasKey(snapshot => new { snapshot.ExpenseRequestId, snapshot.CycleId })
            .HasName("PK_bms_web_ExpenseRequestAccrualClosingCycles")
            .IsClustered();
        builder.Property(snapshot => snapshot.SourceUpdatedAtUtc)
            .HasColumnType("datetimeoffset(3)");
        builder.Property(snapshot => snapshot.CapturedAtUtc)
            .HasColumnType("datetimeoffset(3)");
        builder.Property(snapshot => snapshot.GrossAmount).HasPrecision(18, 2);
        builder.Property(snapshot => snapshot.VatAmount).HasPrecision(18, 2);
        builder.Property(snapshot => snapshot.AmountExcludingVat).HasPrecision(18, 2);
        builder.Property(snapshot => snapshot.GrossAmountKzt).HasPrecision(18, 2);
        builder.Property(snapshot => snapshot.VatAmountKzt).HasPrecision(18, 2);
        builder.Property(snapshot => snapshot.AmountExcludingVatKzt).HasPrecision(18, 2);
        builder.HasIndex(snapshot => new { snapshot.ExpenseRequestId, snapshot.CycleNumber })
            .IsUnique()
            .HasDatabaseName("UQ_bms_web_ExpenseRequestAccrualClosingCycles_Request_Number");
        builder.HasIndex(snapshot => snapshot.CycleId)
            .IsUnique()
            .HasDatabaseName("UQ_bms_web_ExpenseRequestAccrualClosingCycles_CycleId");
        builder.HasIndex(snapshot => snapshot.CapturedAtUtc)
            .HasDatabaseName("IX_bms_web_ExpenseRequestAccrualClosingCycles_CapturedAtUtc");
        builder.HasIndex(snapshot => snapshot.OrganizationId)
            .HasDatabaseName("IX_bms_web_ExpenseRequestAccrualClosingCycles_Organization");
        builder.HasIndex(snapshot => snapshot.CostCenterId)
            .HasDatabaseName("IX_bms_web_ExpenseRequestAccrualClosingCycles_CostCenter");
        builder.HasOne<ExpenseRequest>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.ExpenseRequestId)
            .HasConstraintName(
                "FK_bms_web_ExpenseRequestAccrualClosingCycles_ExpenseRequest")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrganizationReference>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.OrganizationId)
            .HasConstraintName(
                "FK_bms_web_ExpenseRequestAccrualClosingCycles_Organization")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CostCenterReference>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.CostCenterId)
            .HasConstraintName(
                "FK_bms_web_ExpenseRequestAccrualClosingCycles_CostCenter")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PersistedAccrualClosingCycleLineConfiguration
    : IEntityTypeConfiguration<PersistedAccrualClosingCycleLine>
{
    public void Configure(EntityTypeBuilder<PersistedAccrualClosingCycleLine> builder)
    {
        builder.ToTable("ExpenseRequestAccrualClosingCycleLines", "bms_web", table =>
        {
            table.HasCheckConstraint(
                "CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Identity",
                "[LineId] <> CONVERT(uniqueidentifier, " +
                "'00000000-0000-0000-0000-000000000000') " +
                "AND [SortOrder] > 0 " +
                "AND [CashFlowItemId] <> CONVERT(uniqueidentifier, " +
                "'00000000-0000-0000-0000-000000000000') " +
                "AND [CostCenterId] <> CONVERT(uniqueidentifier, " +
                "'00000000-0000-0000-0000-000000000000') " +
                "AND [ExpenseItemId] <> CONVERT(uniqueidentifier, " +
                "'00000000-0000-0000-0000-000000000000')");
            table.HasCheckConstraint(
                "CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Period",
                "DAY([AccrualPeriod]) = 1");
            table.HasCheckConstraint(
                "CK_bms_web_ExpenseRequestAccrualClosingCycleLines_Amounts",
                "[GrossAmount] >= 0 AND [VatAmount] >= 0 " +
                "AND [AmountExcludingVat] >= 0 " +
                "AND [VatAmount] + [AmountExcludingVat] = [GrossAmount] " +
                "AND [GrossAmountKzt] >= 0 AND [VatAmountKzt] >= 0 " +
                "AND [AmountExcludingVatKzt] >= 0 " +
                "AND [VatAmountKzt] + [AmountExcludingVatKzt] = [GrossAmountKzt]");
        });
        builder.HasKey(line => new { line.ExpenseRequestId, line.CycleId, line.LineId })
            .HasName("PK_bms_web_ExpenseRequestAccrualClosingCycleLines")
            .IsClustered();
        builder.Property(line => line.AccrualPeriod).HasColumnType("date");
        builder.Property(line => line.GrossAmount).HasPrecision(18, 2);
        builder.Property(line => line.VatAmount).HasPrecision(18, 2);
        builder.Property(line => line.AmountExcludingVat).HasPrecision(18, 2);
        builder.Property(line => line.GrossAmountKzt).HasPrecision(18, 2);
        builder.Property(line => line.VatAmountKzt).HasPrecision(18, 2);
        builder.Property(line => line.AmountExcludingVatKzt).HasPrecision(18, 2);
        builder.HasIndex(line => new
        {
            line.ExpenseRequestId,
            line.CycleId,
            line.SortOrder
        }).IsUnique().HasDatabaseName(
            "UQ_bms_web_ExpenseRequestAccrualClosingCycleLines_Cycle_SortOrder");
        builder.HasIndex(line => new { line.ExpenseItemId, line.AccrualPeriod })
            .HasDatabaseName(
                "IX_bms_web_ExpenseRequestAccrualClosingCycleLines_ExpenseItem_Period");
        builder.HasIndex(line => line.CashFlowItemId)
            .HasDatabaseName(
                "IX_bms_web_ExpenseRequestAccrualClosingCycleLines_CashFlowItem");
        builder.HasIndex(line => line.CostCenterId)
            .HasDatabaseName(
                "IX_bms_web_ExpenseRequestAccrualClosingCycleLines_CostCenter");
        builder.HasOne<PersistedAccrualClosingCycleSnapshot>()
            .WithMany()
            .HasForeignKey(line => new { line.ExpenseRequestId, line.CycleId })
            .HasConstraintName(
                "FK_bms_web_ExpenseRequestAccrualClosingCycleLines_Cycle")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CashFlowItemReference>()
            .WithMany()
            .HasForeignKey(line => line.CashFlowItemId)
            .HasConstraintName(
                "FK_bms_web_ExpenseRequestAccrualClosingCycleLines_CashFlowItem")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CostCenterReference>()
            .WithMany()
            .HasForeignKey(line => line.CostCenterId)
            .HasConstraintName(
                "FK_bms_web_ExpenseRequestAccrualClosingCycleLines_CostCenter")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
