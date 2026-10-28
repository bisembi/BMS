using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class CfoInitiatorConfiguration : IEntityTypeConfiguration<CfoInitiatorCostCenterAssignment>
{
    public void Configure(EntityTypeBuilder<CfoInitiatorCostCenterAssignment> builder)
    {
        builder.ToTable("CfoInitiatorCostCenterAssignments", "bms_web");
        builder.HasKey(row => new { row.EmployeeId, row.CostCenterId });
        builder.HasOne<EmployeeReference>().WithMany().HasForeignKey(row => row.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CostCenterReference>().WithMany().HasForeignKey(row => row.CostCenterId).OnDelete(DeleteBehavior.NoAction);
    }
}
