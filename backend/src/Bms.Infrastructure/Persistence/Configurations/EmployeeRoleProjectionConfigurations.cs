using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class EmployeeReferenceConfiguration : IEntityTypeConfiguration<EmployeeReference>
{
    public void Configure(EntityTypeBuilder<EmployeeReference> builder)
    {
        builder.ToTable("Employees", "bms_web");
        builder.HasKey(employee => employee.Id)
            .HasName("PK_bms_web_Employees")
            .IsClustered();
        builder.Property(employee => employee.Code)
            .HasMaxLength(20)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(employee => employee.Name)
            .HasMaxLength(150)
            .IsRequired();
        builder.Property(employee => employee.SourceDatabaseUserName)
            .HasMaxLength(100);
        builder.HasIndex(employee => employee.Code)
            .IsUnique()
            .HasDatabaseName("UQ_bms_web_Employees_Code");
        builder.HasIndex(employee => new { employee.IsActive, employee.Code })
            .HasDatabaseName("IX_bms_web_Employees_Active_Code");
    }
}

public sealed class FunctionalRoleReferenceConfiguration
    : IEntityTypeConfiguration<FunctionalRoleReference>
{
    public void Configure(EntityTypeBuilder<FunctionalRoleReference> builder)
    {
        builder.ToTable("FunctionalRoles", "bms_web");
        builder.HasKey(role => role.Id)
            .HasName("PK_bms_web_FunctionalRoles")
            .IsClustered();
        builder.Property(role => role.Code)
            .HasMaxLength(30)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(role => role.Name)
            .HasMaxLength(100)
            .IsRequired();
        builder.HasIndex(role => role.Code)
            .IsUnique()
            .HasDatabaseName("UQ_bms_web_FunctionalRoles_Code");
        builder.HasIndex(role => new { role.IsActive, role.Code })
            .HasDatabaseName("IX_bms_web_FunctionalRoles_Active_Code");
    }
}

public sealed class EmployeeFunctionalRoleConfiguration
    : IEntityTypeConfiguration<EmployeeFunctionalRole>
{
    public void Configure(EntityTypeBuilder<EmployeeFunctionalRole> builder)
    {
        builder.ToTable("EmployeeFunctionalRoles", "bms_web");
        builder.HasKey(assignment => new { assignment.EmployeeId, assignment.FunctionalRoleId })
            .HasName("PK_bms_web_EmployeeFunctionalRoles")
            .IsClustered();
        builder.HasIndex(assignment => new
        {
            assignment.FunctionalRoleId,
            assignment.IsActive,
            assignment.EmployeeId
        }).HasDatabaseName("IX_bms_web_EmployeeFunctionalRoles_Role_Active_Employee");
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(assignment => assignment.EmployeeId)
            .HasConstraintName("FK_bms_web_EmployeeFunctionalRoles_Employee")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<FunctionalRoleReference>()
            .WithMany()
            .HasForeignKey(assignment => assignment.FunctionalRoleId)
            .HasConstraintName("FK_bms_web_EmployeeFunctionalRoles_FunctionalRole")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class EmployeeActorMappingConfiguration
    : IEntityTypeConfiguration<EmployeeActorMapping>
{
    public void Configure(EntityTypeBuilder<EmployeeActorMapping> builder)
    {
        builder.ToTable("EmployeeActorMappings", "bms_web");
        builder.HasKey(mapping => mapping.ActorExternalId)
            .HasName("PK_bms_web_EmployeeActorMappings")
            .IsClustered();
        builder.Property(mapping => mapping.ActorExternalId)
            .HasMaxLength(256)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(mapping => mapping.ActorExternalId).Metadata.SetValueComparer(
            IdentifierValueComparers.OrdinalKey);
        builder.HasIndex(mapping => new
        {
            mapping.EmployeeId,
            mapping.IsActive,
            mapping.ActorExternalId
        }).HasDatabaseName("IX_bms_web_EmployeeActorMappings_Employee_Active_Actor");
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(mapping => mapping.EmployeeId)
            .HasConstraintName("FK_bms_web_EmployeeActorMappings_Employee")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ExpenseRequestSubstitutionConfiguration
    : IEntityTypeConfiguration<ExpenseRequestSubstitution>
{
    public void Configure(EntityTypeBuilder<ExpenseRequestSubstitution> builder)
    {
        builder.ToTable("ExpenseRequestSubstitutions", "bms_web");
        builder.HasKey(substitution => substitution.Id)
            .HasName("PK_bms_web_ExpenseRequestSubstitutions")
            .IsClustered();
        builder.Property(substitution => substitution.Reason)
            .HasMaxLength(500)
            .IsRequired();
        builder.HasIndex(substitution => new
        {
            substitution.DeputyEmployeeId,
            substitution.IsActive,
            substitution.ValidFromUtc,
            substitution.ValidThroughUtc,
            substitution.PrincipalEmployeeId,
            substitution.Id
        }).HasDatabaseName(
            "IX_bms_web_ExpenseRequestSubstitutions_Deputy_Active_Period_Principal");
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(substitution => substitution.PrincipalEmployeeId)
            .HasConstraintName("FK_bms_web_ExpenseRequestSubstitutions_PrincipalEmployee")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(substitution => substitution.DeputyEmployeeId)
            .HasConstraintName("FK_bms_web_ExpenseRequestSubstitutions_DeputyEmployee")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class BudgetOwnerCostCenterAssignmentConfiguration
    : IEntityTypeConfiguration<BudgetOwnerCostCenterAssignment>
{
    public void Configure(EntityTypeBuilder<BudgetOwnerCostCenterAssignment> builder)
    {
        builder.ToTable("BudgetOwnerCostCenterAssignments", "bms_web");
        builder.HasKey(assignment => new { assignment.EmployeeId, assignment.CostCenterId })
            .HasName("PK_bms_web_BudgetOwnerCostCenterAssignments")
            .IsClustered();
        builder.HasIndex(assignment => new
        {
            assignment.CostCenterId,
            assignment.IsActive,
            assignment.EmployeeId
        }).HasDatabaseName(
            "IX_bms_web_BudgetOwnerCostCenterAssignments_CostCenter_Active_Employee");
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(assignment => assignment.EmployeeId)
            .HasConstraintName("FK_bms_web_BudgetOwnerCostCenterAssignments_Employee")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CostCenterReference>()
            .WithMany()
            .HasForeignKey(assignment => assignment.CostCenterId)
            .HasConstraintName("FK_bms_web_BudgetOwnerCostCenterAssignments_CostCenter")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
