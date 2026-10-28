using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class OrganizationReferenceConfiguration : IEntityTypeConfiguration<OrganizationReference>
{
    public void Configure(EntityTypeBuilder<OrganizationReference> builder)
    {
        builder.ToTable("Organizations", "bms_web");
        builder.HasKey(organization => organization.Id);
        builder.Property(organization => organization.Code).HasMaxLength(100).IsRequired();
        builder.Property(organization => organization.Name).HasMaxLength(500).IsRequired();
        builder.HasIndex(organization => organization.Code).IsUnique();
    }
}

public sealed class CostCenterReferenceConfiguration : IEntityTypeConfiguration<CostCenterReference>
{
    public void Configure(EntityTypeBuilder<CostCenterReference> builder)
    {
        builder.ToTable("CostCenters", "bms_web", table => table.UseSqlOutputClause(false));
        builder.HasKey(costCenter => costCenter.Id);
        builder.Property(costCenter => costCenter.Code).HasMaxLength(100).IsRequired();
        builder.Property(costCenter => costCenter.Name).HasMaxLength(500).IsRequired();
        builder.Property(costCenter => costCenter.GroupingName).HasMaxLength(500);
        builder.HasIndex(costCenter => costCenter.Code).IsUnique();
        builder.HasOne<CostCenterReference>()
            .WithMany()
            .HasForeignKey(costCenter => costCenter.ParentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CashFlowItemReferenceConfiguration : IEntityTypeConfiguration<CashFlowItemReference>
{
    public void Configure(EntityTypeBuilder<CashFlowItemReference> builder)
    {
        builder.ToTable("CashFlowItems", "bms_web", table => table.UseSqlOutputClause(false));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code)
            .HasMaxLength(20)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(item => item.Name).HasMaxLength(150).IsRequired();
        builder.Property(item => item.IsHistoricalForActuals).HasDefaultValue(false).IsRequired();
        builder.Property(item => item.ActivityType)
            .HasMaxLength(20)
            .UseCollation("Latin1_General_100_BIN2")
            .HasDefaultValue("Operating")
            .IsRequired();
        builder.Property(item => item.AccrualSettingsVersion)
            .HasDefaultValueSql("NEWID()")
            .IsConcurrencyToken();
        builder.Property(item => item.FlowDirection).HasMaxLength(10)
            .UseCollation("Latin1_General_100_BIN2");
        builder.HasIndex(item => item.Code).IsUnique();
        builder.HasIndex(item => item.CostCenterId);
        builder.HasOne<CostCenterReference>()
            .WithMany()
            .HasForeignKey(item => item.CostCenterId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CashFlowItemReference>()
            .WithMany()
            .HasForeignKey(item => item.ParentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ExpenseItemReferenceConfiguration : IEntityTypeConfiguration<ExpenseItemReference>
{
    public void Configure(EntityTypeBuilder<ExpenseItemReference> builder)
    {
        builder.ToTable("ExpenseItems", "bms_web");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code)
            .HasMaxLength(9)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(item => item.Name).HasMaxLength(250).IsRequired();
        builder.HasIndex(item => item.Code).IsUnique();
    }
}

public sealed class CounterpartyReferenceConfiguration : IEntityTypeConfiguration<CounterpartyReference>
{
    public void Configure(EntityTypeBuilder<CounterpartyReference> builder)
    {
        builder.ToTable("Counterparties", "bms_web");
        builder.HasKey(counterparty => counterparty.Id);
        builder.Property(counterparty => counterparty.Code)
            .HasMaxLength(20)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(counterparty => counterparty.Name)
            .HasMaxLength(150)
            .IsRequired();
        builder.Property(counterparty => counterparty.Bin)
            .HasMaxLength(12)
            .UseCollation("Latin1_General_100_BIN2");
        builder.HasIndex(counterparty => counterparty.Code).IsUnique();
        builder.HasIndex(counterparty => counterparty.Bin)
            .IsUnique()
            .HasFilter("[Bin] IS NOT NULL");
    }
}

public sealed class ContractReferenceConfiguration : IEntityTypeConfiguration<ContractReference>
{
    public void Configure(EntityTypeBuilder<ContractReference> builder)
    {
        builder.ToTable("Contracts", "bms_web");
        builder.HasKey(contract => contract.Id);
        builder.Property(contract => contract.Code)
            .HasMaxLength(30)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(contract => contract.Name)
            .HasMaxLength(150)
            .IsRequired();
        builder.Property(contract => contract.ContractDate)
            .HasColumnType("date")
            .IsRequired();
        builder.HasIndex(contract => contract.Code).IsUnique();
        builder.HasIndex(contract => new
        {
            contract.CounterpartyId,
            contract.OrganizationId,
            contract.IsActive,
            contract.Code
        });
    }
}

public sealed class ActorOrganizationGrantConfiguration : IEntityTypeConfiguration<ActorOrganizationGrant>
{
    public void Configure(EntityTypeBuilder<ActorOrganizationGrant> builder)
    {
        builder.ToTable("ActorOrganizationGrants", "bms_web");
        builder.HasKey(grant => new { grant.ActorExternalId, grant.OrganizationId })
            .IsClustered();
        builder.Property(grant => grant.ActorExternalId)
            .HasMaxLength(256)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(grant => grant.ActorExternalId).Metadata.SetValueComparer(
            IdentifierValueComparers.OrdinalKey);
        builder.HasOne<OrganizationReference>()
            .WithMany()
            .HasForeignKey(grant => grant.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ActorCostCenterAccessConfiguration : IEntityTypeConfiguration<ActorCostCenterAccess>
{
    public void Configure(EntityTypeBuilder<ActorCostCenterAccess> builder)
    {
        builder.ToTable("ActorCostCenterAccess", "bms_web");
        builder.HasKey(access => new
        {
            access.ActorExternalId,
            access.CostCenterId,
            access.EffectiveFrom
        })
            .IsClustered();
        builder.Property(access => access.ActorExternalId)
            .HasMaxLength(256)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(access => access.ActorExternalId).Metadata.SetValueComparer(
            IdentifierValueComparers.OrdinalKey);
        builder.HasOne<CostCenterReference>()
            .WithMany()
            .HasForeignKey(access => access.CostCenterId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
