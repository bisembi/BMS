using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence;

public sealed class BudgetAdjustmentEntity
{
    public Guid Id { get; set; }
    public Guid BaseBudgetId { get; set; }
    public Guid CostCenterId { get; set; }
    public int SequenceNo { get; set; }
    public string Number { get; set; } = "";
    public string State { get; set; } = "Draft";
    public Guid Stamp { get; set; }
    public string Reason { get; set; } = "";
    public string TransfersJson { get; set; } = "[]";
    public string StagesJson { get; set; } = "[]";
    public string DecisionsJson { get; set; } = "[]";
    public string? BasisHash { get; set; }
    public int SubmissionNo { get; set; }
    public Guid AuthorEmployeeId { get; set; }
    public string AuthorExternalId { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByEmployeeId { get; set; }
    public int? ApprovalOrder { get; set; }
}
public sealed class BudgetAdjustmentConfiguration : IEntityTypeConfiguration<BudgetAdjustmentEntity>
{
    public void Configure(EntityTypeBuilder<BudgetAdjustmentEntity> b)
    {
        b.ToTable("BudgetAdjustments","bms_web",t=>t.UseSqlOutputClause(false));
        b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
        b.Property(x=>x.Number).HasMaxLength(80).IsRequired();
        b.Property(x=>x.State).HasMaxLength(20).IsRequired();
        b.Property(x=>x.Reason).HasMaxLength(2000).IsRequired();
        b.Property(x=>x.AuthorExternalId).HasMaxLength(256).IsRequired();
        b.Property(x=>x.BasisHash).HasMaxLength(64);
        b.Property(x=>x.Stamp).IsConcurrencyToken();
        b.Property(x=>x.CreatedAtUtc).HasColumnType("datetimeoffset(3)");
        b.Property(x=>x.UpdatedAtUtc).HasColumnType("datetimeoffset(3)");
        b.Property(x=>x.ApprovedAtUtc).HasColumnType("datetimeoffset(3)");
        b.HasIndex(x=>new{x.BaseBudgetId,x.SequenceNo}).IsUnique().HasDatabaseName("UX_BudgetAdjustments_Base_Sequence");
        b.HasIndex(x=>new{x.BaseBudgetId,x.ApprovalOrder}).IsUnique().HasFilter("[ApprovalOrder] IS NOT NULL").HasDatabaseName("UX_BudgetAdjustments_Base_Approval");
        b.HasIndex(x=>new{x.CostCenterId,x.CreatedAtUtc}).HasDatabaseName("IX_BudgetAdjustments_Cfo_Created");
    }
}
