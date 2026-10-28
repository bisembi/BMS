using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class CashFlowActualsSnapshotConfiguration : IEntityTypeConfiguration<CashFlowActualsSnapshot>
{
    public void Configure(EntityTypeBuilder<CashFlowActualsSnapshot> builder)
    {
        builder.ToTable("CashFlowActualsSnapshots", "bms_web", table => table.UseSqlOutputClause(false));
        builder.HasKey(row => row.Id).HasName("PK_bms_web_CashFlowActualsSnapshots");
        builder.Property(row => row.SourceHash).HasMaxLength(64).IsFixedLength().IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        builder.Property(row => row.SourceGeneration).HasMaxLength(64).IsFixedLength().IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        builder.Property(row => row.CapturedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(row => row.ImportedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(row => row.ImportedByExternalId).HasMaxLength(256).UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(row => row.ReceiptsAmountKzt).HasPrecision(19, 2);
        builder.Property(row => row.PaymentsAmountKzt).HasPrecision(19, 2);
        builder.Property(row => row.NetCashFlowAmountKzt).HasPrecision(19, 2);
        builder.HasIndex(row => row.SourceHash).IsUnique().HasDatabaseName("UX_bms_web_CashFlowActualsSnapshots_Hash");
        builder.HasIndex(row => new { row.SourceYear, row.IsSealed, row.ImportedAtUtc }).HasDatabaseName("IX_bms_web_CashFlowActualsSnapshots_Latest");
        builder.HasOne<EmployeeReference>().WithMany().HasForeignKey(row => row.ImportedByEmployeeId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CashFlowActualsMonthConfiguration : IEntityTypeConfiguration<CashFlowActualsMonth>
{
    public void Configure(EntityTypeBuilder<CashFlowActualsMonth> builder)
    {
        builder.ToTable("CashFlowActualsMonths", "bms_web", table => table.UseSqlOutputClause(false));
        builder.HasKey(row => new { row.SnapshotId, row.BudgetMonth }).HasName("PK_bms_web_CashFlowActualsMonths");
        builder.Property(row => row.BudgetMonth).HasColumnType("date");
        builder.HasOne<CashFlowActualsSnapshot>().WithMany().HasForeignKey(row => row.SnapshotId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CashFlowActualsArticleConfiguration : IEntityTypeConfiguration<CashFlowActualsArticle>
{
    public void Configure(EntityTypeBuilder<CashFlowActualsArticle> builder)
    {
        builder.ToTable("CashFlowActualsArticles", "bms_web", table => table.UseSqlOutputClause(false));
        builder.HasKey(row => new { row.SnapshotId, row.ArticleOrdinal }).HasName("PK_bms_web_CashFlowActualsArticles");
        builder.Property(row => row.ArticleName).HasMaxLength(500).UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.HasIndex(row => new { row.SnapshotId, row.ArticleName }).IsUnique().HasDatabaseName("UX_bms_web_CashFlowActualsArticles_Name");
        builder.HasIndex(row => new { row.SnapshotId, row.CashFlowItemId }).HasDatabaseName("IX_bms_web_CashFlowActualsArticles_Mapping");
        builder.HasOne<CashFlowActualsSnapshot>().WithMany().HasForeignKey(row => row.SnapshotId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CashFlowItemReference>().WithMany().HasForeignKey(row => row.CashFlowItemId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CashFlowActualsRowConfiguration : IEntityTypeConfiguration<CashFlowActualsRow>
{
    public void Configure(EntityTypeBuilder<CashFlowActualsRow> builder)
    {
        builder.ToTable("CashFlowActualsRows", "bms_web", table => table.UseSqlOutputClause(false));
        builder.HasKey(row => new { row.SnapshotId, row.SourceOrdinal }).HasName("PK_bms_web_CashFlowActualsRows");
        builder.Property(row => row.BudgetMonth).HasColumnType("date");
        builder.Property(row => row.SignedAmountKzt).HasPrecision(18, 2);
        builder.HasIndex(row => new { row.SnapshotId, row.ArticleOrdinal, row.BudgetMonth }).IsUnique().HasDatabaseName("UX_bms_web_CashFlowActualsRows_Cell");
        builder.HasOne<CashFlowActualsArticle>().WithMany().HasForeignKey(row => new { row.SnapshotId, row.ArticleOrdinal }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CashFlowActualsMonth>().WithMany().HasForeignKey(row => new { row.SnapshotId, row.BudgetMonth }).OnDelete(DeleteBehavior.NoAction);
    }
}
