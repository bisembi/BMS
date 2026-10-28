using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class ExpenseRequestCostCenterCounterConfiguration
    : IEntityTypeConfiguration<ExpenseRequestCostCenterCounter>
{
    public void Configure(EntityTypeBuilder<ExpenseRequestCostCenterCounter> builder)
    {
        builder.ToTable("ExpenseRequestCostCenterCounters", "bms_web", table =>
            table.HasCheckConstraint(
                "CK_bms_web_ExpenseRequestCostCenterCounters_ordinal",
                "LastIssuedOrdinal >= 0"));
        builder.HasKey(row => row.CostCenterId)
            .HasName("PK_bms_web_ExpenseRequestCostCenterCounters")
            .IsClustered();
        builder.Property(row => row.CostCenterId)
            .ValueGeneratedNever()
            .HasColumnOrder(0);
        builder.Property(row => row.Prefix)
            .HasMaxLength(100)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired()
            .HasColumnOrder(1);
        builder.Property(row => row.LastIssuedOrdinal)
            .IsRequired()
            .HasColumnOrder(2);
        builder.HasOne<CostCenterReference>()
            .WithMany()
            .HasForeignKey(row => row.CostCenterId)
            .HasConstraintName("FK_bms_web_ExpenseRequestCostCenterCounters_CostCenter")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
