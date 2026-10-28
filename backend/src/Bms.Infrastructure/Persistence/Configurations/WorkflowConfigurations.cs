using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

/// <summary>
/// Displayed statuses and their logical-state codes (OPEN-01). Provisional rows
/// are persisted for review only and cannot be executed by WorkflowResolver.
/// </summary>
public sealed class DocumentStatusConfiguration : IEntityTypeConfiguration<DocumentStatus>
{
    public void Configure(EntityTypeBuilder<DocumentStatus> builder)
    {
        builder.ToTable("DocumentStatuses", "bms_web");
        builder.HasKey(status => status.Code);
        builder.Property(status => status.Code).HasMaxLength(100).IsRequired();
        builder.Property(status => status.Name).HasMaxLength(200).IsRequired();
        builder.Property(status => status.LogicalStateCode).HasMaxLength(100).IsRequired();
        builder.Property(status => status.Color).HasMaxLength(20).IsRequired();
        builder.Property(status => status.SortOrder).IsRequired();
        builder.Property(status => status.IsProvisional).IsRequired();
    }
}

/// <summary>Route commands. Exact targets belong to versioned rules.</summary>
public sealed class WorkflowCommandConfiguration : IEntityTypeConfiguration<WorkflowCommand>
{
    public void Configure(EntityTypeBuilder<WorkflowCommand> builder)
    {
        builder.ToTable("WorkflowCommands", "bms_web");
        builder.HasKey(command => command.Code);
        builder.Property(command => command.Code).HasMaxLength(100).IsRequired();
        builder.Property(command => command.Name).HasMaxLength(200).IsRequired();
        builder.Property(command => command.RequiresReason).IsRequired();
        builder.Property(command => command.IsApprovalAction).IsRequired();
        builder.Property(command => command.SortOrder).IsRequired();
        builder.Property(command => command.IsProvisional).IsRequired();
    }
}

/// <summary>
/// Versioned Status x Stage x Role x Command transition rules. Exact target and
/// condition are rule data; a missing or provisional row is a denial.
/// </summary>
public sealed class WorkflowPermissionConfiguration : IEntityTypeConfiguration<WorkflowPermission>
{
    public void Configure(EntityTypeBuilder<WorkflowPermission> builder)
    {
        builder.ToTable("WorkflowPermissions", "bms_web");
        builder.HasKey(permission => new
        {
            permission.DefinitionVersion,
            permission.StatusCode,
            permission.StageNumber,
            permission.RoleCode,
            permission.CommandCode,
            permission.ConditionCode
        });
        builder.Property(permission => permission.DefinitionVersion).IsRequired();
        builder.Property(permission => permission.StatusCode).HasMaxLength(100).IsRequired();
        builder.Property(permission => permission.StageNumber).IsRequired();
        builder.Property(permission => permission.RoleCode).HasMaxLength(100).IsRequired();
        builder.Property(permission => permission.CommandCode).HasMaxLength(100).IsRequired();
        builder.Property(permission => permission.TargetStatusCode).HasMaxLength(100);
        builder.Property(permission => permission.TargetStageNumber).IsRequired();
        builder.Property(permission => permission.ConditionCode)
            .HasMaxLength(100)
            .HasDefaultValue(string.Empty)
            .IsRequired();
        builder.Property(permission => permission.IsVisible).IsRequired();
        builder.Property(permission => permission.IsEnabled).IsRequired();
        builder.Property(permission => permission.IsProvisional).IsRequired();

        builder.HasOne<DocumentStatus>()
            .WithMany()
            .HasForeignKey(permission => permission.StatusCode)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentStatus>()
            .WithMany()
            .HasForeignKey(permission => permission.TargetStatusCode)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkflowCommand>()
            .WithMany()
            .HasForeignKey(permission => permission.CommandCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
