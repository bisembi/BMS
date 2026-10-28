using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class BudgetCostCenterSettingsConfiguration : IEntityTypeConfiguration<BudgetCostCenterSettings>
{
    public void Configure(EntityTypeBuilder<BudgetCostCenterSettings> builder)
    {
        builder.ToTable("BudgetCostCenterSettings", "bms_web", table =>
            table.HasCheckConstraint("CK_bms_web_BudgetCostCenterSettings_stamp",
                "ConcurrencyStamp <> CONVERT(uniqueidentifier, '00000000-0000-0000-0000-000000000000')"));
        builder.HasKey(row => row.CostCenterId).HasName("PK_bms_web_BudgetCostCenterSettings").IsClustered();
        builder.Property(row => row.CostCenterId).ValueGeneratedNever().HasColumnOrder(0);
        builder.Property(row => row.Abbreviation).HasMaxLength(6).UseCollation("Latin1_General_100_BIN2").HasColumnOrder(1);
        builder.Property(row => row.ConcurrencyStamp).IsConcurrencyToken().HasColumnOrder(2);
        builder.HasOne<CostCenterReference>().WithMany().HasForeignKey(row => row.CostCenterId)
            .HasConstraintName("FK_bms_web_BudgetCostCenterSettings_CostCenter").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<BudgetPrefixOwner>().WithMany()
            .HasForeignKey(row => new { row.Abbreviation, row.CostCenterId })
            .HasPrincipalKey(row => new { row.CanonicalPrefix, row.CostCenterId })
            .HasConstraintName("FK_bms_web_BudgetCostCenterSettings_PrefixOwner").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(row => new { row.Abbreviation, row.CostCenterId })
            .HasDatabaseName("IX_bms_web_BudgetCostCenterSettings_PrefixOwner");
    }
}

public sealed class BudgetPrefixOwnerConfiguration : IEntityTypeConfiguration<BudgetPrefixOwner>
{
    public void Configure(EntityTypeBuilder<BudgetPrefixOwner> builder)
    {
        builder.ToTable("BudgetPrefixOwners", "bms_web", table =>
            table.HasCheckConstraint("CK_bms_web_BudgetPrefixOwners_storage_capacity",
                "DATALENGTH(CanonicalPrefix) BETWEEN 4 AND 12"));
        builder.HasKey(row => row.CanonicalPrefix).HasName("PK_bms_web_BudgetPrefixOwners").IsClustered();
        builder.HasAlternateKey(row => new { row.CanonicalPrefix, row.CostCenterId })
            .HasName("UX_bms_web_BudgetPrefixOwners_Prefix_CostCenter");
        builder.Property(row => row.CanonicalPrefix).HasMaxLength(6).UseCollation("Latin1_General_100_BIN2").HasColumnOrder(0);
        builder.Property(row => row.CostCenterId).HasColumnOrder(1);
        builder.HasOne<CostCenterReference>().WithMany().HasForeignKey(row => row.CostCenterId)
            .HasConstraintName("FK_bms_web_BudgetPrefixOwners_CostCenter").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(row => row.CostCenterId).HasDatabaseName("IX_bms_web_BudgetPrefixOwners_CostCenter");
    }
}

public sealed class BudgetCostCenterCounterConfiguration : IEntityTypeConfiguration<BudgetCostCenterCounter>
{
    public void Configure(EntityTypeBuilder<BudgetCostCenterCounter> builder)
    {
        builder.ToTable("BudgetCostCenterCounters", "bms_web", table =>
            table.HasCheckConstraint("CK_bms_web_BudgetCostCenterCounters_ordinal", "LastIssuedOrdinal >= 0"));
        builder.HasKey(row => row.CostCenterId).HasName("PK_bms_web_BudgetCostCenterCounters").IsClustered();
        builder.Property(row => row.CostCenterId).ValueGeneratedNever().HasColumnOrder(0);
        builder.Property(row => row.LastIssuedOrdinal).HasColumnOrder(1);
        builder.HasOne<BudgetCostCenterSettings>().WithMany().HasForeignKey(row => row.CostCenterId)
            .HasConstraintName("FK_bms_web_BudgetCostCenterCounters_Settings").OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class BudgetCostCenterMembershipConfiguration : IEntityTypeConfiguration<BudgetCostCenterMembership>
{
    public void Configure(EntityTypeBuilder<BudgetCostCenterMembership> builder)
    {
        builder.ToTable("BudgetCostCenterMemberships", "bms_web", table =>
            table.HasCheckConstraint("CK_bms_web_BudgetCostCenterMemberships_kind",
                "(MembershipKind = N'OWNER' AND DATALENGTH(MembershipKind) = 10) OR (MembershipKind = N'PARTICIPANT' AND DATALENGTH(MembershipKind) = 22)"));
        builder.HasKey(row => new { row.EmployeeId, row.CostCenterId }).HasName("PK_bms_web_BudgetCostCenterMemberships").IsClustered();
        builder.Property(row => row.EmployeeId).HasColumnOrder(0);
        builder.Property(row => row.CostCenterId).HasColumnOrder(1);
        builder.Property(row => row.MembershipKind).HasMaxLength(16).UseCollation("Latin1_General_100_BIN2").HasColumnOrder(2);
        builder.Property(row => row.IsActive).HasColumnOrder(3);
        builder.HasOne<EmployeeReference>().WithMany().HasForeignKey(row => row.EmployeeId)
            .HasConstraintName("FK_bms_web_BudgetCostCenterMemberships_Employee").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<BudgetCostCenterSettings>().WithMany().HasForeignKey(row => row.CostCenterId)
            .HasConstraintName("FK_bms_web_BudgetCostCenterMemberships_Settings").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(row => new { row.CostCenterId, row.IsActive, row.EmployeeId })
            .HasDatabaseName("IX_bms_web_BudgetCostCenterMemberships_CostCenter_Active_Employee");
    }
}
