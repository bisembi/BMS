using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence;

public sealed class ExpenseRequestNumberLeaseRow
{
    public Guid CostCenterId { get; set; }
    public long Ordinal { get; set; }
    public Guid LeaseId { get; set; }
    public string ActorExternalId { get; set; } = "";
    public string Number { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public bool Consumed { get; set; }
}

public sealed class ExpenseRequestNumberLeaseConfiguration : IEntityTypeConfiguration<ExpenseRequestNumberLeaseRow>
{
    public void Configure(EntityTypeBuilder<ExpenseRequestNumberLeaseRow> b)
    {
        b.ToTable("ExpenseRequestNumberLeases", "bms_web", t =>
            t.HasCheckConstraint("CK_ExpenseRequestNumberLeases_Ordinal", "Ordinal > 0"));
        b.HasKey(x => new { x.CostCenterId, x.Ordinal });
        b.HasIndex(x => x.LeaseId).IsUnique();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.ActorExternalId).HasMaxLength(256).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x => x.Number).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.HasOne<CostCenterReference>().WithMany().HasForeignKey(x => x.CostCenterId).OnDelete(DeleteBehavior.NoAction);
        b.Property(x => x.CostCenterId).HasColumnOrder(0);
        b.Property(x => x.Ordinal).HasColumnOrder(1);
        b.Property(x => x.LeaseId).HasColumnOrder(2);
        b.Property(x => x.ActorExternalId).HasColumnOrder(3);
        b.Property(x => x.Number).HasColumnOrder(4);
        b.Property(x => x.ExpiresAtUtc).HasColumnOrder(5);
        b.Property(x => x.Consumed).HasColumnOrder(6);
    }
}
