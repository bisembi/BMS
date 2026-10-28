using Bms.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class BudgetApprovalSubmissionConfiguration : IEntityTypeConfiguration<BudgetApprovalSubmission>
{
    public void Configure(EntityTypeBuilder<BudgetApprovalSubmission> b)
    {
        b.ToTable("BudgetApprovalSubmissions", "bms_web", table => table.UseSqlOutputClause(false));
        b.HasKey(r => r.Id);
        b.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
        b.Property(r => r.SubmittedByExternalId).HasMaxLength(256).UseCollation("Latin1_General_100_BIN2");
        b.Property(r => r.PositiveExcessKzt).HasPrecision(18, 2);
        b.Property(r => r.State).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(r => new { r.BudgetInstanceId, r.SubmissionNumber }).IsUnique();
        b.HasOne<BudgetInstance>().WithMany().HasForeignKey(r => r.BudgetInstanceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CostCenterReference>().WithMany().HasForeignKey(r => r.CostCenterId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<EmployeeReference>().WithMany().HasForeignKey(r => r.SubmittedByEmployeeId).OnDelete(DeleteBehavior.NoAction);
    }
}
