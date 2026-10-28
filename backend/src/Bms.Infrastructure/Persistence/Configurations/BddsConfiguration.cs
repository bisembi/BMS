using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class BddsSnapshotConfiguration : IEntityTypeConfiguration<BddsSnapshot>
{
    public void Configure(EntityTypeBuilder<BddsSnapshot> builder)
    {
        builder.ToTable("BddsSnapshots", "bms_web", table => table.UseSqlOutputClause(false));
        builder.HasKey(row => row.Id).HasName("PK_bms_web_BddsSnapshots");
        builder.Property(row => row.SourceVersionDate).HasColumnType("date").IsRequired();
        builder.Property(row => row.Scenario).HasMaxLength(100).IsRequired();
        builder.Property(row => row.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(row => row.SourceTable).HasMaxLength(128).IsRequired();
        builder.Property(row => row.SourceHash).HasMaxLength(64).IsUnicode(false).IsFixedLength()
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(row => row.ReceiptsAmountKzt).HasPrecision(19, 2).IsRequired();
        builder.Property(row => row.PaymentsAmountKzt).HasPrecision(19, 2).IsRequired();
        builder.Property(row => row.NetCashFlowAmountKzt).HasPrecision(19, 2).IsRequired();
        builder.Property(row => row.CreatedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(row => row.ApprovedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(row => row.ApprovedByExternalId).HasMaxLength(256)
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(row => row.IsSealed).HasDefaultValue(false).IsRequired();
        builder.HasIndex(row => row.SourceHash).IsUnique()
            .HasDatabaseName("UX_bms_web_BddsSnapshots_SourceHash");
        builder.HasIndex(row => new { row.BudgetYear, row.ApprovedAtUtc })
            .HasDatabaseName("IX_bms_web_BddsSnapshots_Year_ApprovedAtUtc");
    }
}

public sealed class BddsSnapshotRowConfiguration : IEntityTypeConfiguration<BddsSnapshotRow>
{
    public void Configure(EntityTypeBuilder<BddsSnapshotRow> builder)
    {
        builder.ToTable("BddsSnapshotRows", "bms_web", table => table.UseSqlOutputClause(false));
        builder.HasKey(row => row.Id).HasName("PK_bms_web_BddsSnapshotRows");
        builder.Property(row => row.Id).ValueGeneratedOnAdd();
        builder.Property(row => row.SourceCostCenterName).HasMaxLength(500).IsRequired();
        builder.Property(row => row.ActivityName).HasMaxLength(500).IsRequired();
        builder.Property(row => row.SectionName).HasMaxLength(500).IsRequired();
        builder.Property(row => row.GroupName).HasMaxLength(500).IsRequired();
        builder.Property(row => row.ArticleBusinessCode).HasMaxLength(20)
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(row => row.ArticleName).HasMaxLength(500).IsRequired();
        builder.Property(row => row.BudgetMonth).HasColumnType("date").IsRequired();
        builder.Property(row => row.SignedAmountKzt).HasPrecision(19, 2).IsRequired();
        builder.Property(row => row.SourceFile).HasMaxLength(1000).IsRequired();
        builder.Property(row => row.SourceSheet).HasMaxLength(200).IsRequired();
        builder.Property(row => row.SourceLoadedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(row => row.RowHash).HasMaxLength(64).IsUnicode(false).IsFixedLength()
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.HasIndex(row => new { row.SnapshotId, row.SourceOrdinal }).IsUnique()
            .HasDatabaseName("UX_bms_web_BddsSnapshotRows_Snapshot_Ordinal");
        builder.HasIndex(row => new { row.SnapshotId, row.MappedCostCenterId, row.BudgetMonth })
            .HasDatabaseName("IX_bms_web_BddsSnapshotRows_Report");
        builder.HasOne<BddsSnapshot>().WithMany().HasForeignKey(row => row.SnapshotId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_bms_web_BddsSnapshotRows_Snapshot");
        builder.HasOne<CostCenterReference>().WithMany().HasForeignKey(row => row.MappedCostCenterId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_bms_web_BddsSnapshotRows_CostCenter");
        builder.HasOne<CashFlowItemReference>().WithMany().HasForeignKey(row => row.CashFlowItemId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_bms_web_BddsSnapshotRows_CashFlowItem");
    }
}

public sealed class BddsSnapshotBudgetInstanceConfiguration : IEntityTypeConfiguration<BddsSnapshotBudgetInstance>
{
    public void Configure(EntityTypeBuilder<BddsSnapshotBudgetInstance> builder)
    {
        builder.ToTable("BddsSnapshotBudgetInstances", "bms_web",
            table => table.UseSqlOutputClause(false));
        builder.HasKey(row => new { row.SnapshotId, row.BudgetInstanceId })
            .HasName("PK_bms_web_BddsSnapshotBudgetInstances");
        builder.Property(row => row.SourceCostCenterName).HasMaxLength(500).IsRequired();
        builder.HasIndex(row => new { row.SnapshotId, row.CostCenterId }).IsUnique()
            .HasDatabaseName("UX_bms_web_BddsSnapshotBudgetInstances_Snapshot_CostCenter");
        builder.HasOne<BddsSnapshot>().WithMany().HasForeignKey(row => row.SnapshotId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_bms_web_BddsSnapshotBudgetInstances_Snapshot");
        builder.HasOne<Bms.Domain.Budgeting.BudgetInstance>().WithMany()
            .HasForeignKey(row => row.BudgetInstanceId).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_bms_web_BddsSnapshotBudgetInstances_Instance");
        builder.HasOne<CostCenterReference>().WithMany().HasForeignKey(row => row.CostCenterId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_bms_web_BddsSnapshotBudgetInstances_CostCenter");
    }
}
