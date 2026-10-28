using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Bms.Infrastructure.Persistence.Configurations;
public sealed class CostCenterPurpose457Configuration : IEntityTypeConfiguration<CostCenterPurpose457>
{
    public void Configure(EntityTypeBuilder<CostCenterPurpose457> builder)
    {
        builder.ToTable("CostCenterPurposes457", "bms_web");
        builder.HasKey(p => p.CostCenterId).HasName("PK_CostCenterPurposes457");
        builder.Property(p => p.CostCenterId).ValueGeneratedNever();
        builder.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasOne<CostCenterReference>().WithMany().HasForeignKey(p => p.CostCenterId).OnDelete(DeleteBehavior.NoAction);
    }
}
