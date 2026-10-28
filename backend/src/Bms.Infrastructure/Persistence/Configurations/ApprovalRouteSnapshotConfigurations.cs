using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bms.Infrastructure.Persistence.Configurations;

public sealed class PersistedApprovalRouteSnapshotConfiguration
    : IEntityTypeConfiguration<PersistedApprovalRouteSnapshot>
{
    public void Configure(EntityTypeBuilder<PersistedApprovalRouteSnapshot> builder)
    {
        builder.ToTable("ExpenseRequestApprovalRouteSnapshots", "bms_web");
        builder.HasKey(snapshot => new { snapshot.ExpenseRequestId, snapshot.SnapshotNumber })
            .HasName("PK_bms_web_ExpenseRequestApprovalRouteSnapshots")
            .IsClustered();
        builder.Property(snapshot => snapshot.WorkflowDefinitionVersion).IsRequired();
        builder.Property(snapshot => snapshot.PositiveDeficitKzt).HasPrecision(18, 2);
        builder.Property(snapshot => snapshot.CapturedAtUtc).HasPrecision(3);
        builder.HasOne<ExpenseRequest>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.ExpenseRequestId)
            .HasConstraintName("FK_bms_web_ExpenseRequestApprovalRouteSnapshots_ExpenseRequest")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrganizationReference>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.OrganizationId)
            .HasConstraintName("FK_bms_web_ExpenseRequestApprovalRouteSnapshots_Organization")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CostCenterReference>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.CostCenterId)
            .HasConstraintName("FK_bms_web_ExpenseRequestApprovalRouteSnapshots_CostCenter")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PersistedApprovalRouteStageConfiguration
    : IEntityTypeConfiguration<PersistedApprovalRouteStage>
{
    public void Configure(EntityTypeBuilder<PersistedApprovalRouteStage> builder)
    {
        builder.ToTable("ExpenseRequestApprovalRouteStages", "bms_web");
        builder.HasKey(stage => new { stage.ExpenseRequestId, stage.SnapshotNumber, stage.StageNumber })
            .HasName("PK_bms_web_ExpenseRequestApprovalRouteStages")
            .IsClustered();
        builder.Property(stage => stage.RoleCode)
            .HasMaxLength(30)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.HasOne<PersistedApprovalRouteSnapshot>()
            .WithMany()
            .HasForeignKey(stage => new { stage.ExpenseRequestId, stage.SnapshotNumber })
            .HasConstraintName("FK_bms_web_ExpenseRequestApprovalRouteStages_Snapshot")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PersistedApprovalRouteStageParticipantConfiguration
    : IEntityTypeConfiguration<PersistedApprovalRouteStageParticipant>
{
    public void Configure(EntityTypeBuilder<PersistedApprovalRouteStageParticipant> builder)
    {
        builder.ToTable("ExpenseRequestApprovalRouteStageParticipants", "bms_web");
        builder.HasKey(participant => new
        {
            participant.ExpenseRequestId,
            participant.SnapshotNumber,
            participant.StageNumber,
            participant.EmployeeId
        }).HasName("PK_bms_web_ExpenseRequestApprovalRouteStageParticipants")
            .IsClustered();
        builder.HasIndex(participant => new
        {
            participant.EmployeeId,
            participant.ExpenseRequestId,
            participant.SnapshotNumber,
            participant.StageNumber
        }).HasDatabaseName(
            "IX_bms_web_ExpenseRequestApprovalRouteStageParticipants_Employee_Snapshot_Stage");
        builder.HasOne<PersistedApprovalRouteStage>()
            .WithMany()
            .HasForeignKey(participant => new
            {
                participant.ExpenseRequestId,
                participant.SnapshotNumber,
                participant.StageNumber
            }).HasConstraintName(
                "FK_bms_web_ExpenseRequestApprovalRouteStageParticipants_Stage")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<EmployeeReference>()
            .WithMany()
            .HasForeignKey(participant => participant.EmployeeId)
            .HasConstraintName(
                "FK_bms_web_ExpenseRequestApprovalRouteStageParticipants_Employee")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
