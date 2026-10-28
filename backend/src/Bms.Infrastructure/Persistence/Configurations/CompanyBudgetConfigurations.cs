using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class CompanyBudgetVersionDeletionConfiguration : IEntityTypeConfiguration<CompanyBudgetVersionDeletion>
{
    public void Configure(EntityTypeBuilder<CompanyBudgetVersionDeletion> b)
    {
        b.ToTable("CompanyBudgetVersionDeletions", "bms_web", t => t.UseSqlOutputClause(false));
        b.HasKey(x => x.VersionId);
        b.Property(x => x.ActorExternalId).HasMaxLength(256).UseCollation("Latin1_General_100_BIN2");
        b.Property(x => x.DeletedAtUtc).HasColumnType("datetimeoffset(3)");
        b.HasOne<CompanyBudgetVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<EmployeeReference>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CompanyBudgetInstanceCreationConfiguration : IEntityTypeConfiguration<CompanyBudgetInstanceCreation>
{
    public void Configure(EntityTypeBuilder<CompanyBudgetInstanceCreation> b)
    {
        b.ToTable("CompanyBudgetInstanceCreations", "bms_web", t => t.UseSqlOutputClause(false));
        b.HasKey(x => x.BudgetInstanceId);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(3)");
        b.Property(x => x.Source).HasMaxLength(20).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.HasOne<Bms.Domain.Budgeting.BudgetInstance>().WithMany().HasForeignKey(x => x.BudgetInstanceId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CompanyBudgetVersionConfiguration : IEntityTypeConfiguration<CompanyBudgetVersion>
{
    public void Configure(EntityTypeBuilder<CompanyBudgetVersion> b)
    {
        b.ToTable("CompanyBudgetVersions", "bms_web", t => t.UseSqlOutputClause(false));
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(3)");
        b.Property(x => x.PublishedAtUtc).HasColumnType("datetimeoffset(3)");
        b.Property(x => x.CreatedByExternalId).HasMaxLength(256).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.HasOne<EmployeeReference>().WithMany().HasForeignKey(x => x.CreatedByEmployeeId).OnDelete(DeleteBehavior.NoAction);
    }
}
public sealed class CompanyBudgetVersionInstanceConfiguration : IEntityTypeConfiguration<CompanyBudgetVersionInstance>
{
    public void Configure(EntityTypeBuilder<CompanyBudgetVersionInstance> b)
    {
        b.ToTable("CompanyBudgetVersionInstances", "bms_web", t => t.UseSqlOutputClause(false));
        b.HasKey(x => new { x.VersionId, x.BudgetInstanceId });
        b.HasOne<CompanyBudgetVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Bms.Domain.Budgeting.BudgetInstance>().WithMany().HasForeignKey(x => x.BudgetInstanceId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.BudgetInstanceId);
    }
}
public sealed class CompanyBudgetActiveVersionConfiguration : IEntityTypeConfiguration<CompanyBudgetActiveVersion>
{
    public void Configure(EntityTypeBuilder<CompanyBudgetActiveVersion> b)
    {
        b.ToTable("CompanyBudgetActiveVersion", "bms_web", t => t.UseSqlOutputClause(false));
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        b.Property(x => x.ActivatedAtUtc).HasColumnType("datetimeoffset(3)");
        b.Property(x => x.ActivatedByExternalId).HasMaxLength(256).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.HasOne<CompanyBudgetVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<EmployeeReference>().WithMany().HasForeignKey(x => x.ActivatedByEmployeeId).OnDelete(DeleteBehavior.NoAction);
    }
}
public sealed class CompanyBudgetApprovalBaselineConfiguration : IEntityTypeConfiguration<CompanyBudgetApprovalBaseline>
{
    public void Configure(EntityTypeBuilder<CompanyBudgetApprovalBaseline> b)
    {
        b.ToTable("CompanyBudgetApprovalBaselines", "bms_web", t => t.UseSqlOutputClause(false)); b.HasKey(x => x.ReviewId);
        b.HasOne<Bms.Domain.Budgeting.BudgetApprovalSubmission>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CompanyBudgetVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.NoAction);
    }
}
public sealed class CompanyExpenseApprovalBaselineConfiguration : IEntityTypeConfiguration<CompanyExpenseApprovalBaseline>
{
    public void Configure(EntityTypeBuilder<CompanyExpenseApprovalBaseline> b)
    {
        b.ToTable("CompanyExpenseApprovalBaselines", "bms_web", t => t.UseSqlOutputClause(false));
        b.HasKey(x => new { x.ExpenseRequestId, x.SnapshotNumber });
        b.HasOne<CompanyBudgetVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<PersistedApprovalRouteSnapshot>().WithMany().HasForeignKey(x => new { x.ExpenseRequestId, x.SnapshotNumber })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
public sealed class CompanyBudgetActivationConfiguration : IEntityTypeConfiguration<CompanyBudgetActivation>
{
    public void Configure(EntityTypeBuilder<CompanyBudgetActivation> b)
    {
        b.ToTable("CompanyBudgetActivations", "bms_web", t => t.UseSqlOutputClause(false)); b.HasKey(x => x.Id);
        b.Property(x => x.ActivatedAtUtc).HasColumnType("datetimeoffset(3)");
        b.Property(x => x.ActorExternalId).HasMaxLength(256).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.HasOne<CompanyBudgetVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CompanyBudgetVersion>().WithMany().HasForeignKey(x => x.PreviousVersionId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<EmployeeReference>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.ActiveConcurrencyStamp).IsUnique();
    }
}
